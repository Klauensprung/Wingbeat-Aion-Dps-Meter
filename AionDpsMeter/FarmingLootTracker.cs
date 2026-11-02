using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

// J1 farming totals use a replacement inventory baseline, independently of the
// merged depot baselines used for dungeon rewards. No raw batches are retained.
internal sealed class FarmingLootTracker
{
    readonly Dictionary<long, long> stacks = new();
    readonly FarmingCounterLedger totals;
    bool baseline, overflow;
    long latest = -1;
    internal int RetainedStacks => stacks.Count;
    internal FarmingLootTracker(FarmingCounterLedger totals) => this.totals = totals;
    internal void Reset(long at) { if (at < latest) at = latest; latest = at; stacks.Clear(); baseline = overflow = false; }
    internal void Apply(NetworkEvent message)
    {
        if (message.Kind is not ("inventory" or "inventory-update") || message.Timestamp < 0 || message.Timestamp < latest) return;
        InventoryBatch? batch;
        try { batch = message.Fields.Deserialize<InventoryBatch>(); } catch (JsonException) { return; }
        if (batch == null || batch.Snapshot != (message.Kind == "inventory") || batch.Stacks is not { Length: <= 8192 } ||
            batch.Stacks.Any(s => s == null || s.InstanceId <= 0 || s.ItemId <= 0 || s.Count < 0 || s.Modification > 2)) return;
        latest = message.Timestamp;
        if (batch.Snapshot) { stacks.Clear(); baseline = true; overflow = false; }
        if (overflow) return;
        // Fail the entire batch on overflow, rather than credit a partial batch
        // and later misinterpret missing baseline counts as newly acquired loot.
        int added = batch.Stacks.Select(s => s.InstanceId).Distinct().Count(id => !stacks.ContainsKey(id));
        if (stacks.Count + added > 16384) { stacks.Clear(); baseline = false; overflow = true; return; }
        foreach (var row in batch.Stacks) {
            long? before = stacks.TryGetValue(row.InstanceId, out long previous) ? previous : row.Modification == 0 || baseline ? 0 : null;
            long count = !batch.Snapshot && row.Modification == 2 ? 0 : row.Count;
            stacks[row.InstanceId] = count;
            if (!batch.Snapshot && before is long old && count > old && InventoryLootTracker.Source(row.IncreaseReason) != null)
                totals.Record(row.ItemId, count - old);
        }
    }
}
