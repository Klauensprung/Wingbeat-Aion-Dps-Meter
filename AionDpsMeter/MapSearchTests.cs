using System.IO;
using System.Text.Json;
namespace AionDpsMeter.Local;
internal static class MapSearchTests
{
    internal static void Run(Action<bool,string> check,string temp)
    {
        var fixtures=new List<object>();
        MapSearchResult Run(MapMarkerGroup[] groups,IReadOnlyDictionary<int,string> worlds,string query,int limit=50) {
            var result=new MapSearch(groups,worlds).Find(query,limit);
            fixtures.Add(new {Groups=groups.ToArray(),Worlds=worlds,Query=query,Limit=limit,Expected=result});return result;
        }
        var worlds=new Dictionary<int,string>{{1010,"Verteron"},{1110,"Altgard"}};
        MapMarkerGroup[] groups=[new(1,1010,"Wolf","monster","named",3),new(2,1010,"Wolf","monster","normal",4),new(3,1110,"Wolf","monster","normal",2),
            new(4,1010,"Named Wolf","monster","normal",1),new(5,1010,"Basilisk","monster","wolf",6),new(6,999,"Wolf","monster","normal",1)];
        check(Run(groups,worlds,"wo").Groups.Length==0 && Run(groups,worlds,"  ").Groups.Length==0,"Map search requires three trimmed English characters");
        var result=Run(groups,worlds," WOLF ");
        check(result.TotalGroups==4 && result.Groups.Select(g=>g.Name).SequenceEqual(["Named Wolf","Wolf","Wolf","Basilisk"]),
            "Global marker search ranks name matches before category matches and excludes unconfigured worlds");
        check(result.Groups[1].World==1110 && result.Groups[2].World==1010 && result.Groups[2].Count==7 && result.Groups[2].GroupIds.SequenceEqual([1,2]),
            "Same-name markers group by world across categories and sort ties by world name");
        Run(groups,worlds,"named");Run(groups,worlds,"missing");Run(groups,worlds,"wolf",2);
        var many=Enumerable.Range(0,120).Select(i=>new MapMarkerGroup(i,1010,$"Wolf {i:D3}","monster","normal",1)).ToArray();
        var first=Run(many,worlds,"wolf");var more=Run(many,worlds,"wolf",100);
        check(first.Groups.Length==50 && first.IsLimited && more.Groups.Length==100 && more.TotalGroups==120,"Map search expands in groups of 50 without loading coordinates");
        MapMarkerGroup[] large=[new(1,1010,"Wolf A","monster","normal",6000),new(2,1010,"Wolf B","monster","normal",5000),new(3,1010,"Wolf C","monster","normal",1)];
        result=Run(large,worlds,"wolf");
        check(result.Groups.Length==1 && result.TotalGroups==3 && result.IsLimited,"Search stops before exceeding 10000 markers without skipping ahead to smaller groups");
        large[0]=large[0] with {Count=11000};result=Run(large,worlds,"wolf");
        check(result.Groups.Length==1 && result.Groups[0].Count==11000,"First oversized marker group remains reachable as in the reference search");
        var real=Run(MapMarkers.Default.Groups,MapWorlds.Names,"Guardian Lord Nahma");
        check(MapWorlds.Names.Count==47 && real.Groups.Any(g=>g.World==20 && g.Name=="Guardian Lord Nahma" && g.Count==3),"Configured map search finds the named boss in Chaotic Lower Reshanta");
        foreach(var query in new[]{"wolf","ore","monolith","normal","spirit","aether","vegetable"})Run(MapMarkers.Default.Groups,MapWorlds.Names,query);
        var pins=new HashSet<string>{"Basilisk"};
        var visible=MapMarkers.Visible(groups.Where(g=>g.World==1010),pins,"",null,new HashSet<int>{3});
        check(visible.Select(g=>g.Name).SequenceEqual(["Basilisk"]),"Matches in another map hide unrelated current markers while retaining local pins");
        File.WriteAllText(Path.Combine(temp,"reference-map-search.json"),JsonSerializer.Serialize(fixtures));
    }
}
