using System.IO;
using System.Text.Json;
namespace AionDpsMeter.Local;
internal static class FarmTargetProgressTests
{
    internal static void Run(Action<bool,string> check,string temp)
    {
        var links=PetFarmLinks.Default;var markers=MapMarkers.Default;var index=new MapTargetIndex(markers.Groups);
        check(links.LoadError==null&&links.Pets.Length==207&&links.Pets.Sum(p=>p.Npcs.Length)==2256&&links.Pets.Sum(p=>p.SoulItems.Length)==195,
            "Pet farming links retain all 2256 NPC links from the exact existing spawn-data revision");
        var pet=links.Pets.First(p=>p.Npcs.Length>=2);var kills=new Dictionary<int,long>{{pet.Npcs[0],5},{pet.Npcs[1],2}};
        var gathers=new Dictionary<int,long>{{1500043,3},{1500061,4}};var loot=new Dictionary<int,long>{{123,9}};var souls=new Dictionary<int,long>{{pet.Id,2}};
        var cases=new List<object>();
        FarmTargetProgress Case(MapTarget target,int goal,int level) {
            var collection=new PetCollectionSnapshot(1,0,true,[new(pet.Id,level,0,0)]);
            var result=FarmTargetProgressBuilder.Build(target,index,links,kills,gathers,loot,souls,collection,goal);
            cases.Add(new {Target=target,PetGoal=goal,Levels=collection.Pets.ToDictionary(p=>p.PetId,p=>p.Level),Kills=kills,Gathered=gathers,Loot=loot,Souls=souls,
                Expected=new {result.Count,result.Goal,result.Done,KillsPerSoul=target.Kind=="pet"?result.Pets.FirstOrDefault()?.KillsPerSoul:null,PetGains=result.Pets.ToDictionary(p=>p.Id,p=>p.SoulsGained)}});
            return result;
        }
        var progress=Case(new("pet",pet.Id),2,1);
        check(progress.Count==7&&progress.Pets.Single().SoulsGained==2&&progress.Pets.Single().KillsPerSoul==4&&progress.Done==false,
            "Tracked pet progress joins all linked NPC kills and explicit soul gains, rounding kills per soul like the reference");
        check(Case(new("pet",pet.Id,999),2,2) is {Done:true,Goal:null},"Pet completion uses selected pet level rather than numeric kill goal");
        var unknown=FarmTargetProgressBuilder.Build(new("pet",pet.Id),index,links,kills,gathers,loot,souls,null,2);
        check(unknown.Count==7&&unknown.Done==null&&unknown.Pets.Single().Level==null,"Missing pet roster preserves unknown completion without discarding known kills");
        for(int goal=1;goal<=3;goal++)for(int level=0;level<=3;level++)Case(new("pet",pet.Id),goal,level);
        progress=Case(new("npc",pet.Npcs[0],4),1,1);
        check(progress.Count==5&&progress.GoalReached&&progress.Done==true&&progress.Pets.Any(p=>p.Id==pet.Id),
            "NPC targets complete their numeric goal while retaining associated pet gains");
        Case(new("gatherable",1081004,10),1,1);Case(new("item",123,8),1,1);
        File.WriteAllText(Path.Combine(temp,"reference-farm-target-progress.json"),JsonSerializer.Serialize(new {Pets=links.Pets,Groups=markers.Groups,Cases=cases}));
        var noSouls=FarmTargetProgressBuilder.Build(new("pet",pet.Id),index,links,kills,gathers,loot,new Dictionary<int,long>(),null);
        check(noSouls.Pets.Single().KillsPerSoul==null,"Zero observed soul gains do not produce a fabricated kills-per-soul ratio");
        var unlinked=PetCatalogue.Default.Pets.First(p=>links.Find(p.Id)==null);
        check(links.Targets.Select(p=>p.Id).Distinct().Count()==links.Targets.Count() &&
            PetCatalogue.Default.Pets.All(p=>links.Targets.Any(t=>t.Id==p.Id)) && links.Targets.Any(p=>p.Id==unlinked.Id&&p.Name==unlinked.Name),
            "The pet target picker includes every named catalogue pet without changing verified farming links or duplicating IDs");
        var unlinkedTarget=new MapTarget("pet",unlinked.Id);
        var unlinkedProgress=FarmTargetProgressBuilder.Build(unlinkedTarget,index,links,kills,gathers,loot,
            new Dictionary<int,long>{{unlinked.Id,3}},new(1,0,true,[new(unlinked.Id,2,0,0)]),2);
        check(unlinkedProgress is {Count:null,Done:true} && unlinkedProgress.Pets.Single() is {Level:2,SoulsGained:3,KillsPerSoul:null} &&
            unlinkedProgress.Pets.Single().Name==unlinked.Name,
            "Pets without farming links retain named level and soul progress without inventing a kills-per-soul ratio");
        check(FarmingNearby.Count(unlinkedTarget,index,links,new(1010,[],[]))==null && MapTargetLocations.Resolve(unlinkedTarget,index,links).Length==0,
            "Unverified pet NPC links remain unknown nearby counts with no invented locations");
    }
}
