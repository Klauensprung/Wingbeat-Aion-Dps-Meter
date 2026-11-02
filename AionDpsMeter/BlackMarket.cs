using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal sealed record BlackMarketEntry(int Slot, int Item, long Quantity, byte Stage, byte Currency, long BasePrice, long Price, bool SoldOut);
internal sealed record BlackMarketReading(byte Tab, long RefreshAt, long ServerOffset, BlackMarketEntry[] Entries);
internal sealed record BlackMarketPurchase(byte Tab, int Slot);
internal sealed record BlackMarketStock(byte Tab, long RefreshAt, long OpenedAt, BlackMarketEntry[] Entries);

internal static class BlackMarketWire
{
    internal static BlackMarketReading Read(ref WireReader r, long offset)
    {
        byte tab = r.Byte(); int count = r.Byte(); var entries = new BlackMarketEntry[count];
        for (int i = 0; i < count; i++) {
            int slot = unchecked((int)r.U32()), item = unchecked((int)r.U32()); long quantity = unchecked((long)r.U64());
            byte stage = r.Byte(), currency = r.Byte(); long normal = unchecked((long)r.U64()), price = unchecked((long)r.U64());
            r.Bit(); bool purchased = r.Bit();
            entries[i] = new(slot, item, quantity, stage, currency, normal, price, purchased);
        }
        long refresh = unchecked((long)r.U64()); r.U32();
        if (r.Remaining != 0) throw new InvalidDataException("Unexpected black market trailer.");
        return new(tab, refresh, offset, entries);
    }
    internal static BlackMarketPurchase? Purchase(ref WireReader r)
    {
        ushort result = r.U16(); byte tab = r.Byte(); int slot = unchecked((int)r.U32());
        if (r.Remaining != 0) throw new InvalidDataException("Unexpected black market purchase trailer.");
        return result == 0 ? new(tab, slot) : null;
    }
}

// Current offers only: byte-sized tabs and entry counts bound storage to 65,280 offers.
// No packet history, disk cache, or remote stock reporting.
internal sealed class BlackMarketTracker
{
    readonly Dictionary<byte, BlackMarketStock> tabs = new();
    (int Server, string Name)? owner;
    long persistentOwner;
    internal void Clear() { tabs.Clear(); owner = null; persistentOwner = 0; }
    internal void Follow(Actor actor)
    {
        var next = (actor.Server, actor.Name);
        if (owner is { } previous && (previous != next || persistentOwner > 0 && actor.PersistentId > 0 && persistentOwner != actor.PersistentId)) {
            tabs.Clear(); persistentOwner = 0;
        }
        owner = next;
        if (actor.PersistentId > 0) persistentOwner = actor.PersistentId;
    }
    internal void Apply(NetworkEvent message, long now)
    {
        if (message.Kind is not ("black-market" or "black-market-purchase")) return;
        Prune(now);
        if (message.Kind == "black-market") {
            var reading = message.Fields.Deserialize<BlackMarketReading>();
            if (reading == null || reading.Entries == null || reading.Entries.Length > 255) return;
            long refresh;
            try { refresh = checked(reading.RefreshAt - reading.ServerOffset); } catch (OverflowException) { return; }
            if (refresh <= now || refresh > 253402300799999) return;
            if (tabs.TryGetValue(reading.Tab, out var previous) && message.Timestamp < previous.OpenedAt) return;
            tabs[reading.Tab] = new(reading.Tab, refresh, message.Timestamp, reading.Entries.ToArray());
        } else if (message.Kind == "black-market-purchase") {
            var purchase = message.Fields.Deserialize<BlackMarketPurchase>();
            if (purchase == null || !tabs.TryGetValue(purchase.Tab, out var stock) || message.Timestamp < stock.OpenedAt) return;
            tabs[purchase.Tab] = stock with { OpenedAt = message.Timestamp, Entries = stock.Entries.Select(e => e.Slot == purchase.Slot ? e with { SoldOut = true } : e).ToArray() };
        }
    }
    void Prune(long now) { foreach (byte tab in tabs.Where(p => p.Value.RefreshAt <= now).Select(p => p.Key).ToArray()) tabs.Remove(tab); }
    internal BlackMarketStock[] Snapshot(long now)
    {
        Prune(now);
        return tabs.Values.OrderByDescending(s => s.OpenedAt).Select(s => s with { Entries = s.Entries.ToArray() }).ToArray();
    }
}
