using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class DutyMissionTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        byte[] Body(bool optional) {
            var w = new PartyWireTests.Writer(); w.Int(0); w.Int(3); w.Var(1);
            w.Int(12); w.Int(34); w.Long(56); w.Raw(2); w.Var(1); w.Int(100); w.Var(5); w.Raw(0); w.Long(10000);
            w.Long(100000); w.Raw(optional ? (byte)1 : (byte)0); if (optional) w.Int(10); w.Long(90000); return w.Bytes.ToArray();
        }
        byte[] Frame(byte[] body) { var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw(0x1c, 0x8d); w.Raw(body); return w.Bytes.ToArray(); }
        DecodeResult Decode(byte[] body) => new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, Frame(body)) { TimestampMs = 20 });
        using var fixtures = new ReferencePacketFixtures(Path.Combine(temp, "reference-duty-missions-synthetic.jsonl"));
        foreach (bool optional in new[] { false, true }) {
            byte[] body = Body(optional); var decoded = Decode(body);
            check(decoded.Events.Single().Network!.Fields.Deserialize<DutyMissionReading>() == new DutyMissionReading(3, 100000), "Duty mission packet retains rewarded count/reset with optional field " + optional);
            check(Enumerable.Range(0, body.Length).All(n => Decode(body[..n]).Events.Count == 0) && Decode([..body, 0]).Events.Count == 0,
                "Duty mission parser rejects every truncated packet and unknown trailer " + optional);
            fixtures.Add(new CapturedPayload(13328, 50000, Frame(body)) { TimestampMs = 20 });
        }
        DecodedCombatEvent Update(long at, int count, long reset) => new("network", Network: new("duty-missions", at, "1C8D", JsonSerializer.SerializeToElement(new DutyMissionReading(count, reset))));
        DecodedCombatEvent Prepare(long at) => new("network", Network: new("local-zone", at, "2136", JsonSerializer.SerializeToElement(new { MapId = 1010, AwaitingIdentity = true })));
        DecodedCombatEvent Player(int server, string name) => new("local-player", Actor: new(1, name, true, true, 0, "Cleric", server));
        var ledger = new DutyMissionLedger(Path.Combine(temp, "duty-missions.json")); var engine = new CombatEngine(dutyMissions: ledger);
        engine.Apply(Update(20, 3, 100000)); engine.Apply(Prepare(30));
        check(ledger.Snapshot().Length == 0 && engine.Fight.Network.All(n => n.Kind != "duty-missions"), "Pre-identity duty missions wait without raw retention");
        engine.Apply(Player(1, "First"));
        check(ledger.Snapshot().Single() is { Server: 1, Rewarded: 3 }, "Initial map preparation preserves pending duty counters until explicit identity");
        engine.Apply(Update(40, 4, 100000)); engine.Apply(Update(35, 1, 100000)); engine.NewFight("Reset");
        check(ledger.Snapshot().Single().Rewarded == 4, "Duty counters survive new fight and reject older updates");
        engine.Apply(Prepare(50)); engine.Apply(Update(60, 2, 200000)); engine.Apply(Player(2, "Second"));
        check(ledger.Snapshot().Select(r => (r.Server, r.Rewarded)).SequenceEqual(new[] { (1, 4), (2, 2) }), "Transition duty counts bind to the new server without changing the previous server");
        engine.Apply(Update(70, 9, 70));
        check(ledger.Snapshot().Single(r => r.Server == 2).Rewarded == 2, "Expired duty mission periods do not overwrite current counters");
        ledger.Save(); var restored = new DutyMissionLedger(Path.Combine(temp, "duty-missions.json"));
        check(restored.Snapshot().SequenceEqual(ledger.Snapshot()), "Server duty counters persist independently of fight archives");
        engine.Apply(Prepare(80)); engine.Apply(Update(90, 1, 200000)); engine.Apply(Prepare(100)); engine.Apply(Player(3, "Third"));
        check(ledger.Snapshot().All(r => r.Server != 3), "A newer preparation discards superseded pending mission counters");
        var reused = new CombatEngine(dutyMissions: restored); reused.Apply(Player(1, "Other character")); reused.Apply(Update(110, 5, 100000));
        check(restored.Snapshot().Single(r => r.Server == 1).Rewarded == 5, "Characters on the same server share one rewarded mission counter");
        var many = new DutyMissionLedger(); for (int i = 1; i <= 200; i++) many.Apply(i, new(1, 10000), i);
        check(many.Snapshot().Length == 128 && many.Snapshot()[0].Server == 73, "Duty ledger bounds retained servers and evicts oldest readings");
    }
}
