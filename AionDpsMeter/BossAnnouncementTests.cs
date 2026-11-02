using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class BossAnnouncementTests
{
    internal static void Run(Action<bool,string> check,string temp)
    {
        byte[] Body(int id,params string[] values){var w=new PartyWireTests.Writer();w.Int(id);w.Var(values.Length);foreach(string value in values)w.Text(value);return w.Bytes.ToArray();}
        CapturedPayload Packet(byte[] body){var w=new PartyWireTests.Writer();w.Var(body.Length+6);w.Raw(0x46,0x8d);w.Raw(body);return new CapturedPayload(13328,50000,w.Bytes.ToArray()){TimestampMs=100};}
        DecodeResult Decode(byte[] body)=>new EuGameDecoder().Decode(Packet(body));
        using var fixtures=new ReferencePacketFixtures(Path.Combine(temp,"reference-boss-announcements.jsonl"));
        foreach(var body in new[]{Body(139,"Light","Winner:2301","12345"),Body(139,"Dark","Winner","54321"),Body(139,"Other","Name:with:colon:2302","bad"),Body(139,"Light","Winner:bad","0x10"),Body(139),Body(139,"Light","",""),Body(139,"Dark","Name:0","1e3"),Body(138,"Light","Winner:2301","12345")})fixtures.Add(Packet(body));
        var bytes=Body(139,"Light","Winner:2301","12345");
        check(Decode(bytes).Events.Single().Network!.Fields.Deserialize<BossAnnouncement>() is {Npc:12345,Race:"light",Name:"Winner",Server:2301},"Server boss message parses the faction, name, server suffix and NPC independently");
        check(Decode(Body(138,"Light","Winner:2301","12345")).Events.Count==0,"Other server messages cannot create boss kill announcements");
        check(Enumerable.Range(0,bytes.Length).All(n=>Decode(bytes[..n]).Events.Count==0) && Decode([..bytes,0]).Events.Count==0,"Server messages reject every truncation and trailer before publication");
        var tracker=new ArtifactSiegeTracker();
        void Apply(string kind,long at,object data)=>tracker.Apply(new(kind,at,"test",JsonSerializer.SerializeToElement(data)));
        Apply("boss-kill",100,new BossAnnouncement(1,"light","Earlier",2301));
        Apply("artifact-captured",1000,new ArtifactCapture(1001,"light",1500));
        Apply("artifact-captured",1001,new ArtifactCapture(1002,"dark",1501));
        Apply("boss-kill",6000,new BossAnnouncement(1,"light","Winner",2301));
        check(tracker.Captors().Single() is {Artifact:1001,Name:"Winner",Server:2301},"Captor correlation uses the five-second boundary and matching faction without retroactive announcements");
        Apply("boss-kill",6002,new BossAnnouncement(1,"dark","Too late",2302));
        check(tracker.Captors().Length==1,"Boss announcements outside five seconds cannot name a captor");
        Apply("artifact-siege",7000,new SiegeReading([], [new(1001,"light")]));
        check(tracker.Captors().Single().Name=="Winner","Same-owner snapshots preserve the last correlated captor");
        Apply("artifact-captured",8000,new ArtifactCapture(1001,"dark",8500));
        check(tracker.Captors().Length==0,"A new owner cannot inherit the previous owner's captor name");
        Apply("boss-kill",8001,new BossAnnouncement(1,"dark","New winner",2302));
        Apply("boss-kill",8002,new BossAnnouncement(1,"dark","Duplicate",2302));
        check(tracker.Captors().Single().Name=="New winner","A second announcement cannot overwrite an already linked capture");
        tracker.Clear();check(tracker.Captors().Length==0,"Server/login clearing drops captor correlations");
        var engine=new CombatEngine();foreach(var e in Decode(bytes).Events)engine.Apply(e);
        check(engine.Fight.Network.Count==0 && engine.Loot.Rows().Length==0 && engine.Fight.DungeonRun==null,"Server-wide boss announcements do not invent local dungeon kills, loot or raw fight history");
    }
}
