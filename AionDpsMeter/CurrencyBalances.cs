namespace AionDpsMeter.Local;

internal sealed record CurrencyBalance(int ItemId, long Count);
internal sealed record CurrencyTicketReading(bool Full, long? Count);

// Current balances, not acquired-loot totals. The item family plus one ticket currency bounds this to 1001 keys.
internal sealed class CurrencyBalances
{
    readonly Dictionary<int, long> amounts = new();
    bool inventorySnapshot, ticketSnapshot;
    internal void Clear() { amounts.Clear(); inventorySnapshot = ticketSnapshot = false; }
    internal void InheritMissing(CurrencyBalances previous)
    {
        foreach (var pair in previous.amounts)
            if (pair.Key == 512000014 ? !ticketSnapshot : !inventorySnapshot) amounts.TryAdd(pair.Key, pair.Value);
        inventorySnapshot |= previous.inventorySnapshot;
        ticketSnapshot |= previous.ticketSnapshot;
    }
    internal void Apply(InventoryBatch batch)
    {
        if (batch.Snapshot) {
            inventorySnapshot = true;
            foreach (int key in amounts.Keys.Where(k => k != 512000014).ToArray()) amounts.Remove(key);
        }
        foreach (var row in batch.Stacks)
            if (row.ItemId is >= 930100000 and <= 930100999)
                amounts[row.ItemId] = !batch.Snapshot && row.Modification == 2 ? 0 : row.Count;
    }
    internal void ApplyTicket(CurrencyTicketReading reading)
    {
        if (reading.Full) { ticketSnapshot = true; amounts.Remove(512000014); }
        if (reading.Count is >= 0) amounts[512000014] = reading.Count.Value;
    }
    internal CurrencyBalance[] Snapshot() => amounts.OrderBy(p => p.Key).Select(p => new CurrencyBalance(p.Key, p.Value)).ToArray();
}
