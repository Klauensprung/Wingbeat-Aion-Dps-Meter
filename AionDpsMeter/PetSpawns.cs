using System.Collections.Frozen;
using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal sealed record PetSpawnPoint(int NpcId, double X, double Y, double Z);
internal sealed record PetSpawnSite(int PetId, string MapKey, string Map, PetSpawnPoint[] Spawns);
internal sealed record PetSpawnData(string Source, Dictionary<string, string> Hashes, int ExcludedUnnamedAreas, PetSpawnSite[] Sites);
internal sealed class PetSpawns
{
    static readonly Lazy<PetSpawns> instance = new(() => new());
    internal static PetSpawns Default => instance.Value;
    readonly FrozenDictionary<int, PetSpawnSite[]> sites;
    internal int Count { get; }
    internal int ExcludedUnnamedAreas { get; }
    internal string? LoadError { get; }
    internal IReadOnlyList<PetSpawnSite> ForPet(int id) => sites.GetValueOrDefault(id) ?? [];
    PetSpawns()
    {
        try {
            using var stream = typeof(PetSpawns).Assembly.GetManifestResourceStream("Wingbeat.PetSpawns.json") ?? throw new InvalidDataException("Pet spawns missing.");
            var data = JsonSerializer.Deserialize<PetSpawnData>(stream) ?? throw new InvalidDataException("Empty pet spawns.");
            Validate(data);
            sites = data.Sites.GroupBy(s => s.PetId).ToFrozenDictionary(g => g.Key, g => g.OrderBy(s => s.Map).ThenBy(s => s.MapKey).ToArray());
            Count = data.Sites.Sum(s => s.Spawns.Length); ExcludedUnnamedAreas = data.ExcludedUnnamedAreas;
        } catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentException) {
            sites = FrozenDictionary<int, PetSpawnSite[]>.Empty; LoadError = "Pet spawn reference unavailable.";
        }
    }
    internal static void Validate(PetSpawnData data)
    {
        if (data.Hashes == null || data.Hashes.Count != 4 || data.Hashes.Values.Any(h => h?.Length != 64 || !h.All(Uri.IsHexDigit)) ||
            data.ExcludedUnnamedAreas < 0 || data.Sites is not { Length: > 0 and <= 10000 } ||
            data.Sites.Any(s => s == null || s.PetId is < 1000 or > 99999 || string.IsNullOrWhiteSpace(s.MapKey) || s.MapKey.Length > 300 ||
                string.IsNullOrWhiteSpace(s.Map) || s.Map.Length > 300 || s.Spawns is not { Length: > 0 and <= 10000 } ||
                s.Spawns.Any(p => p == null || p.NpcId <= 0 || !Valid(p.X) || !Valid(p.Y) || !Valid(p.Z))) ||
            data.Sites.Sum(s => (long)s.Spawns.Length) > 100000 || data.Sites.Select(s => (s.PetId, s.MapKey)).Distinct().Count() != data.Sites.Length)
            throw new InvalidDataException("Invalid pet spawn reference.");
        static bool Valid(double value) => double.IsFinite(value) && Math.Abs(value) <= 10000000;
    }
}
