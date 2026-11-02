using AionDpsMeter.Models;
using System.IO;
using System.Text.Json;
namespace AionDpsMeter.Local;
internal static class NearbyTrackingTests
{
    internal static void Run(Action<bool,string> check,string temp) {
        var npc=new FarmingKillTracker();var objects=new WorldObjectTracker();var fixtures=new List<object>();long at=0;
        void Step(string kind,int entity=10,int id=100,int? hp=100,int owner=0) {
            at++;
            string local=kind switch {"spawn"=>"npc-spawn","death"=>"entity-death","removed"=>"entity-removed","object"=>"world-object","object-removed"=>"world-object-removed","map"=>"local-zone",_=>kind};
            var fields=JsonSerializer.SerializeToElement(new {EntityId=entity,NpcId=id,ObjectId=id,OwnerId=owner,CurrentHp=hp,MaximumHp=200,MapId=1010,Removal="despawned",X=1,Y=2,Z=3});
            var message=new NetworkEvent(local,at,"synthetic",fields);npc.Apply(message);objects.Apply(message);
            fixtures.Add(new {Kind=kind,At=at,Entity=entity,Id=id,Hp=hp,Owner=owner,Expected=new {Around=npc.Around(),ObjectsAround=objects.Around()}});
        }
        Step("map");Step("spawn");Step("spawn",11);Step("spawn",12,200,owner:1);
        check(npc.Around().GetValueOrDefault(100)==2&&!npc.Around().ContainsKey(200),"Nearby NPC counts include distinct spawns and exclude owned summons");
        Step("hp",hp:0);Step("death");check(npc.Around()[100]==1,"Nearby NPC counts exclude zero-HP and dead entities without engagement");
        Step("hp",hp:100);check(npc.Around()[100]==2,"Positive HP restores a captured nearby NPC");
        Step("hp",hp:null);check(npc.Around()[100]==2,"Maximum-HP-only reading does not mark a nearby NPC dead");
        Step("removed");Step("spawn",11,300);check(npc.Around().Count==1&&npc.Around()[300]==1,"Removal and reused spawn identity replace nearby NPC counts");
        Step("object",20,1590001);Step("object",21,1590001);Step("gathered",20);
        check(objects.Around()[1590001]==2,"Gather action alone does not remove a nearby world object");
        Step("object-removed",20);Step("object",21,1500043);
        check(objects.Around().Count==1&&objects.Around()[1500043]==1,"Object removal and replacement update nearby IDs without duplicates");
        Step("map");check(npc.Around().Count==0&&objects.Around().Count==0,"Map boundary clears nearby NPCs and objects");
        File.WriteAllText(Path.Combine(temp,"reference-nearby-tracking.json"),JsonSerializer.Serialize(fixtures));
        var index=new MapTargetIndex(MapMarkers.Default.Groups);var pets=PetFarmLinks.Default;
        var state=new FarmingNearbySnapshot(index.Groups(new("gatherable",2000001))[0].World,new(){{100,3}},new(){{1590001,2},{1500043,5},{1500061,7}});
        check(FarmingNearby.Count(new("npc",100),index,pets,state)==3&&FarmingNearby.Count(new("gatherable",2000001),index,pets,state)==2,"Target nearby counts use NPC database IDs and current-map gathering object IDs");
        check(FarmingNearby.Count(new("item",123),index,pets,state)==null&&FarmingNearby.Count(new("pet",int.MaxValue),index,pets,state)==null,"Missing item and pet mappings do not fabricate nearby counts");
        check(FarmingNearby.Count(new("gatherable",2000001),index,pets,state with {MapId=0})==null,"Unknown map reports unavailable nearby data rather than a false zero");
        var cases=new List<object>();
        var targets=MapMarkers.Default.Groups.Where(g=>g.DatabaseType=="gatherable").Select(g=>new MapTarget("gatherable",g.DatabaseId)).Distinct()
            .Concat(pets.Pets.Take(12).Select(p=>new MapTarget("pet",p.Id))).Append(new("npc",100));
        foreach(var target in targets) {
            var locations=MapTargetLocations.Resolve(target,index,pets);
            foreach(int world in new[]{0,locations.FirstOrDefault()?.World??1010}) {
                var nearby=new FarmingNearbySnapshot(world,MapMarkers.Default.Groups.Where(g=>g.DatabaseType=="npc").Select(g=>g.DatabaseId).Distinct().ToDictionary(id=>id,id=>(long)(id%7+1)),
                    MapMarkers.Default.Groups.Where(g=>g.ObjectId>0).Select(g=>g.ObjectId).Distinct().ToDictionary(id=>id,id=>(long)(id%5+1)));
                cases.Add(new {Target=target,World=world,nearby.Npcs,nearby.Objects,Expected=FarmingNearby.Count(target,index,pets,nearby)});
            }
        }
        File.WriteAllText(Path.Combine(temp,"reference-nearby-targets.json"),JsonSerializer.Serialize(new {Groups=MapMarkers.Default.Groups,Pets=pets.Pets,Cases=cases}));
        var engine=new CombatEngine();engine.Apply(new("local-player",Actor:new(1,"Self",true,true,0,"Cleric",2301)));
        void Send(string kind,long time,object fields)=>engine.Apply(new("network",Network:new(kind,time,"synthetic",JsonSerializer.SerializeToElement(fields))));
        Send("local-zone",100,new {MapId=902});Send("npc-spawn",101,new {EntityId=10,NpcId=100});
        engine.NewFight("Reset DPS");check(engine.CurrentFarmingNearby() is {MapId:1010} live&&live.Npcs.GetValueOrDefault(100)==1,"DPS reset preserves nearby scene and canonical map alias");
        Send("account-login",200,new LoginReading(0,2301,[]));Send("npc-spawn",199,new {EntityId=10,NpcId=100});
        check(engine.CurrentFarmingNearby().Npcs.Count==0&&engine.CurrentFarmingNearby().Objects.Count==0,"Login clears nearby scene and rejects prior-character spawn traffic");
    }
}
