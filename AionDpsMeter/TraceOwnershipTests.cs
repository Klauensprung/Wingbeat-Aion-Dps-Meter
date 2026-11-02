using System.Text.Json;
using AionDpsMeter.Models;
namespace AionDpsMeter.Local;
internal static class TraceOwnershipTests
{
    internal static void Run(Action<bool,string> check)
    {
        var actor=new Actor(1,"First",true,true,0,"Cleric",2301){PersistentId=111};
        var appearance=new CharacterPresets("First",2301,"111",110,[]){CurrentAt=110,Current=new(1,"light",null,null,0,0)};
        check(CombatEngine.TraceRace(actor,appearance,100)=="light","Trace faction accepts current appearance bound to the confirmed owner");
        check(new[]{appearance with{Name="Other"},appearance with{Server=2302},appearance with{CharacterId="222"},appearance with{CurrentAt=99},appearance with{Current=appearance.Current with{EntityId=2}}}
            .All(a=>CombatEngine.TraceRace(actor,a,100)==null),"Trace faction rejects stale and mismatched name/server/persistent/entity ownership");
        var engine=new CombatEngine();check(engine.CurrentTraceContext() is {IdentityReady:false,Achievements:null,Race:null,MapId:0},"Unconfirmed character has no live trace state");
        void Send(string kind,long at,object fields)=>engine.Apply(new("network",Network:new(kind,at,"synthetic",JsonSerializer.SerializeToElement(fields))));
        Send("account-login",100,new LoginReading(0,2301,[]));engine.Apply(new("local-player",Actor:actor));
        Send("achievements",110,new AchievementReading(true,[123]));Send("local-zone",120,new {MapId=1010});
        check(engine.CurrentTraceContext() is {IdentityReady:true,MapId:1010,Achievements.FullSnapshotAt:110},"Confirmed trace context exposes achievement baseline and captured map without requiring coordinates");
        engine.NewFight("Reset DPS");check(engine.CurrentTraceContext().Achievements?.Completed.SequenceEqual([123])==true,"DPS reset does not reset trace discoveries");
        Send("account-login",200,new LoginReading(0,2301,[]));check(engine.CurrentTraceContext() is {IdentityReady:false,Achievements:null,Race:null,MapId:0},"Login hides the previous character trace state while awaiting identity");
        Send("achievements",199,new AchievementReading(true,[999]));Send("achievements",201,new AchievementReading(false,[456]));
        engine.Apply(new("local-player",Actor:actor with{Name="Second",PersistentId=222}));
        check(engine.CurrentTraceContext().Achievements is {Complete:false,FullSnapshotAt:null,Completed:[456]},"Incoming partial trace state does not inherit the prior character full baseline");
        var markers=MapMarkers.Default;var index=new MapTargetIndex(markers.Groups);var petLinks=PetFarmLinks.Default;
        var first=markers.Groups.First(g=>g.World==1010&&g.Category=="monolith");
        var partial=TraceDiscovery.Build(markers.Groups,MapWorlds.Groups,new(1,false,[first.DatabaseId]),null);
        var count=FarmTargetProgressBuilder.Build(new("collectable",1010),index,petLinks,new Dictionary<int,long>(),new Dictionary<int,long>(),new Dictionary<int,long>(),new Dictionary<int,long>(),null,traces:partial);
        check(count.Count>0&&count.CountIsPartial&&count.Done==null&&count.Goal>count.Count,"Trace target displays partial capture as a lower bound with unknown completion");
        var all=markers.Groups.Where(g=>g.World==1010&&g.Category=="monolith").Select(g=>g.DatabaseId).Distinct().ToArray();
        var full=TraceDiscovery.Build(markers.Groups,MapWorlds.Groups,new(2,true,all){FullSnapshotAt=2},null);
        count=FarmTargetProgressBuilder.Build(new("collectable",1),index,petLinks,new Dictionary<int,long>(),new Dictionary<int,long>(),new Dictionary<int,long>(),new Dictionary<int,long>(),null,traces:full,currentWorld:1010);
        check(count.Count==count.Goal&&count.Done==true&&!count.CountIsPartial,"Current-map trace target completes only from full known discoveries for that map");
    }
}
