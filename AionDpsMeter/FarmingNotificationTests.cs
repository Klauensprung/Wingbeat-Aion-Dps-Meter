using System.IO;
using System.Text.Json;
namespace AionDpsMeter.Local;
internal static class FarmingNotificationTests
{
    internal static void Run(Action<bool,string> check,string temp) {
        var tracker=new FarmingNotifications();var fixtures=new List<object>();
        var target=new FarmingNoticeTarget("npc:100","Example NPC",0,5,false,null,null,true,0);
        FarmingNotice[] Step(long now,long? since,params FarmingNoticeTarget[] rows) {
            var notices=tracker.Update(rows,since,now);fixtures.Add(new {Now=now,Since=since,Targets=rows,Expected=notices});return notices;
        }
        check(Step(1000000,1,target).Length==0,"Initial tracking does not announce existing state");
        check(Step(1000001,1,target with{Count=5,Around=1}).Select(n=>n.Kind).SequenceEqual(["goal","spawn"]),"Goal crossing and rare nearby increase emit ordered notices");
        check(Step(1000002,1,target with{Count=6,Around=2}).Length==0,"Repeated goal totals and rapid spawns do not repeat alerts");
        Step(1300000,1,target with{Count=6,Around=0});
        check(Step(1300001,1,target with{Count=6,Around=1}).Length==0,"Rare spawn cooldown is strict at exactly five minutes");
        check(Step(1300002,1,target with{Count=6,Around=2}).Single().Kind=="spawn","Rare spawn increase alerts after the five-minute boundary");
        check(Step(1600003,2,target with{Count=10,Around=3}).Length==0,"Farming reset establishes a new baseline without notices");
        Step(1600004,2);check(Step(1600005,2,target with{Count=10,Around=4}).Length==0,"Adding or re-adding a target does not announce existing progress");
        Step(1600006,null,target);check(Step(1600007,2,target with{Count=5,Around=5}).Length==0,"Missing session baseline cannot trigger notifications on reconnect");
        var trace=new FarmingNoticeTarget("collectable:1","Traces",1,560,true,1,100,false,0);
        Step(1700000,2,trace);
        check(Step(1700001,2,trace with{Count=2,Collected=2}).Single().Kind=="collected","Trace gain with a stable full-list baseline emits a notice");
        check(Step(1700002,2,trace with{Count=3,Collected=3,Baseline=200}).Length==0,"New full achievement baseline does not announce old discoveries");
        Step(1700003,2,trace with{Count=4,Collected=null,Baseline=null});
        check(Step(1700004,2,trace with{Count=5,Collected=5}).Length==0,"Partial-to-full achievement capture does not invent a new trace acquisition");
        Step(1700005,2,trace with{Count=559,Collected=559});
        check(Step(1700006,2,trace with{Count=560,Collected=560}).Select(n=>n.Kind).SequenceEqual(["collected"]),"Trace completion does not also emit an ordinary farming goal notice");
        Step(1800000,2,target with{Goal=10,Count=6,Rare=false});
        check(Step(1800001,2,target with{Goal=5,Count=6,Rare=false,Around=50}).Length==0,"Lowering a goal below an already reached count does not synthesize a crossing");
        Step(1800002,2,target with{Goal=0,Count=0,Rare=false});check(Step(1800003,2,target with{Goal=0,Count=10,Rare=false}).Length==0,"Zero goal does not generate completion notifications");
        File.WriteAllText(Path.Combine(temp,"reference-farming-notifications.json"),JsonSerializer.Serialize(fixtures));
        var markers=MapMarkers.Default.Groups;var index=new MapTargetIndex(markers);var pets=PetFarmLinks.Default;
        var rareFixtures=new List<object>();
        foreach(var group in markers.Where(g=>g.DatabaseType=="npc"&&g.Type=="monster").DistinctBy(g=>g.Category).Concat(markers.Where(g=>g.DatabaseType=="gatherable").Take(10))) {
            var t=MapTargetIndex.Identity(group)!;var locations=MapTargetLocations.Resolve(t,index,pets);
            foreach(int world in new[]{0,group.World})rareFixtures.Add(new {Target=t,World=world,Expected=FarmingNoticeProjection.Rare(t,locations,world)});
        }
        File.WriteAllText(Path.Combine(temp,"reference-farming-rare.json"),JsonSerializer.Serialize(new {Groups=markers,Pets=pets.Pets,Cases=rareFixtures}));
        MapMarkerGroup[] synthetic=[new(0,1010,"Example","monster","normal",3,"npc",100)];
        check(FarmingNoticeProjection.Rare(new("npc",100),synthetic,1010)&&!FarmingNoticeProjection.Rare(new("npc",100),[synthetic[0] with{Count=4}],1010),"Rarity uses physical spawn count, with a three-location threshold");
        check(FarmingNoticeProjection.Rare(new("npc",100),[synthetic[0] with{Count=100,Category="elite"}],1010)&&!FarmingNoticeProjection.Rare(new("collectable",1),synthetic,1010),"Elite category overrides count while trace targets never classify as rare");
        var discovery=new TraceDiscoverySnapshot([new(1010,2,560,[],[],false),new(1110,5,560,[],[],false)],7,100,true);
        var progress=new FarmTargetProgress("collectable",1,2,560,false,[]);
        check(FarmingNoticeProjection.Build(new("collectable",1),"Traces",progress,discovery,0,false) is {Collected:7,Count:2,Baseline:100},"Global trace alerts compare all eligible discoveries while retaining current-map display progress");
        check(FarmingNoticeProjection.Build(new("collectable",1010),"Verteron",progress,discovery,0,false).Collected==2,
            "Per-map trace alerts compare only that map discoveries");
        check(FarmingNoticeProjection.Build(new("collectable",1),"Traces",progress,discovery with {TotalFound=null,HasFullSnapshot=false},0,false).Collected==null,
            "Partial discovery captures do not establish a notification acquisition total");
        var bounded=new FarmingNotifications();
        for(int batch=0;batch<3;batch++) {
            var rows=Enumerable.Range(batch*512,512).Select(id=>target with{Key=$"npc:{id}"}).ToArray();
            bounded.Update(rows,1,2000000+batch);bounded.Update(rows.Select(t=>t with{Around=1}),1,2000001+batch);
        }
        check(bounded.RetainedTargets==512&&bounded.RetainedThrottles==512,"Notification previous-state and cooldown retention remain bounded across target churn");
        var queue=new FarmingNoticeQueue();var delivery=new List<object>();
        FarmingNotice Notice(int id)=>new("goal",$"npc:{id}",$"Target {id}",id,null);
        FarmingNotice[] Deliver(long now,params FarmingNotice[] incoming) {
            var shown=queue.Update(incoming,now);delivery.Add(new {Now=now,Incoming=incoming,Expected=shown});return shown;
        }
        check(Deliver(1000,Notice(1),Notice(2)).Select(n=>n.Key).SequenceEqual(["npc:2","npc:1"]),"Overlay retains simultaneous notices newest first");
        check(Deliver(2000,Notice(3),Notice(4)).Select(n=>n.Key).SequenceEqual(["npc:4","npc:3","npc:2"]),"Overlay notice queue retains at most three entries");
        check(Deliver(8999).Length==3&&Deliver(9000).Select(n=>n.Key).SequenceEqual(["npc:4","npc:3"]),"Overlay notices expire exactly eight seconds after arrival");
        check(Deliver(10000,Notice(5)).Single().Key=="npc:5","New overlay notices survive expiration of older notices");
        Deliver(11000,Notice(5));
        check(Deliver(18000).Single().Key=="npc:5"&&Deliver(19000).Length==0,"Repeated target notices have independent lifetimes");
        File.WriteAllText(Path.Combine(temp,"reference-farming-delivery.json"),JsonSerializer.Serialize(delivery));
        queue.Update([Notice(6)],20000);
        check(queue.Update([],20001,true).Length==0,"Scene transition clears overlay notice history");
    }
}
