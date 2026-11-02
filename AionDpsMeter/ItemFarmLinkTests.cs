using System.IO;
using System.Text.Json;
namespace AionDpsMeter.Local;
internal static class ItemFarmLinkTests
{
    internal static void Run(Action<bool,string> check,string temp)
    {
        var catalogue=ItemFarmLinks.Default;var groups=MapMarkers.Default.Groups;var index=new MapTargetIndex(groups);var pets=PetFarmLinks.Default;
        check(catalogue.LoadError==null&&catalogue.Count==1112,"Compact catalogue loads verified source links for 1112 items");
        var shared=catalogue.Items.GroupBy(i=>i.Npcs).Any(g=>g.Count()>1&&g.Key.Length>0);
        check(shared,"Items sharing NPC sources reuse one immutable lookup array");
        var cases=catalogue.Items.OrderBy(i=>i.Id).Select(i=>new {Target=new MapTarget("item",i.Id),Expected=MapTargetLocations.Resolve(new("item",i.Id),index,pets).Select(g=>g.Id).Order().ToArray()}).ToArray();
        check(cases.Any(c=>c.Expected.Length>0)&&cases.All(c=>c.Expected.Distinct().Count()==c.Expected.Length),"Known item sources resolve map locations without duplicated groups");
        File.WriteAllText(Path.Combine(temp,"reference-item-locations.json"),JsonSerializer.Serialize(new {Groups=groups,Pets=pets.Pets,Cases=cases}));
        var nearbyCases=new List<object>();
        foreach(var item in catalogue.Items.Where(i=>i.Gatherables.Length>0).Concat(catalogue.Items.Where(i=>i.Npcs.Length>0).Take(20))) {
            var target=new MapTarget("item",item.Id);var resolved=MapTargetLocations.Resolve(target,index,pets);
            foreach(int world in new[]{0,resolved.FirstOrDefault()?.World??1010}) {
                var nearby=new FarmingNearbySnapshot(world,groups.Where(g=>g.DatabaseType=="npc").Select(g=>g.DatabaseId).Distinct().ToDictionary(id=>id,id=>(long)(id%7+1)),
                    groups.Where(g=>g.ObjectId>0).Select(g=>g.ObjectId).Distinct().ToDictionary(id=>id,id=>(long)(id%5+1)));
                nearbyCases.Add(new {Target=target,World=world,nearby.Npcs,nearby.Objects,Expected=FarmingNearby.Count(target,index,pets,nearby)});
            }
        }
        File.WriteAllText(Path.Combine(temp,"reference-item-nearby.json"),JsonSerializer.Serialize(new {Groups=groups,Pets=pets.Pets,Cases=nearbyCases}));
        var linked=catalogue.Items.First(i=>i.Npcs.Any(n=>pets.OfNpc(n).Length>0));
        var progress=FarmTargetProgressBuilder.Build(new("item",linked.Id,10),index,pets,new Dictionary<int,long>(),new Dictionary<int,long>(),new Dictionary<int,long>{{linked.Id,7}},new Dictionary<int,long>(),null);
        check(progress.Count==7&&progress.Pets.Length>0&&progress.Done==false,"Item progress keeps loot quantities while exposing linked pet progress");
        check(FarmingNearby.Count(new("item",int.MaxValue),index,pets,new(1010,new(),new()))==null,"Unknown item source remains unknown rather than reporting zero");
    }
}
