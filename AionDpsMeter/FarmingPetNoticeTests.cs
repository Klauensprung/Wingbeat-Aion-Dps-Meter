using System.IO;
using System.Text.Json;
namespace AionDpsMeter.Local;
internal static class FarmingPetNoticeTests
{
    internal static void Run(Action<bool,string> check,string temp)
    {
        var tracker=new FarmingPetNotices();var fixtures=new List<object>();
        var registered=new Dictionary<int,long>();var upgrades=new Dictionary<int,long>();var levels=new Dictionary<int,int>();
        long since=1;
        FarmingNotice[] Step() {
            var roster=new PetCollectionSnapshot(1000,0,true,levels.Select(p=>new PetProgressState(p.Key,p.Value,0,0)).ToArray());
            var notices=tracker.Update(new(since,new(registered),0),new(since,new(upgrades),0),roster,since,id=>id==9999?null:$"Pet {id}");
            fixtures.Add(new {Since=since,Registered=new Dictionary<int,long>(registered),Upgrades=new Dictionary<int,long>(upgrades),Levels=new Dictionary<int,int>(levels),Expected=notices});return notices;
        }
        for(int id=1000;id<1004;id++)levels[id]=1;
        check(Step().Length==0,"Pet farming watcher does not announce an existing roster");
        registered[1004]=1;levels[1004]=1;
        check(Step().Select(n=>n.Kind).SequenceEqual(["registered","milestone"]),"Fifth pet announces registration and collection milestone");
        upgrades[1004]=2;levels[1004]=3;
        check(Step() is [{Kind:"levelup",Count:3}]&&Step().Length==0,"Batched pet upgrades announce current level once");
        since++;registered.Clear();upgrades.Clear();check(Step().Length==0,"Farming reset suppresses old pet announcements");
        // Cross every supported milestone, including simultaneous registrations and an unknown catalogue entry.
        for(int id=1005;id<1501;id++) {
            levels[id]=1;registered[id]=1;
            if(id%7==0)Step();
        }
        Step();registered[9999]=1;levels[9999]=1;
        check(Step().All(n=>n.Kind!="registered"),"Unknown catalogue pets do not generate unnamed registration notices");
        upgrades[1800]=1;
        check(Step() is [{Kind:"levelup",Count:1}],"Pet farming notice uses reference fallback when current level is absent");
        File.WriteAllText(Path.Combine(temp,"reference-farming-pet-notices.json"),JsonSerializer.Serialize(fixtures));
        check(FarmingTargetView.Notice(new("milestone","collection:50","50 pets collected",50,null)).Contains("10 candy"),"Collection milestone text identifies the reward without claiming it was received");
    }
}
