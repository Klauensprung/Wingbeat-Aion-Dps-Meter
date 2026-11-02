using System.Text.Json;
using AionDpsMeter.Models;
namespace AionDpsMeter.Local;
internal static class PendingInventoryTests
{
    internal static void Run(Action<bool,string> check)
    {
        var owner=new Actor(1,"First",true,false,0,"Cleric",2301){PersistentId=6000000001};
        NetworkEvent Stack(long at,long count,bool full=false,byte reason=5,byte decrease=0,long instance=100,byte modification=1)=>new(full?"inventory":"inventory-update",at,"test",
            JsonSerializer.SerializeToElement(new InventoryBatch(full,[new(instance,110120003,count,1,modification,reason,decrease)])));
        InventoryLootSession Start(){var s=new InventoryLootSession();s.Bind(owner);s.Apply(Stack(1,50,true),owner);s.Prepare(2);return s;}
        var same=Start();check(same.Apply(Stack(3,52),null,600002,"run").Length==0&&same.Holdings().Items.Length==0,"Pending transfer loot and holdings remain hidden");
        var received=same.Bind(owner with {Id=2});
        check(received is [{MapId:600002,RunId:"run",Gain:{Quantity:2,Timestamp:3,CapturedCharacter:"First",Server:2301}}]&&same.Bind(owner with {Id=2}).Length==0,
            "Same persistent owner receives pending delta once with original time/map/run context");
        foreach(var next in new[]{owner with {PersistentId=6000000002},owner with {PersistentId=0},owner with {Server=2302}}) {
            var other=Start();other.Apply(Stack(3,52),null);check(other.Bind(next).Length==0,"Changed, unknown or foreign-server owner cannot use transfer baseline");
        }
        var fresh=Start();fresh.Apply(Stack(3,100,true),null);fresh.Apply(Stack(4,105),null);
        check(fresh.Bind(owner with {Name="Second",PersistentId=6000000002}) is [{Gain:{Quantity:5,CapturedCharacter:"Second"}}],
            "Fresh pending baseline attributes only its own delta to a different confirmed character");
        var initial=new InventoryLootSession();initial.Apply(Stack(1,50,true),null);initial.Apply(Stack(2,52),null);initial.Prepare(3);
        check(initial.Bind(owner) is [{Gain:{Quantity:2}}],"Initial preparation preserves unassigned baseline and compact loot deltas");
        var added=new InventoryLootSession();added.Login(1);added.Apply(Stack(2,2,instance:200,modification:0),null);added.Prepare(3);
        check(added.Bind(owner) is [{Gain:{Quantity:2}}],"Login and matching map preparation retain explicit added-instance loot");
        var superseded=Start();superseded.Apply(Stack(3,52),null);superseded.Prepare(4);
        check(superseded.Bind(owner).Length==0,"Superseded preparation discards unassigned loot and borrowed baselines");
        var login=Start();login.Apply(Stack(3,52),null);login.Login(4);
        check(login.Bind(owner).Length==0,"Explicit login discards prior pending transfer loot");
        var uses=Start();uses.Apply(Stack(3,47,reason:0,decrease:14),null,600002,"run");
        check(uses.Bind(owner) is [{Use:{ItemId:110120003,Count:3},Gain:null}],"Pending item uses retain compact deltas for the confirmed owner");
        var bounded=new InventoryLootSession();bounded.Apply(Stack(1,0,true),null);
        for(int i=1;i<=4100;i++)bounded.Apply(Stack(i+1,i),null);
        check(bounded.OmittedPending==4&&bounded.Bind(owner).Length==4096&&bounded.ConfirmedOmittedGains==4&&bounded.Holdings().Items.Single().Count==4100,
            "Pending receipts are bounded, report omissions and keep current holdings accurate");
        var engine=new CombatEngine();engine.Apply(new("local-player",Actor:owner));engine.Apply(new("network",Network:Stack(1,50,true)));
        void Send(string kind,long at,object data)=>engine.Apply(new("network",Network:new(kind,at,"test",JsonSerializer.SerializeToElement(data))));
        Send("local-zone",2,new {MapId=600002,AwaitingIdentity=true});
        engine.Apply(new("network",Network:Stack(3,52)));engine.Apply(new("network",Network:Stack(4,49,reason:0,decrease:14)));
        Send("local-zone",5,new {MapId=600002});
        check(engine.Loot.Rows().Length==0,"Engine does not publish pending loot to the old character");
        engine.Apply(new("local-player",Actor:owner with {Id=2}));
        check(engine.Loot.Rows() is [{Quantity:2,MapId:600002,First:3}]&&engine.Snapshot().DungeonRun is {ReceivedItems:[{Count:2}],UsedItems:[{Count:3}],LastObserved:5},
            "Identity confirmation flushes original loot and use quantities to matching dungeon without rewinding its clock");
        check(engine.Snapshot().ForStorage().SavedLoot?.Rows is [{Quantity:2}],"Deferred loot survives compact fight storage");
        var overflowEngine=new CombatEngine();overflowEngine.Apply(new("local-player",Actor:owner));
        overflowEngine.Apply(new("network",Network:new("local-zone",10,"test",JsonSerializer.SerializeToElement(new {MapId=1110,AwaitingIdentity=true}))));
        overflowEngine.Apply(new("network",Network:Stack(11,0,true)));
        var beforeOverflow=overflowEngine.Loot.OverlaySnapshot();
        for(int i=1;i<=4100;i++)overflowEngine.Apply(new("network",Network:Stack(11+i,i)));
        check(!overflowEngine.Loot.OverlaySnapshot().OmittedItems,"Unconfirmed overflow cannot mark the previous owner's visible loot partial");
        overflowEngine.Apply(new("local-player",Actor:owner with {Id=2}));
        check(overflowEngine.Loot.Rows().Single().Quantity==4096&&overflowEngine.Loot.OmittedItems==4&&
            overflowEngine.Loot.OverlaySnapshot().OmittedItems&&!ReferenceEquals(beforeOverflow,overflowEngine.Loot.OverlaySnapshot())&&
            overflowEngine.Snapshot().ForStorage().SavedLoot?.OmittedItems==4,
            "Confirmed deferred overflow marks live history, cached overlay and saved fight totals partial");
        overflowEngine.Apply(new("local-player",Actor:owner with {Id=2}));
        check(overflowEngine.Loot.OmittedItems==4,"Repeated identity binding cannot double-count pending omissions");
        var abandoned=Start();for(int i=1;i<=4100;i++)abandoned.Apply(Stack(i+2,50+i),null);abandoned.Prepare(5000);abandoned.Bind(owner);
        check(abandoned.ConfirmedOmittedGains==0,"Superseded pending overflow cannot leak into another confirmed session");
        var usesOnly=Start();usesOnly.Apply(Stack(3,10000,true),null);
        for(int i=1;i<=4100;i++)usesOnly.Apply(Stack(i+3,10000-i,reason:0,decrease:14),null);
        usesOnly.Bind(owner);
        check(usesOnly.OmittedPending>0&&usesOnly.ConfirmedOmittedGains==0,"Omitted use receipts do not label loot gain totals incomplete");
        var dungeonOverflow=new CombatEngine();dungeonOverflow.Apply(new("local-player",Actor:owner));
        dungeonOverflow.Apply(new("network",Network:new("local-zone",10,"test",JsonSerializer.SerializeToElement(new {MapId=600002,AwaitingIdentity=true}))));
        dungeonOverflow.Apply(new("network",Network:Stack(11,10000,true)));
        for(int i=1;i<=4100;i++)dungeonOverflow.Apply(new("network",Network:Stack(11+i,10000-i,reason:0,decrease:14)));
        dungeonOverflow.Apply(new("network",Network:Stack(5000,5910)));
        dungeonOverflow.Apply(new("local-player",Actor:owner with {Id=2}));
        var partialDungeon=dungeonOverflow.Snapshot();
        check(partialDungeon.DungeonRun is {UsedItems:[{Count:4096}],OmittedUses:4,OmittedReceived:10,LastObserved:4107}&&
            partialDungeon.ForStorage().DungeonRun is {OmittedUses:4,OmittedReceived:10},
            "Deferred dungeon overflow preserves omitted quantities separately from receipt counts in compact storage");
        var runTracker=new DungeonRunTracker();runTracker.Apply(new("local-zone",1,"test",JsonSerializer.SerializeToElement(new {MapId=600002})));
        var original=runTracker.Current!;
        runTracker.RecordDeferredLoss(new("another-run",original.MapId,7,9));
        runTracker.RecordDeferredLoss(new(original.Id,original.MapId+1,7,9));
        check(runTracker.Current==original,"Deferred omissions cannot attach to another run or map");
        runTracker.RecordDeferredLoss(new(original.Id,original.MapId,7,9));
        check(runTracker.Current is {OmittedReceived:7,OmittedUses:9,LastObserved:1}&&original is {OmittedReceived:0,OmittedUses:0},
            "Deferred omissions preserve observation clock and immutable earlier dungeon snapshots");
        var contexts=new InventoryLootSession();contexts.Apply(Stack(1,0,true),null);
        for(int i=1;i<=4161;i++)contexts.Apply(Stack(i+1,i),null,600002,$"run-{Math.Max(0,i-4096)}");
        contexts.Bind(owner);
        check(contexts.ConfirmedLosses.Length==64&&contexts.OmittedPending==65&&contexts.ConfirmedLosses.Sum(l=>l.Received)==64,
            "Deferred loss summaries bound run-context retention while diagnostics count every omitted receipt");
        contexts.Bind(owner);
        check(contexts.ConfirmedLosses.Length==0,"Repeated confirmation cannot replay dungeon omissions");
    }
}
