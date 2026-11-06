namespace AionDpsMeter.Local;
internal sealed record FarmingViewRow(MapTarget Target,string Name,FarmTargetProgress Progress,decimal? Nearby);
internal sealed record FarmingViewSnapshot(FarmingViewRow[] Rows,FarmingSessionSummary Session,FarmingNotice[] Notices,bool SceneChanged)
{
    internal FarmingViewRow[]? OverlayRows {get;init;}
}
internal sealed class FarmingTargetView
{
    static readonly Lazy<MapTargetIndex> sharedIndex=new(()=>new(MapMarkers.Default.Groups));
    readonly FarmingNotifications notifications=new();
    readonly FarmingPetNotices petNotifications=new();
    (long Kill,long Gather,long Loot,long Soul,long Scene)? previousBoundary;
    CombatEngine? previousEngine;long session;
    internal static string Name(MapTarget t)=>t.Kind switch {
        "collectable"=>t.Id==1?"Traces in current map":$"Traces: {MapWorlds.Names.GetValueOrDefault(t.Id)??t.Id.ToString()}",
        "pet"=>PetFarmLinks.Default.Name(t.Id),"item"=>ItemNames.Default.Find(t.Id),
        _=>sharedIndex.Value.Groups(t).FirstOrDefault()?.Name??$"{t.Kind} {t.Id}"
    };
    internal FarmingViewSnapshot Update(CombatEngine engine,IEnumerable<MapTarget> selected,int petGoal,long now) {
        FarmingCounterSnapshot kills,gathers,loot,souls,registrations,upgrades;LiveTraceContext context;FarmingNearbySnapshot nearby;PetCollectionSnapshot? collection;long scene;
        lock(engine.Gate){kills=engine.KillTracking.Snapshot();gathers=engine.GatheringTracking.Snapshot();loot=engine.LootTracking.Snapshot();souls=engine.SoulTracking.Snapshot();
            registrations=engine.PetRegistrationTracking.Snapshot();upgrades=engine.PetLevelTracking.Snapshot();
            context=engine.CurrentTraceContext();nearby=engine.CurrentFarmingNearby();collection=engine.CurrentPets();scene=engine.LiveSceneRevision;}
        var boundary=(kills.Since,gathers.Since,loot.Since,souls.Since,scene);
        bool changedScene=previousBoundary?.Scene!=scene||!ReferenceEquals(previousEngine,engine);
        if(previousBoundary!=boundary||!ReferenceEquals(previousEngine,engine)){session++;previousBoundary=boundary;previousEngine=engine;}
        var targets=selected.Take(512).ToArray();var index=sharedIndex.Value;var pets=PetFarmLinks.Default;
        var traces=targets.Any(t=>t.Kind=="collectable")?TraceDiscovery.Build(MapMarkers.Default.Groups,MapWorlds.Groups,context.Achievements,context.Race):null;
        var notices=new List<FarmingNoticeTarget>();
        var rows=targets.Select(t=>{
            var progress=FarmTargetProgressBuilder.Build(t,index,pets,kills.Counts,gathers.Counts,loot.Counts,souls.Counts,collection,petGoal,traces,context.MapId);
            decimal? around=context.IdentityReady?FarmingNearby.Count(t,index,pets,nearby):null;string name=Name(t);
            bool rare=FarmingNoticeProjection.Rare(t,MapTargetLocations.Resolve(t,index,pets),nearby.MapId);
            notices.Add(FarmingNoticeProjection.Build(t,name,progress,traces,around,rare));return new FarmingViewRow(t,name,progress,around);
        }).ToArray();
        long? noticeSession=context.IdentityReady?session:null;
        var petNotices=petNotifications.Update(registrations,upgrades,collection,noticeSession,id=>pets.Find(id)?.Name??PetCatalogue.Default.Find(id)?.Name);
        return new(rows,FarmingSession.Build(kills,souls,now),[..notifications.Update(notices,noticeSession,now),..petNotices],changedScene){OverlayRows=CombineGatherables(rows,index,gathers.Counts)};
    }
    internal static FarmingViewRow[] CombineGatherables(FarmingViewRow[] rows,MapTargetIndex index,IReadOnlyDictionary<int,long> counts)=>
        rows.GroupBy(r=>r.Target.Kind=="gatherable"?(Kind:r.Target.Kind,Key:r.Name):(Kind:r.Target.Kind,Key:r.Target.Id.ToString()))
            .Select(group=>{
                var first=group.MinBy(r=>r.Target.Id)!;
                if(first.Target.Kind!="gatherable")return first;
                decimal count=group.SelectMany(r=>index.ObjectIds(r.Target)).Distinct().Sum(id=>(decimal)counts.GetValueOrDefault(id));
                return first with {Progress=first.Progress with {Count=count},Nearby=null};
            }).ToArray();
    internal static string Notice(FarmingNotice n)=>n.Kind switch {"goal"=>$"Goal reached: {n.Name} ({n.Count:N0})","spawn"=>$"Nearby increase: {n.Name}",
        "registered"=>$"Pet registered: {n.Name}","levelup"=>$"Pet level up: {n.Name} (level {n.Count:N0})",
        "milestone"=>$"{n.Name} — collection reward: {(n.Count%50==0?"10 candy":"100 crystals")}",
        _=>n.Goal is >0?$"Trace found: {n.Count:N0} / {n.Goal:N0}":"Trace found"};
}
