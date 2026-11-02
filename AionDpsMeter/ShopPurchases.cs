using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal sealed record ShopPurchase(int Goods, int Count, long? ResetAt);
internal sealed record ShopPurchaseBatch(bool Added, ShopPurchase[] Purchases);
internal static class ShopPurchaseWire
{
    internal static (ushort Result, ShopPurchaseBatch Batch) Read(ref WireReader r, ushort opcode)
    {
        ushort result = opcode is 0x8e56 or 0x018c ? r.U16() : (ushort)0;
        if (opcode == 0x018c) r.Var();
        if (opcode == 0x038c) r.Take(6);
        int count = opcode == 0x8f56 ? 1 : r.Var();
        if (count > 4096) throw new InvalidDataException("Shop purchase list exceeds limit.");
        var rows = new ShopPurchase[count];
        for (int i = 0; i < count; i++) {
            int goods = checked((int)r.U32()), quantity = checked((int)r.U32());
            long? reset = opcode is 0x8e56 or 0x8f56 ? checked((long)r.U64()) : null;
            if (goods <= 0 || reset > 253402300799999) throw new InvalidDataException("Invalid shop purchase counter.");
            rows[i] = new(goods, quantity, reset);
        }
        if (opcode == 0x018c) r.Take(8);
        if (r.Remaining != 0) throw new InvalidDataException("Unknown shop purchase trailer.");
        return (result, new(opcode == 0x038c, rows));
    }
}
internal sealed record ShopPurchaseProgress(int Server, int Goods, long Count, long? ResetAt, bool BaselineKnown, long Timestamp, string Character = "", bool InferredReset = false, long Sequence = 0);
public sealed class ShopPurchaseTracker
{
    readonly Dictionary<(int Server, string Character, int Goods), ShopPurchaseProgress> entries = new();
    readonly object gate = new(), saveGate = new();
    readonly string? path;
    long revision, savedRevision, sequence;
    public ShopPurchaseTracker(string? path = null)
    {
        this.path = path;
        if (path == null || !File.Exists(path)) return;
        try {
            if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException();
            var rows = JsonSerializer.Deserialize<ShopPurchaseProgress[]>(File.ReadAllText(path)) ?? throw new InvalidDataException();
            if (rows.Length > 1024 || rows.Any(r => r == null || !Valid(r) || r.ResetAt == null) ||
                rows.Select(r => (r.Server, r.Character, r.Goods)).Distinct().Count() != rows.Length) throw new InvalidDataException();
            foreach (var row in rows) entries.Add((row.Server, row.Character, row.Goods), row);
            sequence = rows.Select(r => r.Sequence).DefaultIfEmpty().Max();
        } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException) {
            LocalLog.Write("Saved shop counters could not be loaded; original file retained.");
        }
    }
    static bool Valid(ShopPurchaseProgress row) => row.Server > 0 && row.Goods > 0 && row.Count >= 0 && row.Sequence >= 0 && row.Timestamp is >= 0 and <= 253402300799999 &&
        row.Character != null && row.Character.Length <= 128 && row.ResetAt is not > 253402300799999 && !(row.ResetAt <= row.Timestamp) &&
        (ChecklistTasks.ForGoods(row.Goods) is { PerCharacter: false } ? row.Character == "" : !string.IsNullOrWhiteSpace(row.Character));
    internal void Apply(NetworkEvent message, Actor? owner)
    {
        if (message.Kind != "shop-purchases" || message.Timestamp < 0 || owner is not { Player: true, Server: > 0 }) return;
        var batch = message.Fields.Deserialize<ShopPurchaseBatch>(); if (batch == null) return;
        lock (gate) foreach (var item in batch.Purchases) {
            var task = ChecklistTasks.ForGoods(item.Goods);
            long? reset = item.ResetAt ?? (task != null ? ChecklistTasks.WeeklyReset(owner.Server, message.Timestamp) : null);
            if (item.Goods <= 0 || item.Count < 0 || reset <= message.Timestamp) continue;
            // Unmapped goods have no verified server-wide scope. Keep owners separate.
            string character = task is { PerCharacter: false } ? "" : owner.Name;
            var key = (owner.Server, character, item.Goods); entries.TryGetValue(key, out var previous);
            if (previous != null && message.Timestamp < previous.Timestamp) continue;
            bool samePeriod = previous != null && previous.ResetAt == reset;
            if (batch.Added && samePeriod && previous!.Count > long.MaxValue - item.Count) continue;
            long count = batch.Added && samePeriod ? previous!.Count + item.Count : item.Count;
            if (sequence == long.MaxValue) {
                sequence = 0;
                foreach (var entry in entries.OrderBy(e => e.Value.Sequence).ToArray()) entries[entry.Key] = entry.Value with { Sequence = ++sequence };
            }
            var next = new ShopPurchaseProgress(owner.Server, item.Goods, count, reset, !batch.Added || samePeriod && previous!.BaselineKnown, message.Timestamp, character, item.ResetAt == null && reset != null, ++sequence);
            if (!Valid(next) || next == previous) continue;
            if (previous == null && entries.Count >= 1024) entries.Remove(entries.MinBy(e => e.Value.Timestamp).Key);
            entries[key] = next; revision++;
        }
    }
    internal ShopPurchaseProgress[] Snapshot() { lock (gate) return entries.Values.OrderBy(r => r.Server).ThenBy(r => r.Character).ThenBy(r => r.Goods).ToArray(); }
    internal void Save()
    {
        if (path == null) return;
        lock (saveGate) {
            ShopPurchaseProgress[] rows; long version;
            lock (gate) { if (revision == savedRevision) return; rows = entries.Values.Where(r => r.ResetAt != null).ToArray(); version = revision; }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temporary = path + ".tmp"; File.WriteAllText(temporary, JsonSerializer.Serialize(rows)); File.Move(temporary, path, true);
            lock (gate) savedRevision = version;
        }
    }
}
