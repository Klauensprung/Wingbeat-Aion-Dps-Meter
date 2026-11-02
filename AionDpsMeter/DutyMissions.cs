using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal sealed record DutyMissionReading(int Rewarded, long ResetAt);
public sealed record DutyMissionProgress(int Server, int Rewarded, long ResetAt, long Timestamp, long Sequence = 0);

internal static class DutyMissionWire
{
    internal static DutyMissionReading Read(ref WireReader r)
    {
        r.Take(4); int rewarded = checked((int)r.U32());
        int rewards = 0;
        for (int n = Count(ref r); n > 0; n--) {
            r.Take(17);
            int count = Count(ref r); rewards = checked(rewards + count);
            if (rewards > 16384) throw new InvalidDataException("Duty mission rewards exceed limit.");
            for (int j = count; j > 0; j--) { r.Take(4); r.Var(); r.Take(9); }
        }
        long reset = checked((long)r.U64()); byte flags = r.Byte();
        if ((flags & ~1) != 0) throw new InvalidDataException("Unknown duty mission flags.");
        if ((flags & 1) != 0) r.Take(4);
        r.Take(8);
        if (reset > 253402300799999 || r.Remaining != 0) throw new InvalidDataException("Invalid duty mission reset or trailer.");
        return new(rewarded, reset);
    }
    static int Count(ref WireReader r) { int n = r.Var(); if (n > 4096) throw new InvalidDataException("Duty mission list exceeds limit."); return n; }
}

// One current reading per server, independent of fight reset/deletion. No packet history.
public sealed class DutyMissionLedger
{
    readonly Dictionary<int, DutyMissionProgress> entries = new();
    readonly object gate = new(), saveGate = new();
    readonly string? path;
    long revision, savedRevision, sequence;
    public DutyMissionLedger(string? path = null)
    {
        this.path = path;
        if (path == null || !File.Exists(path)) return;
        try {
            if (new FileInfo(path).Length > 65536) throw new InvalidDataException();
            var rows = JsonSerializer.Deserialize<DutyMissionProgress[]>(File.ReadAllText(path)) ?? throw new InvalidDataException();
            if (rows.Length > 128 || rows.Any(r => r == null || r.Server <= 0 || r.Rewarded < 0 || r.Timestamp < 0 || r.Sequence < 0 || r.ResetAt <= r.Timestamp || r.ResetAt > 253402300799999) || rows.Select(r => r.Server).Distinct().Count() != rows.Length)
                throw new InvalidDataException();
            foreach (var row in rows) entries.Add(row.Server, row);
            sequence = rows.Select(r => r.Sequence).DefaultIfEmpty().Max();
        } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException) {
            LocalLog.Write("Duty mission counters could not be loaded; original file retained.");
        }
    }
    internal void Apply(int server, DutyMissionReading reading, long timestamp)
    {
        if (server <= 0 || timestamp < 0 || reading.Rewarded < 0 || reading.ResetAt <= timestamp || reading.ResetAt > 253402300799999) return;
        lock (gate) {
            if (entries.TryGetValue(server, out var old) && timestamp < old.Timestamp) return;
            if (sequence == long.MaxValue) { sequence = 0; foreach (var entry in entries.OrderBy(e => e.Value.Sequence).ToArray()) entries[entry.Key] = entry.Value with { Sequence = ++sequence }; }
            var next = new DutyMissionProgress(server, reading.Rewarded, reading.ResetAt, timestamp, ++sequence);
            if (old == next) return;
            if (!entries.ContainsKey(server) && entries.Count >= 128) entries.Remove(entries.MinBy(e => e.Value.Timestamp).Key);
            entries[server] = next; revision++;
        }
    }
    internal DutyMissionProgress[] Snapshot() { lock (gate) return entries.Values.OrderBy(r => r.Server).ToArray(); }
    internal void Save()
    {
        if (path == null) return;
        lock (saveGate) {
            DutyMissionProgress[] rows; long version;
            lock (gate) { if (revision == savedRevision) return; rows = entries.Values.ToArray(); version = revision; }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(rows)); File.Move(temporary, path, true);
            lock (gate) savedRevision = version;
        }
    }
}
