using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class EffectDuration
{
    // Legacy archives used UInt32.MaxValue for an unknown duration. New wire
    // events retain the signed raw duration, so that positive value is finite.
    internal static bool TryRead(JsonElement fields, out long? milliseconds, out bool untilRemoved)
    {
        milliseconds = null; untilRemoved = false;
        if (fields.ValueKind != JsonValueKind.Object || !fields.TryGetProperty("DurationMs", out var value) ||
            value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long duration) || duration < 0) return false;
        if (fields.TryGetProperty("RawDurationMs", out var raw) && raw.ValueKind == JsonValueKind.Number && raw.TryGetInt64(out long wire)) {
            untilRemoved = wire <= 0;
            milliseconds = untilRemoved ? null : duration;
        } else {
            untilRemoved = fields.TryGetProperty("UntilRemoved", out var persistent) && persistent.ValueKind == JsonValueKind.True;
            milliseconds = untilRemoved || duration == uint.MaxValue ? null : duration;
        }
        return true;
    }
    internal static long Add(long time, long duration) => time > long.MaxValue - duration ? long.MaxValue : time + duration;
    internal static long? Deadline(long? duration, long timestamp, long utcNow, long monotonicNow) => duration is long ms
        ? Add(monotonicNow, Math.Max(0, ms - Math.Max(0, utcNow - timestamp))) : null;
}
