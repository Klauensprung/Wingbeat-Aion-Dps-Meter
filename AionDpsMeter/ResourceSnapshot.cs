using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

public sealed record ResourceSnapshot(long Timestamp, long? Mp = null, long? MaximumMp = null,
    long? Sp = null, long? MaximumSp = null, long? Fp = null, long? MaximumFp = null);
internal static class ResourceSnapshots
{
    internal static void Validate(ResourceSnapshot value) {
        if (value.Timestamp < 0 || new[] { value.Mp, value.MaximumMp, value.Sp, value.MaximumSp, value.Fp, value.MaximumFp }.Any(v => v < 0))
            throw new InvalidDataException("Invalid captured resources.");
    }
    internal static ResourceSnapshot? Apply(ResourceSnapshot? old, NetworkEvent e, int local)
    {
        if (e.Kind != "resources" || local <= 0 || e.Timestamp < 0 || e.Timestamp < old?.Timestamp ||
            e.Fields.ValueKind != JsonValueKind.Object || !e.Fields.TryGetProperty("EntityId", out var actor) ||
            actor.ValueKind != JsonValueKind.Number || !actor.TryGetInt32(out int id) || id != local ||
            !e.Fields.TryGetProperty("Values", out var data)) return old;
        var values = data.Deserialize<Dictionary<byte, long>>();
        if (values == null || values.Count > 6 || values.Any(p => p.Key is not (1 or 3 or 6 or 8 or 10 or 13) || p.Value < 0)) return old;
        long? Get(byte key, long? previous) => values.TryGetValue(key, out long value) ? value : previous;
        return new(e.Timestamp, Get(1, old?.Mp), Get(8, old?.MaximumMp), Get(3, old?.Sp), Get(10, old?.MaximumSp), Get(6, old?.Fp), Get(13, old?.MaximumFp));
    }
    internal static IEnumerable<PlayerStatRow> Rows(ResourceSnapshot? value)
    {
        if (value == null) yield break;
        foreach (var pair in new[] { ("MP", value.Mp), ("Maximum MP", value.MaximumMp), ("SP", value.Sp),
            ("Maximum SP", value.MaximumSp), ("FP", value.Fp), ("Maximum FP", value.MaximumFp) })
            if (pair.Item2 is long amount) yield return new(null, pair.Item1 + " (last seen)", amount.ToString("N0"), amount);
    }
}
