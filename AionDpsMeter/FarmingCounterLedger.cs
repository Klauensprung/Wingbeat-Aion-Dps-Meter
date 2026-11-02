using AionDpsMeter.Models;
using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal sealed record FarmingCounterSnapshot(long Since, Dictionary<int, long> Counts, long Omitted);

// Independent local tracking totals: neither DPS reset nor archive deletion
// changes them. Confirmed gathers and kills use separate instances/files.
public sealed class FarmingCounterLedger
{
    readonly object gate = new(), saveGate = new();
    readonly string? path;
    Dictionary<int, long> counts = new();
    long since = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), omitted, revision, savedRevision;
    internal string? LoadError { get; }
    public FarmingCounterLedger(string? path = null)
    {
        this.path = path;
        if (path == null || !File.Exists(path)) return;
        try {
            if (new FileInfo(path).Length > 256 * 1024) throw new InvalidDataException();
            var value = JsonSerializer.Deserialize<FarmingCounterSnapshot>(File.ReadAllText(path));
            if (value == null || value.Since is < 0 or > 253402300799999 || value.Omitted < 0 ||
                value.Counts is not { Count: <= 4096 } || value.Counts.Any(p => p.Key <= 0 || p.Value <= 0)) throw new InvalidDataException();
            counts = value.Counts; since = value.Since; omitted = value.Omitted;
        } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException) {
            LoadError = "Saved farming tracking could not be loaded. The original file was retained.";
            LocalLog.Write(LoadError);
        }
    }
    internal void Apply(WorldObjectTracker objects, NetworkEvent message)
    {
        if (message.Kind != "gathered" || message.Timestamp < 0) return;
        int entity = message.Fields.GetProperty("EntityId").GetInt32();
        if (objects.Find(entity, message.Timestamp) is not { ObjectId: > 0 } item) return;
        Record(item.ObjectId);
    }
    internal void Record(int id, long amount = 1)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        lock (gate) {
            long old = counts.GetValueOrDefault(id);
            long accepted = !counts.ContainsKey(id) && counts.Count >= 4096 ? 0 : Math.Min(amount, long.MaxValue - old);
            if (accepted > 0) counts[id] = old + accepted;
            omitted += Math.Min(long.MaxValue - omitted, amount - accepted);
            revision++;
        }
    }
    internal FarmingCounterSnapshot Snapshot() { lock (gate) return new(since, new(counts), omitted); }
    internal void Reset(long now)
    {
        if (now is < 0 or > 253402300799999) throw new ArgumentOutOfRangeException(nameof(now));
        lock (gate) { counts.Clear(); omitted = 0; since = now; revision++; }
    }
    internal void Save()
    {
        if (path == null) return;
        lock (saveGate) {
            FarmingCounterSnapshot snapshot; long version;
            lock (gate) { if (revision == savedRevision) return; snapshot = new(since, new(counts), omitted); version = revision; }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temporary = path + ".tmp";
            try { File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot)); File.Move(temporary, path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            lock (gate) savedRevision = version;
        }
    }
}
