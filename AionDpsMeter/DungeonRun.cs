using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

public sealed record DungeonItemTotal(int ItemId, decimal Count);
public sealed record DungeonBossKill(int NpcId, long Timestamp);
public sealed record DungeonSplit(long Timestamp, string Event, int Stage, int Step, bool Done);
public sealed record DungeonRun(string Id, int MapId, long FirstObserved, long LastObserved, byte State,
    long? StartedAt = null, long? Deadline = null, long? ClearedAt = null, int? ClearLevel = null, bool SawPrepare = false)
{
    public long? PreparedAt { get; init; }
    public long EnteredAt => PreparedAt ?? FirstObserved;
    public DungeonSplit[] Splits { get; init; } = [];
    public int OmittedSplits { get; init; }
    public int[] Skills { get; init; } = [];
    public long OmittedSkillEvents { get; init; }
    public PartyMemberSummary[] Party { get; init; } = [];
    public long OmittedPartyObservations { get; init; }
    public DungeonBossKill[]? BossKills { get; init; }
    public int? ReportedDeaths { get; init; }
    public DungeonItemTotal[] UsedItems { get; init; } = [];
    public decimal OmittedUses { get; init; }
    public DungeonItemTotal[] ReceivedItems { get; init; } = [];
    public decimal OmittedReceived { get; init; }
    internal bool HasProgress => Party.Length > 0 || Skills.Length > 0 || OmittedSkillEvents > 0 || Splits.Length > 0 || OmittedSplits > 0 || ReceivedItems.Length > 0 || OmittedReceived > 0 || UsedItems.Length > 0 || OmittedUses > 0 || ClearedAt != null || BossKills?.Length > 0 || ReportedDeaths != null;
    public double? ClearSeconds => StartedAt is long start && ClearedAt is long end && end >= start ? (end - start) / 1000d : null;
}
internal sealed class DungeonRunTracker(bool retainCompleted = false)
{
    readonly List<DungeonRun> pendingCompleted=new();
    internal long OmittedCompletedRuns {get;private set;}
    void RetainCompleted() {
        if(!retainCompleted||Current==null)return;
        if(pendingCompleted.Count==32){pendingCompleted.RemoveAt(0);OmittedCompletedRuns++;}
        pendingCompleted.Add(Current);
    }
    internal DungeonRun[] FinishPending() {
        var result=pendingCompleted.Where(r=>r.HasProgress).ToArray();pendingCompleted.Clear();retainCompleted=false;return result;
    }
    internal DungeonRun? Current { get; private set; }
    long latest = -1;
    bool ended;
    PartyMemberSummary[] party = [];
    long partyTime = -1;
    int? observedMap;
    internal void Clear() { Current = null; latest = -1; ended = false; party = []; partyTime = -1; observedMap = null; pendingCompleted.Clear();OmittedCompletedRuns=0; }
    internal static bool Same(DungeonRun? a, DungeonRun? b) => a?.Id == b?.Id;
    internal static void Validate(DungeonRun run) {
        if (run.Party == null || run.Party.Length > 256 || run.OmittedPartyObservations < 0 || run.Party.Any(m => m == null ||
            string.IsNullOrWhiteSpace(m.Name) || m.Name.Length > 4096 || m.Job == null || m.Job.Length > 80 || m.Server < 0 || m.Level < 0 || m.CombatPower < 0) ||
            run.Party.Select(m => (m.Name, m.Server)).Distinct().Count() != run.Party.Length) throw new InvalidDataException("Invalid dungeon roster summary.");
        if (run.Skills == null || run.Skills.Length > 256 || run.Skills.Any(s => s is < 11000000 or > 19999999) ||
            run.Skills.Distinct().Count() != run.Skills.Length || run.OmittedSkillEvents < 0) throw new InvalidDataException("Invalid dungeon skill summary.");
        if (run.Splits == null || run.Splits.Length > 256 || run.OmittedSplits < 0 || run.Splits.Any(s => s == null || s.Event == null || s.Event.Length > 4096 ||
            s.Stage < 0 || s.Step < 0 || s.Timestamp < run.EnteredAt || s.Timestamp > run.LastObserved) ||
            run.Splits.Zip(run.Splits.Skip(1)).Any(pair => pair.First.Timestamp > pair.Second.Timestamp)) throw new InvalidDataException("Invalid dungeon checkpoints.");
        if (run.ReceivedItems == null || run.ReceivedItems.Length > 1024 || run.ReceivedItems.Any(i => i == null || i.ItemId <= 0 || i.Count <= 0) ||
            run.ReceivedItems.Select(i => i.ItemId).Distinct().Count() != run.ReceivedItems.Length || run.OmittedReceived < 0) throw new InvalidDataException("Invalid dungeon reward summary.");
        if (run.UsedItems == null || run.UsedItems.Length > 1024 || run.UsedItems.Any(i => i == null || i.ItemId <= 0 || i.Count <= 0) ||
            run.UsedItems.Select(i => i.ItemId).Distinct().Count() != run.UsedItems.Length || run.OmittedUses < 0) throw new InvalidDataException("Invalid dungeon item-use summary.");
        if (!Guid.TryParseExact(run.Id, "N", out _) || run.MapId <= 0 || run.State > 4 || run.FirstObserved < 0 || run.LastObserved < run.FirstObserved ||
            run.PreparedAt < run.FirstObserved || run.PreparedAt > run.LastObserved || run.PreparedAt != null && !run.SawPrepare ||
            run.StartedAt is < 1577836800000L or > 4102444800000L || run.Deadline is < 1577836800000L or > 4102444800000L ||
            run.ClearedAt < run.FirstObserved || run.ClearedAt > run.LastObserved || run.ClearLevel < 0 || run.ReportedDeaths < 0 ||
            run.BossKills is { } kills && (kills.Length > 64 || kills.Any(k => k == null || k.NpcId <= 0 || k.Timestamp < run.FirstObserved || k.Timestamp > run.LastObserved) ||
                kills.Select(k => k.NpcId).Distinct().Count() != kills.Length))
            throw new InvalidDataException("Invalid dungeon run summary.");
    }
    internal void RecordParty(PartyRoster roster) {
        if (roster.Timestamp <= 0 || roster.Timestamp < partyTime) return;
        FightMetadata.Validate(roster);
        party = roster.Summaries.GroupBy(m => (m.Name, m.Server)).Select(g => g.Last()).ToArray();
        partyTime = roster.Timestamp;
        // Leaving changes the pending roster but keeps everyone observed in this run.
        if (Current == null || roster.Timestamp < latest || party.Length == 0) return;
        var keys = party.Select(m => (m.Name, m.Server)).ToHashSet();
        var merged = Current.Party.Where(m => !keys.Contains((m.Name, m.Server))).Concat(party).ToArray();
        Current = Current with { LastObserved = roster.Timestamp, Party = merged.Take(256).ToArray(),
            OmittedPartyObservations = Current.OmittedPartyObservations > long.MaxValue - Math.Max(0, merged.Length - 256) ? long.MaxValue : Current.OmittedPartyObservations + Math.Max(0, merged.Length - 256) };
        latest = roster.Timestamp;
    }
    internal void RecordSkill(long timestamp, int skill) {
        if (Current == null || timestamp < latest || skill is < 11000000 or > 19999999) return;
        if (skill % 1000000 >= 10000) skill = skill / 10000 * 10000;
        if (Current.Skills.Contains(skill)) return;
        Current = Current with { LastObserved = timestamp,
            Skills = Current.Skills.Length < 256 ? [..Current.Skills, skill] : Current.Skills,
            OmittedSkillEvents = Current.Skills.Length < 256 ? Current.OmittedSkillEvents : Current.OmittedSkillEvents == long.MaxValue ? long.MaxValue : Current.OmittedSkillEvents + 1 };
        latest = timestamp;
    }
    internal void RecordSplit(MapEventProgress? progress) {
        if (Current == null || progress == null || progress.Timestamp < latest || progress.Name.Length > 4096 || progress.Stage < 0 || progress.Step < 0) return;
        Current = Current with { LastObserved = progress.Timestamp,
            Splits = Current.Splits.Length < 256 ? [..Current.Splits, new(progress.Timestamp, progress.Name, progress.Stage, progress.Step, progress.Done)] : Current.Splits,
            OmittedSplits = Current.Splits.Length < 256 ? Current.OmittedSplits : (int)Math.Min(int.MaxValue, (long)Current.OmittedSplits + 1) };
        latest = progress.Timestamp;
    }
    internal void RecordDeferred(PendingInventoryReceipt receipt) {
        if(receipt.RunId!=Current?.Id) {
            int index=pendingCompleted.FindIndex(r=>r.Id==receipt.RunId&&r.MapId==receipt.MapId);
            if(index>=0) {
                var correction=new DungeonRunTracker {Current=pendingCompleted[index],latest=pendingCompleted[index].LastObserved};
                correction.RecordDeferred(receipt);pendingCompleted[index]=correction.Current!;
            }
            return;
        }
        if(Current==null||receipt.RunId!=Current.Id||receipt.MapId!=Current.MapId||receipt.Timestamp<Current.FirstObserved)return;
        long at=Math.Max(latest,receipt.Timestamp);
        if(receipt.Gain is {} gain)RecordRewards(at,[gain]);
        if(receipt.Use is {} use)RecordUses(at,[use]);
    }
    internal void RecordDeferredLoss(PendingInventoryLoss loss) {
        if(loss.RunId!=Current?.Id) {
            int index=pendingCompleted.FindIndex(r=>r.Id==loss.RunId&&r.MapId==loss.MapId);
            if(index>=0&&loss.Received>=0&&loss.Used>=0)pendingCompleted[index]=pendingCompleted[index] with {
                OmittedReceived=pendingCompleted[index].OmittedReceived+loss.Received,OmittedUses=pendingCompleted[index].OmittedUses+loss.Used};
            return;
        }
        if(Current==null||loss.RunId!=Current.Id||loss.MapId!=Current.MapId||loss.Received<0||loss.Used<0)return;
        Current=Current with {OmittedReceived=Current.OmittedReceived+loss.Received,OmittedUses=Current.OmittedUses+loss.Used};
    }
    internal void RecordUses(long timestamp, InventoryUse[] uses) {
        if (Current == null || timestamp < latest || uses.Length == 0) return;
        var totals = Current.UsedItems.ToDictionary(i => i.ItemId, i => i.Count); decimal omitted = Current.OmittedUses;
        foreach (var use in uses) {
            if (use.ItemId <= 0 || use.Count <= 0) continue;
            if (!totals.ContainsKey(use.ItemId) && totals.Count >= 1024) omitted += use.Count;
            else totals[use.ItemId] = totals.GetValueOrDefault(use.ItemId) + use.Count;
        }
        Current = Current with { LastObserved = timestamp, UsedItems = totals.Select(p => new DungeonItemTotal(p.Key, p.Value)).ToArray(), OmittedUses = omitted };
        latest = timestamp;
    }
    internal void RecordRewards(long timestamp, LootPickup[] gains) {
        if (Current == null || timestamp < latest || gains.Length == 0) return;
        var totals = Current.ReceivedItems.ToDictionary(i => i.ItemId, i => i.Count); decimal omitted = Current.OmittedReceived;
        foreach (var gain in gains) {
            if (gain.ItemId <= 0 || gain.Quantity <= 0) continue;
            if (!totals.ContainsKey(gain.ItemId) && totals.Count >= 1024) omitted += gain.Quantity;
            else totals[gain.ItemId] = totals.GetValueOrDefault(gain.ItemId) + gain.Quantity;
        }
        Current = Current with { LastObserved = timestamp, ReceivedItems = totals.Select(p => new DungeonItemTotal(p.Key, p.Value)).ToArray(), OmittedReceived = omitted };
        latest = timestamp;
    }
    internal void Apply(NetworkEvent e) {
        if (e.Timestamp < 0 || e.Timestamp < latest || e.Fields.ValueKind != JsonValueKind.Object) return;
        if (e.Kind == "local-zone") {
            if (!e.Fields.TryGetProperty("MapId", out var mapValue) || mapValue.ValueKind != JsonValueKind.Number || !mapValue.TryGetInt32(out int mapId) || mapId <= 0) return;
            observedMap = mapId;
            if (Current?.MapId == mapId) Current = Current with { LastObserved = e.Timestamp };
            else {
                RetainCompleted();
                Current = DungeonMaps.Contains(mapId) ? new(Guid.NewGuid().ToString("N"), mapId, e.Timestamp, e.Timestamp, 0) { Party = [..party] } : null;
                ended = false;
            }
            latest = e.Timestamp; return;
        }
        if (e.Kind is "dungeon-boss-killed" or "dungeon-deaths") {
            if (Current == null) return;
            string field = e.Kind == "dungeon-boss-killed" ? "NpcId" : "Count";
            if (!e.Fields.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int count) || count < 0) return;
            var progress = Current with { LastObserved = e.Timestamp };
            if (e.Kind == "dungeon-deaths") progress = progress with { ReportedDeaths = count };
            else {
                if (count == 0) return;
                var kills = progress.BossKills ?? [];
                if (!kills.Any(k => k.NpcId == count)) {
                    if (kills.Length >= 64) return;
                    progress = progress with { BossKills = [..kills, new(count, e.Timestamp)] };
                }
            }
            Validate(progress); Current = progress; latest = e.Timestamp; return;
        }
        if (e.Kind is not ("dungeon-state" or "dungeon-result")) return;
        DungeonStateReading? state = null; DungeonResultReading? result = null;
        try {
            if (e.Kind == "dungeon-state") state = e.Fields.Deserialize<DungeonStateReading>();
            else result = e.Fields.Deserialize<DungeonResultReading>();
        } catch (JsonException) { return; }
        int map = state?.MapId ?? result?.MapId ?? 0;
        if (map <= 0 || state?.State > 4 || observedMap is int knownMap && map != knownMap) return;
        // Process timers can be refreshed within one run. Only an explicit end
        // followed by prepare establishes a same-map restart.
        bool fresh = Current == null || Current.MapId != map || state is { State: 1 } && ended;
        if (fresh) {RetainCompleted();ended = false;}
        var next = fresh ? new DungeonRun(Guid.NewGuid().ToString("N"), map, e.Timestamp, e.Timestamp, 0) { Party = [..party] } : Current!;
        next = next with { LastObserved = e.Timestamp };
        if (state != null) {
            if (state.State == 4) ended = true;
            if (state.State == 1 && !next.SawPrepare) next = next with { PreparedAt = e.Timestamp, Splits = [], OmittedSplits = 0 };
            next = next with { State = state.State, SawPrepare = next.SawPrepare || state.State == 1 };
            // Complete-state timestamps describe a post-clear timer, not run duration.
            if (state.State == 2 && next.ClearedAt == null)
                next = next with { StartedAt = next.StartedAt ?? state.StartedAt, Deadline = next.Deadline ?? state.Deadline };
        }
        // The reference completion normalizer treats zero as unspecified.
        // Preserve completion time while allowing a later positive grade.
        if (result?.Succeeded == true) next = next with { ClearedAt = next.ClearedAt ?? e.Timestamp,
            ClearLevel = next.ClearLevel ?? (result.ClearLevel is > 0 ? result.ClearLevel : null) };
        Validate(next); Current = next; latest = e.Timestamp;
    }
}
