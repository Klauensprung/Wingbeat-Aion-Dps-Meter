namespace AionDpsMeter.Local;

internal sealed record FarmingNoticeTarget(string Key,string Name,decimal? Count,long? Goal,bool Collects,int? Collected,long? Baseline,bool Rare,decimal? Around);
internal sealed record FarmingNotice(string Kind,string Key,string Name,decimal? Count,long? Goal);
internal static class FarmingNoticeProjection
{
    internal static bool Rare(MapTarget target,IEnumerable<MapMarkerGroup> locations,int world) {
        if(target.Kind=="collectable"||world<=0)return false;
        var here=locations.Where(g=>g.World==world).ToArray();
        return here.Length>0&&(here.Sum(g=>(long)g.Count)<=3||here.Any(g=>g.Category is "named" or "elite" or "world-boss"));
    }
    internal static FarmingNoticeTarget Build(MapTarget target,string name,FarmTargetProgress progress,TraceDiscoverySnapshot? traces,
        decimal? around,bool rare) {
        int? collected=null;
        if(target.Kind=="collectable"&&traces?.TotalFound is int total)
            collected=target.Id==1?total:traces.Worlds.FirstOrDefault(w=>w.World==target.Id)?.Found??0;
        return new($"{target.Kind}:{target.Id}",name,progress.Count,progress.Goal,target.Kind=="collectable",collected,traces?.Baseline,rare,around);
    }
}
// Retains only the previous bounded projection and per-target spawn throttle times.
internal sealed class FarmingNotifications
{
    const int Limit=512;
    Dictionary<string,FarmingNoticeTarget> previous=new(StringComparer.Ordinal);
    readonly Dictionary<string,long> spawnedAt=new(StringComparer.Ordinal);
    long? previousSince;
    internal int RetainedTargets=>previous.Count;
    internal int RetainedThrottles=>spawnedAt.Count;
    internal FarmingNotice[] Update(IEnumerable<FarmingNoticeTarget> targets,long? since,long now) {
        var rows=targets.Take(Limit).DistinctBy(t=>t.Key).ToArray();var notices=new List<FarmingNotice>();
        if(since.HasValue&&since==previousSince)foreach(var row in rows) {
            if(!previous.TryGetValue(row.Key,out var old))continue;
            if(row.Collected.HasValue&&old.Collected.HasValue&&row.Collected>old.Collected&&row.Baseline==old.Baseline)
                notices.Add(new("collected",row.Key,"Trace found",row.Goal is >0?row.Count:null,row.Goal is >0?row.Goal:null));
            if(!row.Collects&&row.Goal is >0&&row.Count>=row.Goal&&old.Count<row.Goal)
                notices.Add(new("goal",row.Key,row.Name,row.Count,null));
            if(row.Rare&&row.Around>old.Around&&(decimal)now-spawnedAt.GetValueOrDefault(row.Key)>300000) {
                if(!spawnedAt.ContainsKey(row.Key)&&spawnedAt.Count>=Limit)spawnedAt.Remove(spawnedAt.MinBy(p=>p.Value).Key);
                spawnedAt[row.Key]=now;notices.Add(new("spawn",row.Key,row.Name,null,null));
            }
        }
        previous=rows.ToDictionary(t=>t.Key,StringComparer.Ordinal);previousSince=since;return notices.ToArray();
    }
}
