using System.IO;
using System.Text.Json;
namespace AionDpsMeter.Local;
internal static class MapTargetLocationsTests
{
    internal static void Run(Action<bool,string> check,string temp)
    {
        var groups=MapMarkers.Default.Groups;var index=new MapTargetIndex(groups);var pets=PetFarmLinks.Default;
        var npc=MapTargetLocations.Resolve(new("npc",2600094),index,pets);
        check(npc.Length>0&&npc.All(g=>g.DatabaseId==2600094)&&npc.Where(g=>g.World==20).Sum(g=>g.Count)==1,
            "Target map resolves one NPC variant without other identically named bosses");
        var pet=pets.Find(1002)!;var resolved=MapTargetLocations.Resolve(new("pet",pet.Id),index,pets);
        check(resolved.Length>0&&resolved.All(g=>g.DatabaseType=="npc"&&pet.Npcs.Contains(g.DatabaseId))&&resolved.Select(g=>g.Id).Distinct().Count()==resolved.Length,
            "Pet map targets combine linked NPC locations without duplicate marker groups");
        var gathered=MapTargetLocations.Resolve(new("gatherable",1081004),index,pets);
        check(gathered.Select(g=>g.ObjectId).Distinct().Order().SequenceEqual([1500043,1500061]),"Gathering target maps retain both packet object variants for one database resource");
        var worlds=MapTargetLocations.Worlds(resolved,MapWorlds.Names);var available=worlds.Where(w=>w.CanOpen).ToArray();
        check(available.Length>0&&MapTargetLocations.SelectWorld(worlds,available[^1].World)==available[^1].World&&MapTargetLocations.SelectWorld(worlds,int.MaxValue)==available[0].World,
            "Target map keeps a current matching world or selects the supported world with most locations");
        check(MapTargetLocations.Resolve(new("item",123),index,pets).Length==0&&MapTargetLocations.SelectWorld([new(999,"Unknown",4,false)],999)==null,
            "Missing item source links and unsupported map areas do not invent navigable locations");
        var visible=MapMarkers.Visible(groups.Where(g=>g.World==20),new HashSet<string>(),"",null,npc.Select(g=>g.Id).ToHashSet());
        check(visible.All(g=>g.DatabaseId==2600094),"Target-focused visibility uses IDs instead of expanding same-name search groups");
        var targets=new[]{new MapTarget("npc",2600094),new("gatherable",1081004)}.Concat(pets.Pets.Where(p=>p.Npcs.Length>0).Take(12).Select(p=>new MapTarget("pet",p.Id))).ToArray();
        File.WriteAllText(Path.Combine(temp,"reference-target-locations.json"),JsonSerializer.Serialize(new {Groups=groups,Pets=pets.Pets,Cases=targets.Select(t=>new {Target=t,Expected=MapTargetLocations.Resolve(t,index,pets).Select(g=>g.Id).Order().ToArray()})}));
    }
}
