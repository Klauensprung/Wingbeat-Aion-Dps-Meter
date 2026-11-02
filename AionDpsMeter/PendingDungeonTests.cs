using System.Text.Json;
using AionDpsMeter.Models;
namespace AionDpsMeter.Local;
internal static class PendingDungeonTests
{
    internal static void Run(Action<bool,string> check)
    {
        var owner=new Actor(1,"Incoming",true,false,0,"Cleric",2301){PersistentId=6000000001};
        NetworkEvent Event(string kind,long at,object fields)=>new(kind,at,"test",JsonSerializer.SerializeToElement(fields));
        NetworkEvent Inventory(long at,long count,bool full=false)=>Event(full?"inventory":"inventory-update",at,new InventoryBatch(full,[new(100,110120003,count,1,1,5)]));
        CombatEngine Start(){var e=new CombatEngine();e.Apply(new("local-player",Actor:owner));e.Apply(new("network",Network:Inventory(1,50,true)));e.Apply(new("network",Network:Event("local-zone",2,new {MapId=600002,AwaitingIdentity=true})));return e;}
        var engine=Start();engine.Apply(new("network",Network:Inventory(3,52)));
        engine.Apply(new("network",Network:Event("local-zone",4,new {MapId=1110})));
        engine.Apply(new("network",Network:Inventory(5,53)));
        check(engine.SaveCompleted(_=>{}).All(f=>f.DungeonRun?.ReceivedItems.Length is not >0),"Unconfirmed finished runs cannot publish rewards under the old identity");
        engine.Apply(new("local-player",Actor:owner with {Id=2}));
        var archived=engine.SaveCompleted(_=>{}).Single(f=>f.DungeonRun?.ReceivedItems.Length>0);
        check(archived is {LocalPlayer:2,DungeonRun:{MapId:600002,ReceivedItems:[{Count:2}]}}&&
            archived.Actors[2].PersistentId==owner.PersistentId&&engine.Snapshot().DungeonRun==null&&engine.Loot.Rows().Sum(r=>r.Quantity)==3,
            "Pending run exit archives only its own deferred rewards after owner confirmation");
        check(ObservedRunGroups.Build([archived.ForStorage()]) is [{ReceivedItems:[{Count:2}]}],"Deferred historical run totals reach compact run-history projection");
        check(engine.SaveCompleted(_=>{}).Length==0,"Pending historical run archive is drained once");
        var restart=Start();restart.Apply(new("network",Network:Inventory(3,52)));
        restart.Apply(new("network",Network:Event("dungeon-state",4,new DungeonStateReading(600002,4,null,null))));
        restart.Apply(new("network",Network:Event("dungeon-state",5,new DungeonStateReading(600002,1,null,null))));
        restart.Apply(new("network",Network:Inventory(6,55)));restart.Apply(new("local-player",Actor:owner with {Id=2}));
        var prior=restart.SaveCompleted(_=>{}).Single(f=>f.DungeonRun?.ReceivedItems.Length>0).DungeonRun!;
        check(prior.ReceivedItems is [{Count:2}]&&restart.Snapshot().DungeonRun is {ReceivedItems:[{Count:3}]} current&&current.Id!=prior.Id,
            "Same-map restart routes pending loot by run ID rather than combining both runs");
        var tracker=new DungeonRunTracker(true);tracker.Apply(Event("local-zone",1,new {MapId=600002}));var old=tracker.Current!;
        tracker.Apply(Event("local-zone",2,new {MapId=1110}));tracker.RecordDeferredLoss(new(old.Id,old.MapId,7,9));
        check(tracker.FinishPending() is [{OmittedReceived:7,OmittedUses:9}]&&old is {OmittedReceived:0,OmittedUses:0},
            "Historical pending loss summaries amend exact run while earlier snapshots stay immutable");
        var bounded=new DungeonRunTracker(true);
        for(int i=0;i<40;i++) {
            bounded.Apply(Event("local-zone",i*3+1,new {MapId=600002}));bounded.RecordUses(i*3+2,[new(110120003,1)]);
            bounded.Apply(Event("local-zone",i*3+3,new {MapId=1110}));
        }
        check(bounded.OmittedCompletedRuns==8&&bounded.FinishPending().Length==32,"Pending historical runs retain at most 32 compact summaries and count evictions");
        bounded.Apply(Event("local-zone",121,new {MapId=600002}));bounded.RecordUses(122,[new(110120003,1)]);bounded.Apply(Event("local-zone",123,new {MapId=1110}));
        check(bounded.FinishPending().Length==0,"Confirmed tracker stops retaining an extra completed-run history");
    }
}
