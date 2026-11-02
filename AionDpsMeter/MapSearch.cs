using System.Globalization;
using System.IO;
using System.Text.Json;
namespace AionDpsMeter.Local;

internal sealed record MapSearchGroup(int World, string Name, string WorldName, int Count, int[] GroupIds)
{
    public string Display => $"{Name} - {WorldName} ({Count:N0})";
}
internal sealed record MapSearchResult(MapSearchGroup[] Groups, int TotalGroups, bool IsLimited);
internal sealed class MapSearch
{
    sealed record Entry(MapSearchGroup Group, string[] SearchText);
    readonly Entry[] entries;
    static readonly StringComparer order=StringComparer.Create(CultureInfo.GetCultureInfo("en-US"),false);
    internal MapSearch(IEnumerable<MapMarkerGroup> groups,IReadOnlyDictionary<int,string> worlds) {
        entries=groups.Where(g=>worlds.ContainsKey(g.World)).GroupBy(g=>(g.World,g.Name)).Select(g=>new Entry(
            new(g.Key.World,g.Key.Name,worlds[g.Key.World],g.Sum(p=>p.Count),g.Select(p=>p.Id).ToArray()),
            g.Select(p=>$"{p.Name}  {p.Category}".ToLowerInvariant()).Distinct().ToArray())).ToArray();
    }
    internal MapSearchResult Find(string query,int limit=50) {
        query=query.Trim().ToLowerInvariant();
        if(query.Length<3)return new([],0,false);
        limit=Math.Clamp(limit,1,20000);
        var matching=entries.Where(e=>e.SearchText.Any(t=>t.Contains(query,StringComparison.Ordinal)))
            .OrderByDescending(e=>e.Group.Name.Contains(query,StringComparison.OrdinalIgnoreCase))
            .ThenBy(e=>e.Group.Name,order).ThenBy(e=>e.Group.WorldName,order).ToArray();
        var shown=new List<MapSearchGroup>();int points=0;
        foreach(var e in matching) {
            if(shown.Count>=limit || points>0 && points+(long)e.Group.Count>10000)break;
            shown.Add(e.Group);points+=e.Group.Count;
        }
        return new(shown.ToArray(),matching.Length,shown.Count<matching.Length);
    }
}
internal sealed record MapWorldData(string RendererSha256,Dictionary<int,string> Names,Dictionary<int,string>? Groups=null);
internal static class MapWorlds
{
    static readonly Lazy<MapWorldData> data=new(Load);
    internal static IReadOnlyDictionary<int,string> Names=>data.Value.Names;
    internal static IReadOnlyDictionary<int,string> Groups=>data.Value.Groups!;
    static MapWorldData Load() {
        using var stream=typeof(MapWorlds).Assembly.GetManifestResourceStream("Wingbeat.MapWorlds.json")??throw new InvalidDataException("Map world names missing.");
        var data=JsonSerializer.Deserialize<MapWorldData>(stream)??throw new InvalidDataException();
        if(data.RendererSha256?.Length!=64 || !data.RendererSha256.All(Uri.IsHexDigit) || data.Names is not {Count: >0 and <=1024} ||
            data.Names.Any(p=>p.Key<=0 || string.IsNullOrWhiteSpace(p.Value) || p.Value.Length>300))throw new InvalidDataException("Invalid map world names.");
        if(data.Groups==null || data.Groups.Count!=data.Names.Count || data.Groups.Any(p=>!data.Names.ContainsKey(p.Key)||string.IsNullOrWhiteSpace(p.Value)||p.Value.Length>100))throw new InvalidDataException("Invalid map world groups.");
        return data;
    }
}
