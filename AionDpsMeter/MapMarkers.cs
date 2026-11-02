using System.IO;
using System.IO.Compression;
using System.Text.Json;
namespace AionDpsMeter.Local;

internal sealed record MapMarkerGroup(int Id, int World, string Name, string Type, string Category, int Count, string DatabaseType = "", int DatabaseId = 0, int ObjectId = 0);
internal sealed record MapMarkerIndex(string Source, string SourceSha256, MapMarkerGroup[] Groups);
internal readonly record struct MapMarkerPoint(int Group, double X, double Y);
internal sealed class MapMarkers
{
    static readonly Lazy<MapMarkers> instance = new(() => new());
    internal static MapMarkers Default => instance.Value;
    internal MapMarkerGroup[] Groups { get; }
    internal string? LoadError { get; }
    static ZipArchive Open() => new(typeof(MapMarkers).Assembly.GetManifestResourceStream("Wingbeat.MapMarkers.zip") ?? throw new InvalidDataException("Marker catalogue missing."), ZipArchiveMode.Read);
    MapMarkers() {
        try {
            using var archive = Open(); var entry = archive.GetEntry("index.json") ?? throw new InvalidDataException();
            if (entry.Length > 4 * 1024 * 1024) throw new InvalidDataException();
            using var stream = entry.Open(); var data = JsonSerializer.Deserialize<MapMarkerIndex>(stream) ?? throw new InvalidDataException();
            if (data.SourceSha256?.Length != 64 || !data.SourceSha256.All(Uri.IsHexDigit) || data.Groups is not { Length: > 0 and <= 20000 } ||
                data.Groups.Where((g,i) => g == null || g.Id != i || g.World <= 0 || g.Count is <= 0 or > 200000 ||
                    g.DatabaseType is not ("" or "npc" or "gatherable" or "achievement" or "dungeon" or "quest") || g.DatabaseId < 0 || g.ObjectId < 0 ||
                    (g.DatabaseType.Length > 0) != (g.DatabaseId > 0) || (g.DatabaseType == "gatherable") != (g.ObjectId > 0) ||
                    new[] { g.Name, g.Type, g.Category }.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 300)).Any() ||
                data.Groups.Sum(g => (long)g.Count) > 200000) throw new InvalidDataException();
            Groups = data.Groups;
        } catch (Exception ex) when (ex is IOException or JsonException or ArgumentException) { Groups = []; LoadError = "Map marker catalogue unavailable."; }
    }
    // Only the caller's current world is retained; changing worlds drops the old array.
    internal MapMarkerPoint[] LoadWorld(int world) {
        int count = Groups.Where(g => g.World == world).Sum(g => g.Count);
        if (count == 0) return [];
        using var archive = Open(); var entry = archive.GetEntry($"{world}.bin") ?? throw new InvalidDataException("Map coordinates missing.");
        if (entry.Length != count * 20L) throw new InvalidDataException("Invalid map coordinate count.");
        using var reader = new BinaryReader(entry.Open()); var result = new MapMarkerPoint[count]; var counts = new Dictionary<int,int>();
        for (int i=0; i<count; i++) {
            int group = reader.ReadInt32(); double x=reader.ReadDouble(), y=reader.ReadDouble();
            if (group < 0 || group >= Groups.Length || Groups[group].World != world || !double.IsFinite(x) || !double.IsFinite(y) ||
                Math.Abs(x)>10000000 || Math.Abs(y)>10000000) throw new InvalidDataException("Invalid map coordinate.");
            counts[group]=counts.GetValueOrDefault(group)+1; result[i]=new(group,x,y);
        }
        if (Groups.Where(g=>g.World==world).Any(g=>counts.GetValueOrDefault(g.Id)!=g.Count)) throw new InvalidDataException("Invalid map groups.");
        return result;
    }
    internal static MapMarkerGroup[] Visible(IEnumerable<MapMarkerGroup> groups, IReadOnlySet<string> pins, string query, string? category, IReadOnlySet<int>? searchGroups = null, Func<MapMarkerGroup,bool>? categoryFilter = null) {
        if (searchGroups != null) return groups.Where(g=>pins.Contains(g.Name) || searchGroups.Contains(g.Id)).ToArray();
        var rows=groups.ToArray(); query=query.Trim();
        var matches=query.Length==0 ? [] : rows.Where(g=>g.Name.Contains(query,StringComparison.OrdinalIgnoreCase) || g.Category.Contains(query,StringComparison.OrdinalIgnoreCase)).ToArray();
        // Name pins override filtering; an unsuccessful search falls back to categories.
        var names=matches.Select(g=>g.Name).ToHashSet(StringComparer.Ordinal);
        return rows.Where(g=>pins.Contains(g.Name) || (matches.Length>0 ? names.Contains(g.Name) : categoryFilter?.Invoke(g) ?? (category==null || g.Category==category))).ToArray();
    }
}
