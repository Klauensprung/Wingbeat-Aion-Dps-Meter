using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal sealed record InventoryUse(int ItemId, long Count);
internal sealed class InventoryLootTracker
{
    const int Capacity = 16384;
    readonly Dictionary<long, (int Item, long Count)> stacks = new();
    readonly CurrencyBalances currencies = new();
    readonly ContentTickets tickets = new();
    readonly InventoryHoldings holdings = new();
    internal InventoryHoldingSnapshot Holdings() => holdings.Snapshot();
    internal CapturedContentTicket[] Tickets() => tickets.Snapshot();
    internal bool TicketsComplete => tickets.Complete;
    internal CurrencyBalance[] Currencies() => currencies.Snapshot();
    internal LootEnergyEvent? Energy { get; private set; }
    internal void ApplyEnergy(LootEnergyEvent value, Actor? local)
    {
        if (local != null) Bind(local);
        if (value.TimestampMs < 0 || value.TimestampMs < latestTicket || value.Daily < 0 || value.Bonus < 0) return;
        Energy = value; latestTicket = value.TimestampMs;
        currencies.ApplyTicket(new(value.FromLoginList, (long)value.Daily + value.Bonus));
    }
    bool baseline, overflow;
    long latest = -1, sequence;
    long latestTicket = -1;
    int entity, server;
    uint character;
    long persistentCharacter;
    string characterName = "";
    string session = Guid.NewGuid().ToString("N");
    internal InventoryUse[] Uses { get; private set; } = [];
    internal int Count => stacks.Count;
    internal bool HasIdentity => entity != 0;
    internal InventoryLootTracker CopyUnassignedCounts()
    {
        var copy=new InventoryLootTracker {baseline=baseline,overflow=overflow,latest=latest};
        foreach(var pair in stacks)copy.stacks.Add(pair.Key,pair.Value);
        return copy;
    }
    internal LootPickup Attribute(LootPickup pickup,Actor local)=>pickup with {CharacterId=local.CharacterId,Server=local.Server,CapturedCharacter=local.Name,InventorySession=session};
    internal bool IsSamePersistentOwner(Actor local) => local.Player && server > 0 && server == local.Server &&
        persistentCharacter > 0 && persistentCharacter == local.PersistentId;
    internal void InheritTransferState(InventoryLootTracker previous)
    {
        if (ReferenceEquals(this, previous) || overflow || previous.overflow) return;
        holdings.InheritMissing(previous.holdings);
        currencies.InheritMissing(previous.currencies);
        tickets.InheritMissing(previous.tickets);
        if (latestTicket < 0) { Energy = previous.Energy; latestTicket = previous.latestTicket; }
        int missing = previous.stacks.Keys.Count(key => !stacks.ContainsKey(key));
        if (stacks.Count + missing > Capacity) return;
        // Updates received during transfer take precedence. Retain only compact
        // counts for instances not mentioned while identity was pending.
        foreach (var pair in previous.stacks) stacks.TryAdd(pair.Key, pair.Value);
        baseline |= previous.baseline;
        latest = Math.Max(latest, previous.latest);
    }
    internal void Clear() { stacks.Clear(); currencies.Clear(); tickets.Clear(); holdings.Clear(); Energy = null; baseline = overflow = false; latest = latestTicket = -1; entity = server = 0; character = 0; persistentCharacter = 0; characterName = ""; Uses = []; session = Guid.NewGuid().ToString("N"); }
    internal void Bind(Actor local, bool reconnect = false)
    {
        if (entity != 0 && (reconnect || entity != local.Id || server > 0 && local.Server > 0 && server != local.Server ||
            persistentCharacter > 0 && local.PersistentId > 0 && persistentCharacter != local.PersistentId ||
            character > 0 && local.CharacterId > 0 && character != local.CharacterId || characterName.Length > 0 && characterName != local.Name)) Clear();
        entity = local.Id; characterName = local.Name;
        // Sparse appearance/link updates must not erase an already-known owner.
        if (local.Server > 0) server = local.Server;
        if (local.CharacterId > 0) character = local.CharacterId;
        if (local.PersistentId > 0) persistentCharacter = local.PersistentId;
    }
    internal static string? Source(byte reason) => reason switch {
        5 or 11 or 12 or 43 or 55 or 56 or 57 or 77 or 82 => "drop",
        31 or 35 or 76 => "box", 41 => "gather", 24 or 25 => "quest",
        58 or 74 or 75 or 85 or 86 or 93 => "other", _ => null
    };
    internal LootPickup[] Apply(NetworkEvent message, Actor? local, bool allowUnassigned = false)
    {
        Uses = [];
        if (message.Kind == "content-tickets") {
            if (local != null) Bind(local);
            try {
                if (message.Fields.Deserialize<ContentTicketReading>() is { } reading) tickets.Apply(reading, message.Timestamp);
            } catch (JsonException) { }
            return [];
        }
        if (message.Kind == "currency-ticket") {
            if (local != null) Bind(local);
            if (message.Timestamp < 0 || message.Timestamp < latestTicket) return [];
            CurrencyTicketReading? reading;
            try { reading = message.Fields.Deserialize<CurrencyTicketReading>(); } catch (JsonException) { return []; }
            if (reading == null || reading.Count < 0 || !reading.Full && reading.Count == null) return [];
            currencies.ApplyTicket(reading); latestTicket = message.Timestamp;
            if (reading.Count == null || Energy != null && (long)Energy.Daily + Energy.Bonus != reading.Count) Energy = null;
            return [];
        }
        if (message.Kind is not ("inventory" or "inventory-update")) return [];
        if (local != null) Bind(local);
        if (message.Timestamp < 0 || message.Timestamp < latest) return [];
        InventoryBatch? batch;
        try { batch = message.Fields.Deserialize<InventoryBatch>(); } catch (JsonException) { return []; }
        // The observed login list carries result 528 with a complete valid
        // inventory. The reference consumes its rows regardless of this field.
        if (batch == null || batch.Snapshot != (message.Kind == "inventory") ||
            batch.Stacks is not { Length: <= 8192 } || batch.Stacks.Any(s => s == null || s.InstanceId <= 0 || s.ItemId <= 0 || s.Count < 0 || s.Modification > 2)) return [];
        latest = message.Timestamp;
        if (batch.Snapshot) {
            // Depot lists refresh their supplied instances; absence is not a
            // removal. The reference retains other known counts across lists.
            // Only a new identity or recovery from capacity overflow resets them.
            if (overflow) stacks.Clear();
            baseline = true; overflow = false;
        }
        if (overflow) return [];
        var gains = new List<LootPickup>();
        var uses = new List<InventoryUse>();
        foreach (var row in batch.Stacks) {
            bool known = stacks.TryGetValue(row.InstanceId, out var old);
            if (!known && stacks.Count >= Capacity) {
                // Losing a count would make subsequent updates look like new loot.
                stacks.Clear(); currencies.Clear(); tickets.Clear(); holdings.Clear(); Energy = null; baseline = false; overflow = true; return [];
            }
            long count = !batch.Snapshot && row.Modification == 2 ? 0 : row.Count;
            stacks[row.InstanceId] = (row.ItemId, count);
            if (batch.Snapshot || known && old.Item != row.ItemId) continue;
            long? before = known ? old.Count : row.Modification == 0 || baseline ? 0 : null;
            string? source = Source(row.IncreaseReason);
            if (before is long consumedFrom && count < consumedFrom && source == null && row.DecreaseReason is 14 or 37 or 38 or 39 or 85 &&
                (allowUnassigned || entity > 0 && server > 0 && local is { Player: true })) uses.Add(new(row.ItemId, consumedFrom - count));
            if (before is long previous && count > previous && source != null && (allowUnassigned || entity > 0 && server > 0 && local is { Player: true }))
                gains.Add(new(message.Timestamp, 0, row.ItemId, count - previous, character, server,
                    InventoryInstance: row.InstanceId, Source: source, Sequence: ++sequence,
                    InventorySession: session, CapturedCharacter: local?.Name));
        }
        Uses = uses.ToArray();
        currencies.Apply(batch);
        holdings.Apply(batch);
        return gains.ToArray();
    }
}
