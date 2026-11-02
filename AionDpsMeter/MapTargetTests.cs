using System.IO;
using System.Text.Json;
namespace AionDpsMeter.Local;
internal static class MapTargetTests
{
    internal static void Run(Action<bool,string> check,string temp)
    {
        var batchPath=Path.Combine(temp,"batch-gather-targets.json");var batch=new MapTargets(batchPath);
        batch.AddGatherables([100,101,100]);batch.SetGoal("gatherable",100,25);batch.AddGatherables([100,101,102]);
        var persisted=new MapTargets(batchPath).Snapshot();
        check(persisted.Length==3&&persisted.Single(t=>t.Id==100).Goal==25,"Gathering resource tracking saves all source IDs once and preserves existing goals");
        bool rejected=false;try{batch.AddGatherables([103,-1]);}catch(InvalidDataException){rejected=true;}
        check(rejected&&new MapTargets(batchPath).Snapshot().Length==3&&batch.Snapshot().Length==3,"Invalid gathering batch does not partially save tracked IDs");
        var groups=MapMarkers.Default.Groups;var index=new MapTargetIndex(groups);
        var odyle=new MapTarget("gatherable",2000001);var split=new MapTarget("gatherable",1081004);
        check(index.ObjectIds(odyle).SequenceEqual([1590001])&&index.ObjectIds(split).SequenceEqual([1500043,1500061]),
            "Tracked gathering database IDs resolve distinct packet object IDs, including one-to-many mappings");
        check(index.Count(split,new Dictionary<int,long>(),new Dictionary<int,long>{{1500043,3},{1500061,4},{1081004,999}},new Dictionary<int,long>())==7,
            "Tracked gathering totals sum packet object counters once rather than the database ID");
        check(index.Count(split,new Dictionary<int,long>(),new Dictionary<int,long>{{1500043,long.MaxValue},{1500061,long.MaxValue}},new Dictionary<int,long>())==2m*long.MaxValue,
            "Combined tracked counts do not overflow when multiple object counters saturate");
        var boss=groups.Where(g=>g.World==20&&g.Name=="Guardian Lord Nahma").ToArray();
        check(boss.Select(g=>g.DatabaseId).Distinct().Count()==3 && index.Groups(new("npc",2600094),20).All(g=>g.DatabaseId==2600094),
            "Same-name boss spawn variants retain separate NPC identities for tracking");
        check(MapTargetIndex.Choices(boss,"Guardian Lord Nahma").Select(t=>t.Id).SequenceEqual(boss.Select(g=>g.DatabaseId).Distinct().Order()),
            "Map tracking offers separate database IDs for a shared display name");
        check(MapTargetIndex.Choices(index.Groups(split),index.Groups(split)[0].Name).SequenceEqual([split]),
            "Map tracking collapses multiple gathering packet IDs to one database target");
        check(MapTargetIndex.Choices(groups.Where(g=>g.Category=="monolith"),"Empyrean Trace").SequenceEqual([new MapTarget("collectable",1)]),
            "Map trace selection uses the reference global collectable target");
        check(index.Count(new("pet",1002),new Dictionary<int,long>(),new Dictionary<int,long>(),new Dictionary<int,long>())==null,
            "Missing pet/trace target integration is not presented as a verified zero count");
        File.WriteAllText(Path.Combine(temp,"reference-map-target-identities.json"),JsonSerializer.Serialize(groups.Select(g=>new {Group=g,Expected=MapTargetIndex.Identity(g)})));
        string path=Path.Combine(temp,"map-targets.json");var store=new MapTargets(path);var fixtures=new List<object>();
        void Step(string operation,string kind,int id,long? goal=null) {
            switch(operation){case "toggle":store.Toggle(kind,id);break;case "remove":store.Remove(kind,id);break;case "goal":store.SetGoal(kind,id,goal);break;}
            fixtures.Add(new {Operation=operation,Kind=kind,Id=id,Goal=goal,Expected=store.Snapshot()});
        }
        Step("toggle","npc",2600094);Step("toggle","gatherable",2000001);Step("goal","gatherable",2000001,50);
        Step("toggle","pet",1002);Step("toggle","item",123);Step("toggle","collectable",1010);Step("toggle","collectable",1110);Step("toggle","collectable",1);
        check(store.Snapshot().Count(t=>t.Kind=="collectable")==1 && store.Snapshot()[0] is {Kind:"collectable",Id:1},
            "Tracking global traces removes per-map trace entries and adds at the front");
        Step("toggle","collectable",1010);check(store.Snapshot().Count(t=>t.Kind=="collectable")==2,
            "Adding a per-map trace target preserves global trace selection as in the reference");
        Step("toggle","npc",2600094);Step("goal","pet",1002,0);Step("remove","item",123);Step("goal","gatherable",2000001,null);
        File.WriteAllText(Path.Combine(temp,"reference-map-target-state.json"),JsonSerializer.Serialize(fixtures));
        check(new MapTargets(path).Snapshot().SequenceEqual(store.Snapshot()),"Tracked target ordering and goals persist across restart");
        var bounded=new MapTargets();for(int id=1;id<=512;id++)bounded.Toggle("npc",id);bool limited=false;
        try{bounded.Toggle("npc",513);}catch(InvalidDataException){limited=true;}
        check(limited&&bounded.Snapshot().Length==512,"Tracked target retention is bounded and failed additions leave existing entries intact");
        File.WriteAllText(path,"broken");var broken=new MapTargets(path);check(broken.LoadError!=null&&File.ReadAllText(path)=="broken","Invalid target files are retained without fabricating targets");
    }
}
