using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

public sealed class PlayerProfileCache
{
    public sealed record Entry(int Server, string Name, int Cp, string Job);
    public sealed record LocalIdentity(int Server, string Name);
    public sealed record CacheFile(int Version, Entry[] Players, LocalIdentity? LocalPlayer);
    LocalIdentity? localIdentity;
    internal const string CachedSource = "Last known (cached)";
    readonly Dictionary<(int, string), Entry> entries = new();
    readonly object gate = new(), saveGate = new();
    readonly string? path;
    long revision, savedRevision;
    public PlayerProfileCache(string? path = null)
    {
        this.path = path;
        if (path == null || !File.Exists(path)) return;
        try {
            if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException();
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var state = json.RootElement.ValueKind == JsonValueKind.Array
                ? new CacheFile(1, json.RootElement.Deserialize<Entry[]>()!, null)
                : json.RootElement.Deserialize<CacheFile>() ?? throw new InvalidDataException();
            if (state.Version != 1 || state.Players == null || state.LocalPlayer is { } identity &&
                (identity.Server <= 0 || string.IsNullOrWhiteSpace(identity.Name) || identity.Name.Length > 64 || identity.Name.Any(char.IsControl)))
                throw new InvalidDataException();
            var loaded = state.Players;
            if (loaded.Length > 5000 || loaded.Any(e => e == null || e.Server <= 0 || string.IsNullOrWhiteSpace(e.Name) || e.Name.Length > 64 ||
                e.Name.Any(char.IsControl) || e.Cp is < 0 or > 2000000 || e.Job == null || (e.Job != "Unknown" && !ClassEvidence.Known(e.Job))) ||
                loaded.Select(e => (e.Server, e.Name)).Distinct().Count() != loaded.Length) throw new InvalidDataException();
            foreach (var e in loaded) entries[(e.Server, e.Name)] = e;
            localIdentity = state.LocalPlayer;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) {
            LocalLog.Write("Player profile cache could not be loaded; starting with an empty cache.");
        }
    }
    static bool Eligible(Actor a) => a.Player && a.NpcId == 0 && a.OriginalEntityId == 0 && a.Server > 0 &&
        !string.IsNullOrWhiteSpace(a.Name) && !a.Name.StartsWith("Entity ", StringComparison.Ordinal);
    internal void RememberLocal(Actor actor)
    {
        if (!Eligible(actor)) return;
        var next = new LocalIdentity(actor.Server, actor.Name);
        lock (gate) { if (next != localIdentity) { localIdentity = next; revision++; } }
    }
    internal int MatchLocal(Fight fight)
    {
        lock (gate) {
            if (localIdentity == null) return 0;
            var matches = fight.Actors.Values.Where(a => Eligible(a) && a.Server == localIdentity.Server && a.Name == localIdentity.Name).Take(2).ToArray();
            return matches.Length == 1 ? matches[0].Id : 0;
        }
    }
    internal void Update(Fight fight, bool inferClasses = false)
    {
        var inferred = inferClasses ? ClassEvidence.Infer(fight) : new Dictionary<int, string>();
        var actors = fight.Actors.Values.Where(Eligible).ToArray();
        var counts = actors.GroupBy(a => (a.Server, a.Name)).ToDictionary(g => g.Key, g => g.Count());
        lock (gate) foreach (var actor in actors) {
            var key = (actor.Server, actor.Name);
            if (counts[key] != 1) {
                if (actor.CpSource == CachedSource) fight.Actors[actor.Id] = actor with { Cp = 0, CpSource = "Unknown", CachedJob = "Unknown" };
                else if (actor.CachedJob != "Unknown") fight.Actors[actor.Id] = actor with { CachedJob = "Unknown" };
                continue;
            }
            entries.TryGetValue(key, out var old);
            int cp = actor.Cp is > 0 and <= 2000000 && actor.CpSource != CachedSource ? actor.Cp : old?.Cp ?? 0;
            string job = ClassEvidence.Known(actor.Job) ? actor.Job : inferred.GetValueOrDefault(actor.Id, old?.Job ?? "Unknown");
            var next = new Entry(actor.Server, actor.Name, cp, job);
            if ((cp > 0 || job != "Unknown") && next != old) {
                if (entries.Count >= 5000 && old == null) entries.Remove(entries.Keys.First());
                entries[key] = next; revision++;
            }
            fight.Actors[actor.Id] = actor with {
                Cp = actor.Cp > 0 ? actor.Cp : cp,
                CpSource = actor.Cp > 0 || cp == 0 ? actor.CpSource : CachedSource,
                CachedJob = job
            };
        }
    }
    public void Save()
    {
        if (path == null) return;
        lock (saveGate) {
            CacheFile snapshot; long version;
            lock (gate) { if (revision == savedRevision) return; snapshot = new(1, entries.Values.ToArray(), localIdentity); version = revision; }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot)); File.Move(temporary, path, true);
            lock (gate) savedRevision = version;
        }
    }
}
