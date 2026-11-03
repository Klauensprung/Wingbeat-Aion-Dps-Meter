using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal sealed class FieldBossTimerCache
{
    internal sealed record Entry(int Server, FieldBossObservation Observation, Dictionary<int, long> Notified);
    internal sealed record State(int Version, int LastServer, Entry[] Entries);
    readonly object gate = new();
    readonly Dictionary<(int Server, int Map), Entry> entries = new();
    readonly string? path;
    bool dirty;
    int lastServer;
    internal int LastServer { get { lock (gate) return lastServer; } }
    internal FieldBossTimerCache(string? path = null)
    {
        this.path = path;
        if (path == null || !File.Exists(path)) return;
        try {
            if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException();
            var state = JsonSerializer.Deserialize<State>(File.ReadAllText(path));
            if (state is not { Version: 1, Entries.Length: <= 64 }) throw new InvalidDataException();
            foreach (var entry in state.Entries) {
                if (entry == null || entry.Server <= 0 || entry.Observation == null || entry.Observation.Seen is < 1600000000000 or > 2600000000000 || entry.Notified == null || entry.Notified.Count > 99)
                    throw new InvalidDataException();
                var validator = new FieldBossTracker();
                validator.Apply(Event(entry.Observation));
                if (validator.MapCount != 1) throw new InvalidDataException();
                entries[(entry.Server, entry.Observation.List.MapId)] = entry;
            }
            lastServer = entries.Keys.Any(k => k.Server == state.LastServer) ? state.LastServer : 0;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NullReferenceException) {
            entries.Clear(); lastServer = 0; LocalLog.Write("Boss timer cache unavailable: " + ex.Message);
        }
    }
    static NetworkEvent Event(FieldBossObservation observation) => new("field-boss-list", observation.Seen, "cached",
        JsonSerializer.SerializeToElement(observation.List));
    internal void Capture(int server, FieldBossObservation[] observations)
    {
        if (server <= 0) return;
        lock (gate) foreach (var observation in observations) {
            var key = (server, observation.List.MapId);
            if (entries.TryGetValue(key, out var old) && old.Observation.Seen >= observation.Seen) continue;
            var notified = old?.Notified.Where(p => observation.List.Slots.Any(s => s.Slot == p.Key && s.TimeMs == p.Value))
                .ToDictionary(p => p.Key, p => p.Value) ?? new();
            entries[key] = new(server, observation, notified);
            lastServer = server; dirty = true;
            while (entries.Count > 64) entries.Remove(entries.MinBy(p => p.Value.Observation.Seen).Key);
        }
    }
    internal FieldBossTimerRow[] Rows(int server, long now)
    {
        lock (gate) {
            var tracker = new FieldBossTracker();
            foreach (var entry in entries.Values.Where(e => e.Server == server)) tracker.Apply(Event(entry.Observation));
            return tracker.Rows(now);
        }
    }
    internal static string SelectionKey(int server, int map, int slot) => $"{server}:{map}:{slot}";
    internal string[] Poll(int server, long now, int leadSeconds, bool enabled, IReadOnlySet<string>? disabled = null)
    {
        if (!enabled || server <= 0) return [];
        lock (gate) {
            var messages = new List<string>();
            foreach (var entry in entries.Values.Where(e => e.Server == server)) {
                // Do not replay old notifications on restart or invent a next-day respawn.
                if (entry.Observation.Seen > now || now - entry.Observation.Seen > 7L * 24 * 60 * 60 * 1000) continue;
                foreach (var slot in entry.Observation.List.Slots) {
                    if (disabled?.Contains(SelectionKey(server, entry.Observation.List.MapId, slot.Slot)) == true ||
                        disabled?.Contains(SelectionKey(0, entry.Observation.List.MapId, slot.Slot)) == true || slot.Alive || slot.TimeMs <= now || slot.TimeMs - now > Math.Clamp(leadSeconds, 0, 3600) * 1000L ||
                        entry.Notified.GetValueOrDefault(slot.Slot) == slot.TimeMs) continue;
                    entry.Notified[slot.Slot] = slot.TimeMs; dirty = true;
                    var reference = FieldBossReferences.Rows.GetValueOrDefault((entry.Observation.List.MapId, slot.Slot));
                    messages.Add($"{reference?.Name ?? $"Boss slot {slot.Slot}"} · {reference?.Map ?? $"Map {entry.Observation.List.MapId}"} · server {server}: reported respawn {DateTimeOffset.FromUnixTimeMilliseconds(slot.TimeMs).ToLocalTime():HH:mm}. Refresh in game to confirm.");
                }
            }
            return messages.ToArray();
        }
    }
    internal void Save()
    {
        if (path == null) return;
        lock (gate) {
            if (!dirty) return;
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new State(1, lastServer, entries.Values.ToArray())));
                File.Move(path + ".tmp", path, true); dirty = false;
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                LocalLog.Write("Could not save boss timers: " + ex.Message);
            }
        }
    }
}
