using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;
internal static class LoginBaselineOrderTests
{
    internal static void Run(Action<bool,string> check)
    {
        IEnumerable<int[]> Orders(int[] remaining) => remaining.Length == 0 ? new[] { Array.Empty<int>() } :
            remaining.SelectMany(n => Orders(remaining.Where(x => x != n).ToArray()).Select(tail => new[] { n }.Concat(tail).ToArray()));
        bool hidden=true, restored=true, credited=true, immutable=true; int cases=0;
        foreach (var order in Orders([0,1,2,3])) {
            cases++; var engine=new CombatEngine();
            var owner=new Actor(1,"First",true,true,0,"Cleric",2301) { PersistentId=6000000001 };
            engine.Apply(new("local-player",Actor:owner));
            void Send(string kind,long at,object fields)=>engine.Apply(new("network",Network:new(kind,at,"synthetic",JsonSerializer.SerializeToElement(fields))));
            void Inventory(long at,long count,bool full)=>Send(full?"inventory":"inventory-update",at,new InventoryBatch(full,[new(100,930100031,count,1,1,5)]));
            Inventory(10,10,true);Inventory(11,11,false);
            Send("pet-roster",12,new CapturedPetRoster(1002,[new(1002,3)],[new(1002,99)],0));
            var historical=engine.Snapshot();
            Send("account-login",100,new LoginReading(0,2301,[]));long at=100;
            foreach(int step in order) {
                at++;
                switch(step) {
                    case 0:Inventory(at,500,true);break;
                    case 1:Send("pet-roster",at,new CapturedPetRoster(1002,[new(1002,1)],[new(1002,2)],0));break;
                    case 2:
                        Send("content-tickets",at,new ContentTicketReading(true,[new(60000001,30,7,0,0)]));
                        Send("currency-ticket",at,new CurrencyTicketReading(true,37));
                        engine.Apply(new("energy",Energy:new(30,7,true,at)));break;
                    case 3:Send("local-zone",at,new {MapId=1010,AwaitingIdentity=true});break;
                }
                hidden &= engine.CurrentPets()==null&&engine.CurrentCurrencies().Length==0&&engine.CurrentTickets().Length==0&&
                    engine.Fight.Energy==null&&!engine.CurrentHoldings().Complete;
                Inventory(99,999,true); // Prior-session late traffic cannot replace the incoming baseline.
            }
            engine.Apply(new("local-player",Actor:owner with {Id=2,Name="Second",PersistentId=7000000001}));
            restored &= engine.CurrentPets()?.Pets is [{Level:1,Souls:2}]&&engine.CurrentTickets() is [{Count:30,Extra:7}]&&
                engine.CurrentTicketsComplete&&engine.Fight.Energy is {Daily:30,Bonus:7}&&engine.CurrentHoldings().Complete&&
                engine.CurrentCurrencies().Single(c=>c.ItemId==930100031).Count==500&&
                engine.CurrentCurrencies().Single(c=>c.ItemId==512000014).Count==37;
            Inventory(110,501,false);Inventory(111,501,false);
            credited &= engine.Loot.Rows().Where(r=>r.Character=="First").Sum(r=>r.Quantity)==1&&
                engine.Loot.Rows().Where(r=>r.Character=="Second").Sum(r=>r.Quantity)==1;
            immutable &= historical.Pets?.Pets is [{Level:3,Souls:99}];
        }
        check(cases==24&&hidden,"All login baseline orderings hide unconfirmed pet, currency, ticket, energy and bag state");
        check(restored,"Inventory, pet, ticket and map preparation orders bind consistent new-owner baselines");
        check(credited,"Stale prior-session and repeated post-confirmation inventory cannot invent or duplicate loot across login orders");
        check(immutable,"All baseline orderings preserve detached previous-owner pet history");
    }
}
