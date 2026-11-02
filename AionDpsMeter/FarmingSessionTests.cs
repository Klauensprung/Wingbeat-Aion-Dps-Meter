using System.IO;
using System.Text.Json;
namespace AionDpsMeter.Local;
internal static class FarmingSessionTests
{
    internal static void Run(Action<bool,string> check,string temp) {
        var fixtures=new List<object>();
        FarmingSessionSummary Case(long now,long? since,Dictionary<int,long> kills,Dictionary<int,long> souls) {
            var result=FarmingSession.Build(since.HasValue?new(since.Value,kills,0):null,since.HasValue?new(since.Value,souls,0):null,now);
            fixtures.Add(new {Now=now,Since=since,Kills=kills,Souls=souls,Expected=new {Since=result.KillsSince,result.Kills,PerHour=result.KillsPerHour,result.Souls}});return result;
        }
        check(Case(100000,null,[],[]) is {Kills:0,KillsPerHour:0,Souls:0,KillsSince:null},"Missing farming session has zero totals without a fabricated start time");
        var kills=new Dictionary<int,long>{{100,2},{200,3}};var souls=new Dictionary<int,long>{{1002,4},{1005,2}};
        check(Case(100000,100000,kills,souls) is {Kills:5,KillsPerHour:300,Souls:6},"Farming totals sum all IDs and rate uses a one-minute minimum duration");
        check(Case(159999,100000,kills,souls).KillsPerHour==300&&Case(160000,100000,kills,souls).KillsPerHour==300,
            "Farming rate stays clamped through exactly one minute");
        check(Case(3700000,100000,kills,souls).KillsPerHour==5&&Case(7300000,100000,kills,souls).KillsPerHour==3,
            "Hourly farming rate uses elapsed wall time and rounds positive half values upward");
        check(Case(99999,100000,kills,souls).KillsPerHour==300,"Clock rollback cannot create negative or unbounded farming rates");
        Case(160001,100000,kills,souls);Case(100000000,100000,kills,souls);Case(100001,100000,[],[]);
        File.WriteAllText(Path.Combine(temp,"reference-farming-session.json"),JsonSerializer.Serialize(fixtures));
        var summary=FarmingSession.Build(new(1,new(){{100,long.MaxValue},{200,long.MaxValue}},1),new(2,new(){{1002,7}},0),1);
        check(summary.Kills==2m*long.MaxValue&&summary.KillsPerHour==120m*long.MaxValue&&summary.HasOmissions&&summary.SoulsSince==2,
            "Farming summary avoids integer overflow, preserves separate reset times and exposes omitted quantities");
    }
}
