using System.Collections.Frozen;
using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal sealed record FieldBossReference(int MapId, int Slot, string Map, int NpcId, string Name);
internal sealed record FieldBossReferenceData(string Source, string SourceSha256, FieldBossReference[] Rows);
internal static class FieldBossReferences
{
    internal static readonly FrozenDictionary<(int Map, int Slot), FieldBossReference> Rows = Load();
    static FrozenDictionary<(int, int), FieldBossReference> Load()
    {
        try {
            using var stream = typeof(FieldBossReferences).Assembly.GetManifestResourceStream("Wingbeat.FieldBosses.json")!;
            var data = JsonSerializer.Deserialize<FieldBossReferenceData>(stream)!;
            if (data.Rows.Length > 4096 || data.SourceSha256.Length != 64 || !data.SourceSha256.All(Uri.IsHexDigit) ||
                data.Rows.Any(r => r.MapId is < 1000 or > 99999 || r.Slot is < 1 or > 99 || r.NpcId <= 0 || string.IsNullOrWhiteSpace(r.Name) || string.IsNullOrWhiteSpace(r.Map)))
                throw new InvalidDataException("Invalid field boss catalogue.");
            return data.Rows.ToFrozenDictionary(r => (r.MapId, r.Slot));
        } catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or NullReferenceException) {
            return new Dictionary<(int, int), FieldBossReference>().ToFrozenDictionary();
        }
    }
}
internal sealed record FieldBossTimerRow(int MapId, int Slot, string Map, string Boss, string Status, string ReportedTime, string Remaining, string Observed);
internal sealed class FieldBossTracker
{
    readonly Dictionary<int, (long Seen, FieldBossList List)> maps = new();
    internal int MapCount => maps.Count;
    internal void Clear() => maps.Clear();
    internal void Apply(NetworkEvent item)
    {
        if (item.Kind != "field-boss-list" || item.Timestamp < 0) return;
        var list = item.Fields.Deserialize<FieldBossList>();
        if (list == null || list.MapId is < 1000 or > 99999 || list.Slots == null || list.Slots.Length > 99 ||
            list.Slots.Any(s => s == null || s.Slot is < 1 or > 99 || s.TimeMs != 0 && s.TimeMs is < 1600000000000 or > 2600000000000) ||
            list.Slots.Select(s => s.Slot).Distinct().Count() != list.Slots.Length) return;
        if (maps.TryGetValue(list.MapId, out var old) && old.Seen > item.Timestamp) return;
        if (!maps.ContainsKey(list.MapId) && maps.Count >= 32) {
            var oldest = maps.MinBy(p => p.Value.Seen);
            if (oldest.Value.Seen > item.Timestamp) return;
            maps.Remove(oldest.Key);
        }
        maps[list.MapId] = (item.Timestamp, list);
    }
    internal FieldBossTimerRow[] Rows(long now) => maps.OrderBy(p => p.Key).SelectMany(map => map.Value.List.Slots.OrderBy(s => s.Slot).Select(slot => {
        var reference = FieldBossReferences.Rows.GetValueOrDefault((map.Key, slot.Slot));
        string status = slot.Alive ? "Reported alive" : slot.TimeMs == 0 ? "Respawn unknown" : slot.TimeMs > now ? "Reported respawn" : "Timer elapsed; refresh in game";
        string time = slot.TimeMs == 0 ? "Unknown" : DateTimeOffset.FromUnixTimeMilliseconds(slot.TimeMs).ToLocalTime().ToString("ddd HH:mm:ss");
        string remaining = !slot.Alive && slot.TimeMs > now ? TimeSpan.FromMilliseconds(slot.TimeMs - now).ToString(@"d\.hh\:mm\:ss") : "—";
        string seen = map.Value.Seen > now ? "Clock mismatch" : TimeSpan.FromMilliseconds(now - map.Value.Seen).ToString(@"d\.hh\:mm\:ss") + " ago";
        return new FieldBossTimerRow(map.Key, slot.Slot, reference?.Map ?? $"Map {map.Key}", reference?.Name ?? $"Slot {slot.Slot}", status, time, remaining, seen);
    })).ToArray();
}
