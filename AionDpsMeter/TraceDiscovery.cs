namespace AionDpsMeter.Local;

internal sealed record TraceWorldProgress(int World,int Found,int Total,int[] FoundGroups,int[] RemainingGroups,bool? Complete);
internal sealed record TraceDiscoverySnapshot(TraceWorldProgress[] Worlds,int? TotalFound,long? Baseline,bool HasFullSnapshot);
internal static class TraceDiscovery
{
    internal static bool CanCollect(int world,string? race,IReadOnlyDictionary<int,string> groups) {
        string? group=groups.GetValueOrDefault(world);
        return race is not ("light" or "dark") || group is not ("elyos" or "asmodians") || (group=="elyos")== (race=="light");
    }
    internal static TraceDiscoverySnapshot Build(IEnumerable<MapMarkerGroup> markers,IReadOnlyDictionary<int,string> worldGroups,AchievementSnapshot? achievements,string? ownerRace) {
        var completed=achievements?.Completed.ToHashSet()??new();bool full=achievements?.Complete==true;
        var monoliths=markers.Where(g=>g.Category=="monolith").ToArray();
        var traces=monoliths.Where(g=>g.DatabaseType=="achievement"&&CanCollect(g.World,ownerRace,worldGroups));
        var worlds=traces.GroupBy(g=>g.World).Select(group=>{
            var found=group.Where(g=>completed.Contains(g.DatabaseId)).ToArray();var remaining=group.Where(g=>!completed.Contains(g.DatabaseId)).ToArray();
            return new TraceWorldProgress(group.Key,found.Sum(g=>g.Count),group.Sum(g=>g.Count),found.Select(g=>g.Id).ToArray(),remaining.Select(g=>g.Id).ToArray(),full?remaining.Length==0:null);
        }).OrderBy(w=>w.World).ToArray();
        return new(worlds,full&&monoliths.Length>0?worlds.Sum(w=>w.Found):null,achievements?.FullSnapshotAt,full);
    }
}
