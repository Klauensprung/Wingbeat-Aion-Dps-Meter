using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;
namespace AionDpsMeter.Local;

// Exports current behavior, including known gaps, for an independent reference audit.
internal static class PendingInventoryAudit
{
    internal static void Export(string directory)
    {
        var fixtures=new List<object>();
        foreach(string scenario in new[]{"confirmed-before-update","pending-known-stack","pending-full-baseline","pending-new-instance","changed-owner-full-baseline","superseded-preparation"}) {
            var session=new InventoryLootSession();var owner=new Actor(1,"First",true,false,0,"Cleric",2301){PersistentId=6000000001};
            var next=owner with {Id=2,Name=scenario=="changed-owner-full-baseline"?"Second":"First",PersistentId=scenario=="changed-owner-full-baseline"?6000000002:6000000001};
            var events=new List<object>();long quantity=0;
            void Bind(Actor actor){quantity+=session.Bind(actor).Sum(r=>r.Gain?.Quantity??0);events.Add(new {Type="identity",At=0,Owner=actor.Name});}
            void Prepare(long at){session.Prepare(at);events.Add(new {Type="map",At=at,Map=1110});}
            void Stack(long at,long count,bool full=false,long instance=100,byte modification=1){
                var message=new NetworkEvent(full?"inventory":"inventory-update",at,"audit",JsonSerializer.SerializeToElement(new InventoryBatch(full,[new(instance,110120003,count,1,modification,5)])));
                var gains=session.Apply(message,null);quantity+=gains.Sum(g=>g.Quantity);
                events.Add(new {Type=full?"inventory":"inventory-update",At=at,Instance=instance,Item=110120003,Count=count,Added=modification==0});
            }
            // Apply needs the currently confirmed actor to authorize a gain.
            Bind(owner);session.Apply(new("inventory",1,"audit",JsonSerializer.SerializeToElement(new InventoryBatch(true,[new(100,110120003,50,1)]))),owner);
            events.Add(new {Type="inventory",At=1,Instance=100,Item=110120003,Count=50,Added=false});
            Prepare(2);
            if(scenario=="confirmed-before-update")Bind(next);
            if(scenario is "pending-full-baseline" or "changed-owner-full-baseline" or "superseded-preparation")Stack(3,100,true);
            if(scenario=="confirmed-before-update") {
                var message=new NetworkEvent("inventory-update",4,"audit",JsonSerializer.SerializeToElement(new InventoryBatch(false,[new(100,110120003,52,1,1,5)])));
                quantity+=session.Apply(message,next).Sum(g=>g.Quantity);events.Add(new {Type="inventory-update",At=4,Instance=100,Item=110120003,Count=52,Added=false});
            } else Stack(4,scenario is "pending-full-baseline" or "changed-owner-full-baseline" or "superseded-preparation"?105:scenario=="pending-new-instance"?2:52,false,scenario=="pending-new-instance"?200:100,(byte)(scenario=="pending-new-instance"?0:1));
            if(scenario=="superseded-preparation")Prepare(5);
            if(scenario!="confirmed-before-update")Bind(next);
            fixtures.Add(new {Name=scenario,Events=events,LocalQuantity=quantity,CurrentHoldings=session.Holdings()});
        }
        File.WriteAllText(Path.Combine(directory,"pending-inventory-audit.json"),JsonSerializer.Serialize(fixtures));
    }
}
