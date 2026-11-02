using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

public sealed record LootItemRow(int Session, long SessionStart, string Character, int ItemId, long Quantity, int Pickups, long First, long Last, int MapId = 0)
{
    [JsonIgnore]
    public string Map => ZoneNames.Default.Find(MapId);
    [JsonIgnore]
    public string Item => ItemNames.Default.Find(ItemId);
    [JsonIgnore]
    public string FirstTime => DateTimeOffset.FromUnixTimeMilliseconds(First).ToLocalTime().ToString("dd MMM HH:mm:ss");
    [JsonIgnore]
    public string LastTime => DateTimeOffset.FromUnixTimeMilliseconds(Last).ToLocalTime().ToString("dd MMM HH:mm:ss");
}
public sealed record SavedLootSummary(LootItemRow[] Rows, long OmittedItems, int OmittedSessions);
internal sealed class LootLedger
{
    SavedLootSummary? stored;
    internal bool HasData => current != null || stored?.Rows.Length > 0 || OmittedItems > 0 || OmittedSessions > 0;
    internal int PickupCount => (int)Math.Min(int.MaxValue, stored?.Rows.Sum(r => (long)r.Pickups) ?? sessions.Sum(s => s.Items.Values.Sum(r => (long)r.Pickups)));
    internal LootLedger Copy()
    {
        var copy = new LootLedger { stored = stored == null ? null : stored with { Rows = stored.Rows.ToArray() },
            nextSession = nextSession, Revision = Revision, OmittedItems = OmittedItems, OmittedSessions = OmittedSessions };
        foreach (var session in sessions) {
            var cloned = new Session(session.Id, session.First, session.Character) { Last = session.Last };
            foreach (var pair in session.Items) cloned.Items.Add(pair.Key, pair.Value);
            copy.sessions.Enqueue(cloned);
            if (ReferenceEquals(session, current)) copy.current = cloned;
        }
        return copy;
    }
    internal SavedLootSummary Summary() => new(Rows(), OmittedItems, OmittedSessions);
    internal static void Validate(SavedLootSummary summary) {
        if (summary.Rows == null || summary.Rows.Length > 16384 || summary.OmittedItems < 0 || summary.OmittedSessions < 0 ||
            summary.Rows.Any(r => r == null || r.Session <= 0 || r.SessionStart < 0 || r.SessionStart > r.First || r.First > r.Last ||
                r.Last > 253402300799999 || string.IsNullOrWhiteSpace(r.Character) || r.Character.Length > 256 || r.ItemId <= 0 ||
                r.Quantity <= 0 || r.Pickups <= 0 || r.MapId < 0) ||
            summary.Rows.Sum(r => (long)r.Pickups) > int.MaxValue ||
            summary.Rows.Select(r => (r.Session, r.MapId, r.ItemId)).Distinct().Count() != summary.Rows.Length ||
            summary.Rows.GroupBy(r => r.Session).Any(g => g.Count() > 512 || g.Select(r => (r.SessionStart, r.Character)).Distinct().Count() != 1) ||
            summary.Rows.Select(r => r.Session).Distinct().Count() > 32)
            throw new InvalidDataException("Invalid saved loot summary.");
    }
    internal sealed record OverlayView(LootItemRow[] Rows, bool OmittedItems);
    OverlayView? overlayView;
    long overlayRevision = -1;
    sealed class Session(int id, LootPickup first, string character)
    {
        internal readonly int Id = id;
        internal readonly LootPickup First = first;
        internal readonly string Character = character;
        internal long Last = first.Timestamp;
        internal readonly Dictionary<(int Map, int Item), LootItemRow> Items = new();
    }
    readonly Queue<Session> sessions = new();
    Session? current;
    int nextSession;
    internal long Revision { get; private set; }
    internal long OmittedItems { get; private set; }
    internal void RecordOmitted(long count) {
        if(stored!=null)throw new InvalidOperationException("Saved loot summaries are read-only.");
        if(count<=0)return;
        OmittedItems=count>long.MaxValue-OmittedItems?long.MaxValue:OmittedItems+count;
        Revision++;
    }
    internal int OmittedSessions { get; private set; }
    internal void Add(LootPickup pickup, string character)
    {
        if (stored != null) throw new InvalidOperationException("Saved loot summaries are read-only.");
        bool inventoryIdentity = Guid.TryParseExact(pickup.InventorySession, "N", out _) &&
            !string.IsNullOrWhiteSpace(pickup.CapturedCharacter) && pickup.CapturedCharacter.Length <= 256;
        if (pickup.Timestamp is < 0 or > 253402300799999 || pickup.ItemId <= 0 || pickup.Quantity <= 0 ||
            pickup.CharacterId == 0 && !inventoryIdentity || pickup.Server <= 0 || pickup.MapId < 0) return;
        if (current != null && pickup.Timestamp < current.Last) return;
        if (current == null || (inventoryIdentity ? current.First.InventorySession != pickup.InventorySession : current.First.CharacterId != pickup.CharacterId) ||
            current.First.Server != pickup.Server || pickup.Timestamp - current.Last >= 1800000) {
            current = new(++nextSession, pickup, character); sessions.Enqueue(current);
            if (sessions.Count > 32) { sessions.Dequeue(); OmittedSessions++; }
        }
        current.Last = pickup.Timestamp;
        Revision++;
        var key = (pickup.MapId, pickup.ItemId);
        if (!current.Items.TryGetValue(key, out var row)) {
            if (current.Items.Count >= 512) { OmittedItems++; return; }
            row = new(current.Id, current.First.Timestamp, current.Character, pickup.ItemId, 0, 0, pickup.Timestamp, pickup.Timestamp, pickup.MapId);
        }
        if (row.Quantity > long.MaxValue - pickup.Quantity || row.Pickups == int.MaxValue) { OmittedItems++; return; }
        current.Items[key] = row with { Quantity = row.Quantity + pickup.Quantity, Pickups = row.Pickups + 1, Last = pickup.Timestamp };
    }
    internal LootItemRow[] Rows() => stored?.Rows.ToArray() ?? sessions.Reverse().SelectMany(s => s.Items.Values.OrderByDescending(r => r.Quantity).ThenBy(r => r.ItemId)).ToArray();
    internal LootItemRow[] RecentItems() {
        if (stored == null) return current?.Items.Values.OrderByDescending(r => r.Last).ThenBy(r => r.ItemId).Take(5).ToArray() ?? [];
        int session = stored.Rows.Select(r => r.Session).DefaultIfEmpty().Max();
        return stored.Rows.Where(r => r.Session == session).OrderByDescending(r => r.Last).ThenBy(r => r.ItemId).Take(5).ToArray();
    }
    internal OverlayView OverlaySnapshot()
    {
        if (overlayView == null || overlayRevision != Revision) {
            overlayRevision = Revision; overlayView = new(RecentItems(), OmittedItems > 0);
        }
        return overlayView;
    }
    internal static LootLedger FromFight(Fight fight)
    {
        var summary = fight.SavedLoot ?? fight.LiveLoot?.Summary();
        if (summary != null) {
            Validate(summary);
            return new LootLedger { stored = summary, OmittedItems = summary.OmittedItems, OmittedSessions = summary.OmittedSessions, Revision = 1 };
        }
        var ledger = new LootLedger();
        var seen = new HashSet<(long, uint, uint, int, long, long)>();
        foreach (var message in fight.Network.Where(n => n.Kind == "loot-pickup").OrderBy(n => n.Timestamp)) {
            try {
                var pickup = message.Fields.Deserialize<LootPickup>();
                if (pickup == null || pickup.Timestamp != message.Timestamp || !seen.Add((pickup.Timestamp, pickup.DropId, pickup.CharacterId, pickup.Server, pickup.InventoryInstance, pickup.Sequence))) continue;
                var actor = fight.Actors.Values.FirstOrDefault(a => a.CharacterId == pickup.CharacterId && a.Server == pickup.Server);
                ledger.Add(pickup, pickup.CapturedCharacter ?? actor?.Name ?? "Captured character");
            } catch (JsonException) { }
        }
        return ledger;
    }
}
