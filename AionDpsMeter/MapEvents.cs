using System.IO;
using System.Text;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal sealed record MapEventReading(int EventId, string? Name, byte Status, int Stage, int Step);
internal sealed record MapEventProgress(int EventId, string Name, int Stage, int Step, bool Done, long Timestamp)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string Completion => Done ? "Completed" : "In progress";
}
internal static class MapEventWire
{
    internal static MapEventReading Read(ref WireReader r, bool joined)
    {
        int id = checked((int)r.U32()); string? name = null;
        if (joined) name = new UTF8Encoding(false, true).GetString(r.Take(Count(ref r))).TrimEnd('\0');
        byte status = r.Byte(); int stage = r.Var(), step = r.Var();
        for (int i = Count(ref r); i > 0; i--) r.Var();
        if (joined) r.Take(8); r.Take(Count(ref r) * 2);
        if (id <= 0 || r.Remaining != 0) throw new InvalidDataException("Invalid map event.");
        return new(id, name, status, stage, step);
    }
    static int Count(ref WireReader r) { int count = r.Var(); if (count > 4096) throw new InvalidDataException("Map event list exceeds limit."); return count; }
}
internal sealed class MapEventTracker
{
    readonly Dictionary<int, (long Time, MapEventProgress? State)> entries = new();
    long boundary = -1;
    internal void ResetSession(long timestamp)
    {
        if (timestamp < 0 || timestamp <= boundary) return;
        boundary = timestamp; entries.Clear();
    }
    internal MapEventProgress[] Snapshot() => entries.Values.Where(e => e.State != null).Select(e => e.State!).OrderBy(e => e.EventId).ToArray();
    internal MapEventProgress? Apply(NetworkEvent message)
    {
        if (message.Timestamp < 0 || message.Timestamp < boundary) return null;
        if (message.Kind == "local-zone") { if (message.Timestamp > boundary) { entries.Clear(); boundary = message.Timestamp; } return null; }
        if (message.Kind is not ("map-event-join" or "map-event-progress" or "map-event-leave")) return null;
        int id = message.Fields.GetProperty("EventId").GetInt32();
        entries.TryGetValue(id, out var old);
        if (old.Time > message.Timestamp) return null;
        if (message.Kind == "map-event-progress" && old.State == null) return null;
        if (!entries.ContainsKey(id) && entries.Count >= 128) {
            var oldest = entries.MinBy(e => e.Value.Time);
            if (message.Timestamp < oldest.Value.Time) return null;
            entries.Remove(oldest.Key);
        }
        if (message.Kind == "map-event-leave") { entries[id] = (message.Timestamp, null); return null; }
        var reading = message.Fields.Deserialize<MapEventReading>()!;
        var next = new MapEventProgress(id, reading.Name ?? old.State?.Name ?? "", reading.Stage, reading.Step, reading.Status == 3, message.Timestamp);
        entries[id] = (message.Timestamp, next);
        return message.Kind == "map-event-progress" && old.State is { } previous &&
            (previous.Stage != next.Stage || previous.Step != next.Step || previous.Done != next.Done) ? next : null;
    }
}
