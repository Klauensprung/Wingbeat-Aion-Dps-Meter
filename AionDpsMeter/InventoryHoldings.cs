namespace AionDpsMeter.Local;

internal sealed record HeldItem(int Id, decimal Count);
internal sealed record InventoryHoldingSnapshot(bool Complete, HeldItem[] Items);
internal sealed class InventoryHoldings
{
    const int Capacity = 16384;
    readonly Dictionary<long, (int Item, long Count)> stacks = new();
    bool complete, overflow;
    internal void Clear() { stacks.Clear(); complete = overflow = false; }
    internal void InheritMissing(InventoryHoldings previous)
    {
        if (complete || overflow || previous.overflow || ReferenceEquals(this, previous)) return;
        if (stacks.Count + previous.stacks.Keys.Count(k => !stacks.ContainsKey(k)) > Capacity) return;
        foreach (var pair in previous.stacks) stacks.TryAdd(pair.Key, pair.Value);
        complete = previous.complete;
    }
    internal void Apply(InventoryBatch batch)
    {
        if (batch.Snapshot) { stacks.Clear(); complete = true; overflow = false; }
        if (overflow) return;
        foreach (var row in batch.Stacks) {
            if (!stacks.ContainsKey(row.InstanceId) && stacks.Count >= Capacity) { Clear(); overflow = true; return; }
            stacks[row.InstanceId] = (row.ItemId, !batch.Snapshot && row.Modification == 2 ? 0 : row.Count);
        }
    }
    internal InventoryHoldingSnapshot Snapshot() => new(complete, stacks.Values.Where(v => v.Count > 0).GroupBy(v => v.Item)
        .Select(g => new HeldItem(g.Key, g.Sum(v => (decimal)v.Count))).OrderBy(v => v.Id).ToArray());
}
