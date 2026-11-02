using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

public sealed record ObservedRun(int MapId, long ServerTimestampMs, int Variant, ulong Reference, int State, long ObservedAt);
internal sealed class ObservedRunTracker
{
    long latest = -1;
    internal ObservedRun? Current { get; private set; }
    internal void Clear() { Current = null; latest = -1; }
    internal static bool Same(ObservedRun? left, ObservedRun? right) => left == null || right == null ? left == right :
        left.MapId == right.MapId && left.ServerTimestampMs == right.ServerTimestampMs && left.Variant == right.Variant && left.Reference == right.Reference;
    internal static void Validate(ObservedRun run) {
        if (run.MapId <= 0 || run.ServerTimestampMs is < 1577836800000L or > 4102444800000L || run.Variant is not (1 or 2) ||
            run.State is not (2 or 3) || run.ObservedAt < 0) throw new InvalidDataException("Invalid observed run metadata.");
    }
    internal void Apply(NetworkEvent message)
    {
        if (message.Timestamp < 0 || message.Timestamp < latest || message.Fields.ValueKind != JsonValueKind.Object) return;
        if (message.Kind == "local-zone") {
            if (!message.Fields.TryGetProperty("MapId", out var map) || map.ValueKind != JsonValueKind.Number || !map.TryGetInt32(out int id) || id <= 0) return;
            // Any teleport can change the instance, even when the zone ID repeats.
            latest = message.Timestamp; Current = null; return;
        }
        if (message.Kind != "zone-state") return;
        ZoneStateReading? reading;
        try { reading = message.Fields.Deserialize<ZoneStateReading>(); } catch (JsonException) { return; }
        if (reading is not { MapId: > 0, State: >= 1 and <= 3 }) return;
        latest = message.Timestamp;
        if (reading is not { State: 2 or 3, Variant: 1 or 2, ServerTimestampMs: >= 1577836800000L and <= 4102444800000L, Reference: not null }) { Current = null; return; }
        var next = new ObservedRun(reading.MapId, reading.ServerTimestampMs.Value, reading.Variant.Value, reading.Reference.Value, reading.State, message.Timestamp);
        // Re-entering a phase cannot rewind a later state on the same timestamp.
        if (Same(Current, next) && Current!.State >= next.State) return;
        Current = next;
    }
}
