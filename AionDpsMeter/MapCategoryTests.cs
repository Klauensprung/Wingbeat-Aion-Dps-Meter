using System.IO;
using System.Text.Json;
namespace AionDpsMeter.Local;
internal static class MapCategoryTests
{
    internal static void Run(Action<bool,string> check,string temp)
    {
        string path=Path.Combine(temp,"map-categories.json");var filters=new MapCategories(path);var fixtures=new List<object>();
        MapMarkerGroup[] groups=[new(1,1010,"Wolf","monster","normal",1),new(2,1010,"Boss","monster","named",1),new(3,1010,"Ore","gatherable","ore",1),
            new(4,1010,"Vegetable","gatherable","vegetable",1),new(5,1010,"Stone","collectable","monolith",1),new(6,1010,"Cube","collectable","hidden-cube",1),new(7,1110,"Ore","gatherable","ore",1)];
        void Step(int world,string operation,string type="",string category="") {
            filters.Reconcile(world,groups);
            switch(operation){case "toggle":filters.Toggle(world,type,category,groups);break;case "type":filters.ToggleType(world,type,groups);break;
                case "all":filters.SetAll(world,groups,true);break;case "none":filters.SetAll(world,groups,false);break;}
            fixtures.Add(new {World=world,Operation=operation,Type=type,Category=category,Groups=groups.ToArray(),Expected=filters.Snapshot(world)});
        }
        Step(1010,"prepare");check(!filters.Enabled(1010,"monster","normal")&&filters.Enabled(1010,"gatherable","ore")&&filters.Enabled(1010,"collectable","monolith")&&!filters.Enabled(1010,"collectable","hidden-cube"),"Map category defaults enable gathering and monoliths without enabling all monsters or collectables");
        Step(1010,"toggle","monster","named");Step(1010,"toggle","gatherable","vegetable");
        check(filters.Enabled(1010,"monster","named")&&filters.Enabled(1010,"gatherable","ore")&&!filters.Enabled(1010,"gatherable","vegetable"),"Multiple categories can be selected across marker types");
        Step(1110,"none");Step(1010,"prepare");check(filters.Enabled(1010,"gatherable","ore")&&!filters.Enabled(1110,"gatherable","ore"),"Category choices are independent per map");
        filters=new(path);check(filters.LoadError==null&&filters.Enabled(1010,"monster","named")&&!filters.Enabled(1110,"gatherable","ore"),"Per-map category selections persist across restart");
        Step(1010,"type","monster");Step(1010,"type","monster");Step(1010,"none");
        groups=groups.Append(new(8,1010,"Aether","gatherable","aether",1)).Append(new(9,1010,"Elite","monster","elite",1)).ToArray();Step(1010,"prepare");
        check(filters.Enabled(1010,"gatherable","aether")&&!filters.Enabled(1010,"gatherable","ore")&&!filters.Enabled(1010,"monster","elite"),"New categories use type defaults while previously disabled categories remain disabled");
        Step(1010,"all");groups=groups.Where(g=>g.Type!="gatherable"||g.World!=1010).Append(new(10,1010,"Herb","gatherable","herb",1)).ToArray();Step(1010,"prepare");
        check(filters.Enabled(1010,"gatherable","herb"),"A wholly obsolete saved category selection falls back to current defaults");
        Step(1010,"none");Step(1010,"prepare");check(!filters.Enabled(1010,"gatherable","herb"),"Explicit empty category selection survives reconciliation");
        File.WriteAllText(Path.Combine(temp,"reference-map-categories.json"),JsonSerializer.Serialize(fixtures));
        var snapshot=filters.Snapshot(1010);snapshot.Enabled["gatherable"]=["herb"];check(!filters.Enabled(1010,"gatherable","herb"),"Category snapshots cannot mutate stored selections");
        File.WriteAllText(path,"broken");var broken=new MapCategories(path);broken.Reconcile(1010,groups);check(broken.LoadError!=null&&File.ReadAllText(path)=="broken","Reading defaults does not overwrite a corrupt category file");
        string blockedPath=Path.Combine(temp,"blocked-map-categories");Directory.CreateDirectory(blockedPath);
        var blocked=new MapCategories(blockedPath);blocked.Reconcile(1010,groups);bool failed=false;
        try{blocked.Toggle(1010,"gatherable","herb",groups);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){failed=true;}
        check(failed&&blocked.Enabled(1010,"gatherable","herb")&&!File.Exists(blockedPath+".tmp"),"Failed category saves retain previous selection and remove temporary files");
        var pinned=new HashSet<string>{"Wolf"};var visible=MapMarkers.Visible(groups.Where(g=>g.World==1010),pinned,"",null,null,g=>false);
        check(visible is [{Name:"Wolf"}],"Pinned names override an explicitly empty multi-category filter");
    }
}
