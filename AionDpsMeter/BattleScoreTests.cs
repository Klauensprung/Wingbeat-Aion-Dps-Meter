using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class BattleScoreTests
{
    internal static void Run(Action<bool,string> check,string temp)
    {
        const long wide = 650000000000000001;
        byte[] Body(int count) {
            var w=new PartyWireTests.Writer();w.Var(count);
            for(int i=0;i<count;i++){w.Long(wide+i);w.Bool(i%2==0);w.Bool(i%3==0);w.Raw((byte)(i%256),(byte)(i%5),(byte)(i%7),0);}
            return w.Bytes.ToArray();
        }
        CapturedPayload Packet(byte[] body){var w=new PartyWireTests.Writer();w.Var(body.Length+6);w.Raw(0x26,0xe3);w.Raw(body);return new CapturedPayload(13328,50000,w.Bytes.ToArray()){TimestampMs=100};}
        DecodeResult Decode(byte[] body)=>new EuGameDecoder().Decode(Packet(body));
        using var fixtures=new ReferencePacketFixtures(Path.Combine(temp,"reference-scoreboard-synthetic.jsonl"));
        foreach(int count in new[]{0,1,5,4096}){var body=Body(count);fixtures.Add(Packet(body));check(Decode(body).Events.Single().Network!.Fields.Deserialize<BattleScores>()!.Scores.Length==count,"Battle scoreboard parses shared packed flags and full identities for count "+count);}
        var sample=Body(5);
        check(Enumerable.Range(0,sample.Length).All(n=>Decode(sample[..n]).Events.Count==0) && Decode([..sample,0]).Events.Count==0 && Decode(Body(4097)).Events.Count==0,
            "Scoreboard rejects truncation, trailers and over-limit lists before publishing");
        var tracker=new BattleScoreTracker();long at=100;
        using var states=new StreamWriter(Path.Combine(temp,"reference-scoreboard-state.jsonl"));
        void Apply(params BattleScore[] scores){var batch=new BattleScores(scores);tracker.Apply(new("scoreboard",at++,"test",JsonSerializer.SerializeToElement(batch)));states.WriteLine(JsonSerializer.Serialize(new{Kind="scoreboard",Data=batch,Expected=tracker.Snapshot()}));}
        Apply(new BattleScore(wide,5,2,3),new(wide+1,5,1,3),new(wide+2,6,9,0));
        check(tracker.Snapshot().Select(s=>s.Id).SequenceEqual(new[]{wide+2,wide+1,wide}),"Game scoreboard sorts by kills, assists and fewest deaths");
        Apply();Apply(new BattleScore(wide,0,0,0));Apply(new BattleScore(wide+1,1,2,3),new(wide+1,2,3,4));
        check(tracker.Snapshot().Length==3 && tracker.Snapshot().Single(s=>s.Id==wide).Kills==0 && tracker.Snapshot().Single(s=>s.Id==wide+1).Kills==2,
            "Scoreboard deltas replace absolute counts, retain absent players, and honor the final duplicate entry");
        var actor=new Actor(1,"Known player",true,true,0,"Cleric",2301){PersistentId=wide};
        check(tracker.Rows([actor]).Single(r=>r.CharacterId==wide.ToString()).Name=="Known player" && tracker.Rows([actor]).Count(r=>r.Name=="Not identified")==2,
            "Scoreboard names require full captured identities, leaving other IDs unguessed");
        check(tracker.Rows([actor,actor with{Id=2}]).Single(r=>r.CharacterId==wide.ToString()).Name=="Not identified",
            "Ambiguous entity matches cannot assign an arbitrary scoreboard name");
        tracker.Clear(at);states.WriteLine(JsonSerializer.Serialize(new{Kind="map",Data=new{},Expected=tracker.Snapshot()}));
        tracker.Apply(new("scoreboard",at-1,"test",JsonSerializer.SerializeToElement(new BattleScores([new(wide,99,0,0)]))));
        check(tracker.Snapshot().Length==0,"Map boundary clears scores and rejects delayed previous-map updates");
        Apply(new BattleScore(wide,1,0,0));
        var bounded=new BattleScoreTracker();bounded.Apply(new("scoreboard",100,"test",JsonSerializer.SerializeToElement(new BattleScores(Enumerable.Range(1,3000).Select(i=>new BattleScore(i,1,0,0)).ToArray()))));
        check(bounded.Snapshot().Length==2048,"Scoreboard state is bounded across repeated partial batches");
        var engine=new CombatEngine();foreach(var e in Decode(sample).Events)engine.Apply(e);engine.NewFight("Manual reset");
        check(engine.Scoreboard.Snapshot().Length==5 && engine.Fight.Network.Count==0 && engine.Loot.Rows().Length==0,"Fight reset preserves game scores without raw events or loot changes");
        engine.Apply(new("network",Network:new("local-zone",200,"test",JsonSerializer.SerializeToElement(new{MapId=1010,X=0,Y=0,Z=0}))));
        check(engine.Scoreboard.Snapshot().Length==0,"Production map events clear the battle scoreboard");
    }
}
