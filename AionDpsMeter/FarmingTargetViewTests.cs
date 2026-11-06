using System.Text.Json;
namespace AionDpsMeter.Local;
internal static class FarmingTargetViewTests
{
    internal static void Run(Action<bool,string> check) {
        var markers=new MapTargetIndex([
            new(1,1010,"Orichalcum","gatherable","ore",1,"gatherable",11,101),
            new(2,1110,"Orichalcum","gatherable","ore",1,"gatherable",12,101),
            new(3,1110,"Orichalcum","gatherable","ore",1,"gatherable",12,102),
            new(4,1010,"Odyle","gatherable","ore",1,"gatherable",13,103)]);
        FarmingViewRow Row(int id,string name)=>new(new("gatherable",id),name,new("gatherable",id,0,null,false,[]),null);
        var combined=FarmingTargetView.CombineGatherables([Row(11,"Orichalcum"),Row(12,"Orichalcum"),Row(13,"Odyle")],markers,new Dictionary<int,long>{{101,2},{102,3},{103,7}});
        check(combined.Length==2&&combined[0].Progress.Count==5&&combined[1].Progress.Count==7,"Farming overlay combines material variants and counts shared object IDs only once");
        var engine=new CombatEngine();engine.Apply(new("local-player",Actor:new(1,"Self",true,true,0,"Cleric",2301)));
        var materialTargets=new MapTargets();materialTargets.SetItems([610620013,610620013]);
        engine.LootTracking.Record(610620013,3);
        var materialView=new FarmingTargetView();
        var materialBefore=materialView.Update(engine,materialTargets.Snapshot(),1,1000000);
        engine.LootTracking.Record(610620013,1);
        var materialAfter=materialView.Update(engine,materialTargets.Snapshot(),1,1000001);
        check(materialBefore.OverlayRows is [{Progress.Count:3}]&&materialAfter.OverlayRows is [{Progress.Count:4}]&&engine.GatheringTracking.Snapshot().Counts.Count==0,"Selected farming materials count acquired items without requiring gathering node events or duplicating selections");
        void Send(string kind,long at,object fields)=>engine.Apply(new("network",Network:new(kind,at,"synthetic",JsonSerializer.SerializeToElement(fields))));
        Send("local-zone",100,new {MapId=20});var view=new FarmingTargetView();MapTarget[] targets=[new("npc",2600094,2)];
        var first=view.Update(engine,targets,1,1000000);check(first.SceneChanged&&first.Notices.Length==0&&first.Rows[0].Nearby==0,"Shared farming view establishes a scene without announcing existing progress");
        Send("npc-spawn",101,new {EntityId=10,NpcId=2600094});engine.KillTracking.Record(2600094,2);
        var next=view.Update(engine,targets,1,1000001);
        check(next.Rows[0].Progress.Done==true&&next.Rows[0].Nearby==1&&next.Session.Kills==2&&next.Notices.Select(n=>n.Kind).SequenceEqual(["goal","spawn"]),"Shared farming view joins live counts, goals, session totals and notifications");
        var other=new FarmingTargetView().Update(engine,targets,1,1000001);
        check(other.Rows[0].Progress.Count==next.Rows[0].Progress.Count&&other.Notices.Length==0,"Opening a second farming view has consistent progress without replaying previous notices");
        engine.NewFight("Reset DPS");var reset=view.Update(engine,targets,1,1000002);
        check(!reset.SceneChanged&&reset.Session.Kills==2&&reset.Notices.Length==0,"DPS reset preserves farming overlay state without duplicate notifications");
        Send("pet-roster",150,new CapturedPetRoster(1002,[new(1002,3)],[new(1002,12)],0));
        view.Update(engine,targets,1,1000002);
        Send("pet-added",151,new CapturedPetLevel(1005,1));
        check(view.Update(engine,targets,1,1000002).Notices is [{Kind:"registered",Key:"pet:1005"}],
            "Farming view delivers pet registration notices even without a pinned pet target");
        Send("pet-progress-decrease",152,new {Gains=new[]{new CapturedPetProgress(1005,5)}});
        Send("pet-progress-decrease",153,new {Gains=new[]{new CapturedPetProgress(1005,25)}});
        check(view.Update(engine,targets,1,1000002).Notices is [{Kind:"levelup",Key:"pet:1005",Count:2}],
            "Farming view announces soul-spend inferred upgrades through the counter watcher");
        Send("pet-added",154,new CapturedPetLevel(1005,2));engine.NewFight("Reset DPS");
        check(view.Update(engine,targets,1,1000002).Notices.Length==0,
            "Pet confirmation and DPS reset do not repeat farming upgrade notices");
        var oldPets=engine.Snapshot().Pets;
        Send("account-login",200,new LoginReading(0,2301,[]));var login=view.Update(engine,targets,1,1000003);
        check(login.SceneChanged&&login.Rows[0].Nearby==null&&login.Notices.Length==0,"Character handoff invalidates nearby display and old notification comparison");
        var petLogin=view.Update(engine,[new("pet",1002)],3,1000003);
        check(petLogin.Rows[0].Progress.Pets[0].Level==null&&petLogin.Rows[0].Progress.Done==null&&oldPets?.Pets[0].Level==3,
            "Pending login hides previous-owner pet goals while preserving the historical pet snapshot");
        Send("pet-roster",201,new CapturedPetRoster(1002,[new(1002,1)],[new(1002,2)],0));
        check(engine.CurrentPets()==null,"Incoming pet roster remains hidden before identity confirmation");
        engine.Apply(new("local-player",Actor:new(2,"Next",true,true,0,"Cleric",2301)));
        check(engine.CurrentPets()?.Pets is [{Level:1,Souls:2}]&&view.Update(engine,[new("pet",1002)],3,1000004).Rows[0].Progress.Done==false,
            "Confirmed owner receives only the new roster and its current farming goal");
        check(view.Update(engine,Enumerable.Range(1,1000).Select(id=>new MapTarget("npc",id)),1,1000004).Rows.Length==512,"Shared farming presentation bounds retained target projections");
    }
}
