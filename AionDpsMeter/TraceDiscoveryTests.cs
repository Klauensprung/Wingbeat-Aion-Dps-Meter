using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;
namespace AionDpsMeter.Local;
internal static class TraceDiscoveryTests
{
    internal static void Run(Action<bool,string> check,string temp)
    {
        var fixtures=new List<object>();
        MapMarkerGroup[] markers=[new(1,1010,"Trace A","collectable","monolith",2,"achievement",101),new(2,1110,"Trace B","collectable","monolith",1,"achievement",102),new(3,20,"Trace C","collectable","monolith",1,"achievement",103),new(4,220022,"Trace D","collectable","monolith",1,"achievement",104)];
        TraceDiscoverySnapshot Case(MapMarkerGroup[] rows,AchievementSnapshot? achievements,string? race) {
            var result=TraceDiscovery.Build(rows,MapWorlds.Groups,achievements,race);
            fixtures.Add(new {Markers=rows,Groups=MapWorlds.Groups,Completed=achievements?.Completed??[],Baseline=achievements?.FullSnapshotAt,Race=race,
                Expected=new {Worlds=result.Worlds.Select(w=>new {w.World,w.Found,w.Total,FoundGroups=w.FoundGroups.Order().ToArray(),RemainingGroups=w.RemainingGroups.Order().ToArray()}),result.TotalFound,result.Baseline}});
            return result;
        }
        var full=new AchievementSnapshot(20,true,[101,103]){FullSnapshotAt=10};
        var light=Case(markers,full,"light");var dark=Case(markers,full,"dark");var unknown=Case(markers,full,null);
        check(light.Worlds.Select(w=>w.World).SequenceEqual([20,1010,220022])&&dark.Worlds.Select(w=>w.World).SequenceEqual([20,1110,220022])&&unknown.Worlds.Length==4,
            "Trace discovery excludes only the opposing overworld faction and keeps neutral/dungeon groups");
        check(light.TotalFound==3&&light.Worlds.Single(w=>w.World==1010) is {Found:2,Total:2,Complete:true},"One completed achievement marks every corresponding physical trace marker without confusing marker IDs");
        var partial=Case(markers,new(20,false,[101]),"light");Case(markers,null,null);
        check(partial.TotalFound==null&&partial.Worlds.All(w=>w.Complete==null)&&partial.Worlds.Single(w=>w.World==1010).Found==2,
            "Partial achievement capture retains observed discoveries without claiming a complete discovery baseline");
        check(Case(markers.Where(g=>g.World==1110).ToArray(),full,"light").TotalFound==0&&Case([],full,"light").TotalFound==null,
            "Faction-ineligible trace sets have zero known discoveries while an absent catalogue stays unavailable");
        check(MapTargetLocations.RemainingTraces(new("collectable",1),markers,light).Select(g=>g.Id).SequenceEqual([4]),"Trace maps exclude discovered and faction-ineligible markers by identity");
        check(MapTargetLocations.RemainingTraces(new("collectable",1010),markers,light).Length==0,"Completed map target has no remaining locations");
        check(MapTargetLocations.RemainingTraces(new("collectable",1),markers,partial).Select(g=>g.Id).SequenceEqual([3,4]),"Partial trace maps hide only observed discoveries");
        var nextCharacter=TraceDiscovery.Build(markers,MapWorlds.Groups,null,null);
        check(MapTargetLocations.RemainingTraces(new("collectable",1),markers,nextCharacter).Length==4,"New character without a baseline restores unknown trace locations");
        foreach(var race in new string?[]{null,"light","dark"}) {
            var real=MapMarkers.Default.Groups;var ids=real.Where(g=>g.Category=="monolith").Select(g=>g.DatabaseId).Distinct().ToArray();
            foreach(int n in new[]{0,1,20,ids.Length})Case(real,new(100,true,ids.Take(n).ToArray()){FullSnapshotAt=100},race);
        }
        NetworkEvent Event(bool all,long at,params int[] ids)=>new("achievements",at,"synthetic",JsonSerializer.SerializeToElement(new AchievementReading(all,ids)));
        var state=CharacterCollections.Apply(null,Event(true,10,101));state=CharacterCollections.Apply(state,Event(false,20,103));
        check(state.Achievements is {Timestamp:20,FullSnapshotAt:10,Completed.Length:2},"Achievement deltas preserve the full-snapshot baseline timestamp");
        state=CharacterCollections.Apply(state,Event(true,30,102));check(state.Achievements is {FullSnapshotAt:30,Completed:[102]},"A replacement full achievement snapshot resets both discoveries and baseline");
        var engine=new CombatEngine();engine.Apply(new("network",Network:Event(true,100,101)));engine.Apply(new("local-player",Actor:new(1,"First",true,true,0,"Cleric",2301)));
        engine.NewFight("Reset DPS");check(engine.Fight.Actors[1].Collections?.Achievements?.FullSnapshotAt==100,"DPS reset retains the active character achievement baseline");
        engine.Apply(new("network",Network:new("account-login",200,"synthetic",JsonSerializer.SerializeToElement(new LoginReading(0,2301,[])))));
        engine.Apply(new("network",Network:Event(true,199,104)));engine.Apply(new("network",Network:Event(false,201,102)));
        engine.Apply(new("local-player",Actor:new(1,"Second",true,true,0,"Cleric",2301)));
        check(engine.Fight.Actors[1].Collections?.Achievements is {Complete:false,FullSnapshotAt:null,Completed:[102]},"Reused local entity IDs bind only the incoming achievement state after login");
        var store=new FightStore(Path.Combine(temp,"trace-baseline-store"));engine.Apply(new("network",Network:Event(true,210,101)));var fight=engine.Snapshot();store.Save(fight);
        check(store.Load(Path.Combine(store.Root,fight.Id+".json")).Actors[1].Collections?.Achievements?.FullSnapshotAt==210,"Trace baseline persists with full character detail saves");
        File.WriteAllText(Path.Combine(temp,"reference-trace-discovery.json"),JsonSerializer.Serialize(fixtures));
    }
}
