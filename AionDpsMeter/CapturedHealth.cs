using System.Text.Json;

namespace AionDpsMeter.Local;

internal sealed record HealthReading(long? Value, long Timestamp, long? Maximum = null)
{
    internal string Label => Value == null ? "Revived (HP not captured)" : Value == 0 ? "Defeated (observed)" : Maximum is > 0 ? $"HP {Value:N0} / {Maximum:N0} (last seen)" : $"HP {Value:N0} (last seen)";
}

internal static class CapturedHealth
{
    internal static Dictionary<int, HealthReading> Build(Fight fight)
    {
        var result = new Dictionary<int, HealthReading>();
        var maxima = new Dictionary<int, (long Value, long Time)>();
        // Network events retain original IDs when combat identities are archived.
        // Without a reassignment timestamp those health samples are ambiguous.
        var reused = fight.Actors.Values.Where(a => a.OriginalEntityId > 0).Select(a => a.OriginalEntityId).ToHashSet();
        static long? Number(JsonElement fields, string key) => fields.ValueKind == JsonValueKind.Object &&
            fields.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long n) ? n : null;
        void Observe(int id, long hp, long timestamp) {
            if (id <= 0 || timestamp < 0 || hp < 0) return;
            if (!result.TryGetValue(id, out var old) || timestamp > old.Timestamp || timestamp == old.Timestamp && hp == 0)
                result[id] = new(hp, timestamp);
        }
        foreach (var e in fight.Network) {
            if (e.Kind != "hp" || Number(e.Fields, "EntityId") is not long id || id <= 0 || id > int.MaxValue ||
                reused.Contains((int)id) || e.Timestamp < 0) continue;
            if (Number(e.Fields, "CurrentHp") is long hp) Observe((int)id, hp, e.Timestamp);
            if (Number(e.Fields, "MaximumHp") is > 0 and var max &&
                (!maxima.TryGetValue((int)id, out var oldMax) || e.Timestamp >= oldMax.Time)) maxima[(int)id] = (max, e.Timestamp);
        }
        foreach (var death in fight.Deaths) Observe(death.EntityId, 0, death.Timestamp);
        foreach (var e in fight.Network.Where(e => e.Kind == "entity-revived")) {
            if (e.Timestamp < 0 || Number(e.Fields, "EntityId") is not long id || id <= 0 || id > int.MaxValue || reused.Contains((int)id)) continue;
            // Revival supplies no HP. A newer health sample wins; tied death
            // remains conservative because ordering within a timestamp is unknown.
            if (!result.TryGetValue((int)id, out var current) || e.Timestamp > current.Timestamp)
                result[(int)id] = new(null, e.Timestamp);
        }
        if (fight.LocalStats is { MaximumHp: > 0 } stats && !reused.Contains(fight.LocalPlayer) &&
            (!maxima.TryGetValue(fight.LocalPlayer, out var currentMax) || stats.Timestamp >= currentMax.Time))
            maxima[fight.LocalPlayer] = (stats.MaximumHp.Value, stats.Timestamp);
        foreach (var pair in maxima)
            if (result.TryGetValue(pair.Key, out var current) && current.Value <= pair.Value.Value)
                result[pair.Key] = current with { Maximum = pair.Value.Value };
        return result;
    }
}
