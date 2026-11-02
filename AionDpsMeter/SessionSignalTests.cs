using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class SessionSignalTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        byte[] Talk(bool start, ushort result) { var w = new PartyWireTests.Writer(); w.Raw(BitConverter.GetBytes(result)); w.Int(123); if (start) w.Long(1793470000000); return w.Bytes.ToArray(); }
        byte[] Scene(byte type) { var w = new PartyWireTests.Writer(); w.Raw(0, type); w.Text("Krao cave intro"); w.Text("volume"); w.Long(12345678901); w.Int(2); w.Var(2); w.Int(1); w.Int(2); return w.Bytes.ToArray(); }
        byte[] Finish(ushort result) { var w = new PartyWireTests.Writer(); w.Raw(BitConverter.GetBytes(result)); w.Text("Krao cave intro"); w.Long(12345678901); return w.Bytes.ToArray(); }
        byte[] Controls(byte mode) {
            var w = new PartyWireTests.Writer(); w.Bool(true); w.Bool(false); w.Int(30); w.Bool(true); w.Raw(1); w.Bool(false); w.Bool(true); w.Raw(2);
            for (int i = 0; i < 7; i++) w.Bool(i % 2 == 0); w.Raw(mode); return w.Bytes.ToArray();
        }
        CapturedPayload Packet(ushort opcode, byte[] body) { var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw((byte)(opcode >> 8), (byte)opcode); w.Raw(body); return new CapturedPayload(13328, 50000, w.Bytes.ToArray()) { TimestampMs = 100 }; }
        DecodeResult Decode(ushort opcode, byte[] body) => new EuGameDecoder().Decode(Packet(opcode, body));
        using var fixtures = new ReferencePacketFixtures(Path.Combine(temp, "reference-session-signals-synthetic.jsonl"));
        foreach (var (opcode, body, emitted) in new[] {
            ((ushort)0x41e2, Talk(true,0),true), ((ushort)0x41e2,Talk(true,1),false),
            ((ushort)0x43e2,Talk(false,0),true), ((ushort)0x43e2,Talk(false,1),true),
            ((ushort)0x158d,Scene(9),true), ((ushort)0x158d,Scene(8),false),
            ((ushort)0x35e3,Finish(0),true), ((ushort)0x35e3,Finish(1),true),
            ((ushort)0x448d,Controls(1),true), ((ushort)0x448d,Controls(2),true), ((ushort)0x448d,Controls(0),false), ((ushort)0x448d,Controls(255),false) }) {
            fixtures.Add(Packet(opcode,body));
            check(Decode(opcode,body).Events.Count == (emitted ? 1 : 0), "Session signal result/type gating " + opcode + "/" + emitted);
            check(Enumerable.Range(0,body.Length).All(n => Decode(opcode,body[..n]).Events.Count == 0) && Decode(opcode,[..body,0]).Events.Count == 0,
                "Session signal rejects every truncation and trailer " + opcode + "/" + emitted);
        }
        var tracker = new SessionSignalTracker();
        void Apply(string kind, long at, object data) => tracker.Apply(new(kind, at, "test", JsonSerializer.SerializeToElement(data)),1);
        Apply("cutscene",100,new CutsceneReading("Intro",true)); Apply("skill-use",101,new { ActorId=2 });
        check(tracker.InCutscene,"Another player's cast cannot dismiss the local cutscene");
        Apply("skill-use",102,new { ActorId=1 }); Apply("cutscene",101,new CutsceneReading("Intro",true));
        check(!tracker.InCutscene,"Local skill use ends the cutscene and stale starts cannot reactivate it");
        Apply("controls",100,new ControlModeReading("classic")); Apply("npc-talk",101,new NpcTalkReading(123,true));
        tracker.Clear(200); Apply("controls",199,new ControlModeReading("action"));
        check(tracker.ControlMode==null && tracker.Conversation==null && !tracker.InCutscene,"Login reset clears bounded session state and rejects pre-login settings");
        var engine = new CombatEngine(); engine.Apply(new("local-player",Actor:new(1,"Local",true,true,0,"Cleric",2301)));
        foreach(var e in Decode(0x158d,Scene(9)).Events) engine.Apply(e);
        engine.NewFight("Manual reset");
        check(engine.SessionSignals.InCutscene && engine.Fight.Network.Count==0 && engine.Loot.Rows().Length==0,
            "Session state survives fight reset without packet history or invented loot");
        foreach(var e in Decode(0x35e3,Finish(1)).Events) engine.Apply(e);
        check(!engine.SessionSignals.InCutscene,"Even unsuccessful cutscene completion clears playback, matching reference normalization");
    }
}
