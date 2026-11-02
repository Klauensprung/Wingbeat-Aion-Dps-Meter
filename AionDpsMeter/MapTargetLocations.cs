namespace AionDpsMeter.Local;

internal sealed record TargetMapLocation(int World,string Name,int Spawns,bool CanOpen);
internal static class MapTargetLocations
{
    internal static MapMarkerGroup[] Resolve(MapTarget target,MapTargetIndex markers,PetFarmLinks pets) =>
        target.Kind=="item"&&ItemFarmLinks.Default.Find(target.Id) is {} item?
            item.Npcs.SelectMany(id=>markers.Groups(new("npc",id))).Concat(item.Gatherables.SelectMany(id=>markers.Groups(new("gatherable",id)))).DistinctBy(g=>g.Id).ToArray():
        target.Kind=="pet"?(pets.Find(target.Id)?.Npcs??[]).SelectMany(id=>markers.Groups(new("npc",id))).DistinctBy(g=>g.Id).ToArray():
        target.Kind is "npc" or "gatherable" or "collectable"?markers.Groups(target):[];
    internal static MapMarkerGroup[] RemainingTraces(MapTarget target,IEnumerable<MapMarkerGroup> markers,TraceDiscoverySnapshot traces) {
        if(target.Kind!="collectable")return [];
        var ids=traces.Worlds.Where(w=>target.Id==1||w.World==target.Id).SelectMany(w=>w.RemainingGroups).ToHashSet();
        return markers.Where(g=>ids.Contains(g.Id)).ToArray();
    }
    internal static TargetMapLocation[] Worlds(IEnumerable<MapMarkerGroup> groups,IReadOnlyDictionary<int,string> names)=>groups.GroupBy(g=>g.World)
        .Select(g=>new TargetMapLocation(g.Key,names.GetValueOrDefault(g.Key)??$"Map {g.Key}",g.Sum(p=>p.Count),names.ContainsKey(g.Key)))
        .OrderByDescending(g=>g.Spawns).ThenBy(g=>g.Name,StringComparer.Ordinal).ToArray();
    internal static int? SelectWorld(IEnumerable<TargetMapLocation> worlds,int current) {
        var open=worlds.Where(w=>w.CanOpen).ToArray();return open.Any(w=>w.World==current)?current:open.FirstOrDefault()?.World;
    }
}
