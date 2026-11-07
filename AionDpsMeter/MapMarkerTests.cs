using System.IO;
using System.Text.Json;
namespace AionDpsMeter.Local;
internal static class MapMarkerTests
{
    internal static void Run(Action<bool,string> check,string temp)
    {
        var catalogue=MapMarkers.Default;
        foreach(int cubeWorld in new[]{1010,1011,1110,1111}){
            var cubeGroups=GatheringRouteSources.Groups(catalogue,cubeWorld,GatheringRouteSources.HiddenCubes);
            var cubeIds=cubeGroups.Select(g=>g.Id).ToHashSet();
            var cubePoints=catalogue.LoadWorld(cubeWorld).Where(p=>cubeIds.Contains(p.Group)).Select(p=>new PetSpawnPoint(p.Group+1,p.X,p.Y,0)).ToArray();
            var cubeRoute=FarmingRoute.Build(cubePoints,new(cubeWorld,0,0,0,1000),32);
            check(cubeGroups.Length>0&&cubeGroups.All(g=>g.DatabaseId==0&&g.Category=="hidden-cube")&&cubeRoute.Length==32,
                $"Hidden cube locations produce a route on world {cubeWorld} without inventing inventory item IDs");
        }
        check(GatheringRouteSources.Groups(catalogue,999999,GatheringRouteSources.HiddenCubes).Length==0,
            "Hidden cube route on an unsupported teleport destination has no old-map stops");
        check(catalogue.LoadError==null && catalogue.Groups.Length==11279 && catalogue.Groups.Sum(g=>g.Count)==88256,
            "Compact marker catalogue retains all 88256 reference locations and categories");
        int total=0;MapMarkerPoint? sample=null;
        foreach(int world in catalogue.Groups.Select(g=>g.World).Distinct()) {
            var points=catalogue.LoadWorld(world);total+=points.Length;
            var group=catalogue.Groups.FirstOrDefault(g=>g.World==world && g.Name=="Guardian Lord Nahma" && g.DatabaseId==2600094);
            if(group!=null)sample=points.First(p=>p.Group==group.Id);
        }
        check(total==88256 && catalogue.LoadWorld(int.MaxValue).Length==0,"All per-world coordinate blocks validate and unknown worlds have no markers");
        check(sample is { X: 20234.56640625, Y: -41188.890625 },"Marker import reverses reference map Y into world coordinates without rounding");
        string path=Path.Combine(temp,"map-pins.json");var pins=new MapPins(path);var fixtures=new List<object>();
        MapMarkerGroup[] groups=[new(1,1010,"Ore","gatherable","ore",1),new(2,1010,"Wolf","monster","normal",1),
            new(3,1010,"Wolf","monster","named",1),new(4,1110,"Ore","gatherable","ore",1)];
        void Step(int world,string operation,string name,string query,string? category) {
            if(operation=="toggle")pins.Toggle(world,name);if(operation=="clear")pins.Clear(world);
            var saved=pins.ForWorld(world);var visible=MapMarkers.Visible(groups.Where(g=>g.World==world),saved,query,category);
            fixtures.Add(new {World=world,Operation=operation,Name=name,Query=query,Category=category,Groups=groups,
                Expected=new { Pins=saved.Order().ToArray(),Visible=visible.Select(g=>g.Id).ToArray() }});
        }
        Step(1010,"none","","","normal");Step(1010,"toggle","Ore","","normal");
        check(MapMarkers.Visible(groups.Where(g=>g.World==1010),pins.ForWorld(1010),"Wolf","normal").Length==3,
            "Pinned names remain visible during search; matching names include all category variants");
        Step(1010,"none","","Wolf","normal");Step(1010,"none","","missing","normal");Step(1110,"none","","","normal");
        Step(1110,"toggle","Ore","","normal");Step(1010,"clear","","","normal");
        check(pins.ForWorld(1010).Count==0 && pins.ForWorld(1110).SetEquals(["Ore"]),"Clearing map pins affects only the selected world");
        pins=new(path);check(pins.LoadError==null && pins.ForWorld(1110).SetEquals(["Ore"]),"Map-name pins persist across restart");
        Step(1110,"toggle","Ore","","normal");Step(1010,"toggle","Wolf","ore","ore");Step(1010,"toggle","Wolf","","normal");
        File.WriteAllText(Path.Combine(temp,"reference-map-pins.json"),JsonSerializer.Serialize(fixtures));
        string widePath=Path.Combine(temp,"wide-map-pins.json");var wide=new MapPins(widePath);bool limited=false;
        for(int i=0;i<600;i++) {try{wide.Toggle(100+i/500,new string('\u4e00',295)+i);}catch(InvalidOperationException){limited=true;break;}}
        check(limited && new FileInfo(widePath).Length<=1024*1024 && new MapPins(widePath).LoadError==null,
            "Encoded pin storage stays within its reload limit even with long Unicode names");
        File.WriteAllText(path,"broken");var broken=new MapPins(path);check(broken.LoadError!=null && File.ReadAllText(path)=="broken","Invalid pin files remain intact until a deliberate change");
        string blocked=Path.Combine(temp,"blocked-map-pins");Directory.CreateDirectory(blocked);
        // Atomic writes commit the in-memory state only after a successful file replacement.
        var blockedPins=new MapPins(blocked);bool threw=false;try{blockedPins.Toggle(1010,"Ore");}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){threw=true;}
        check(threw && blockedPins.ForWorld(1010).Count==0 && !File.Exists(blocked+".tmp"),"Failed pin saves do not claim success or leave temporary files");
    }
}
