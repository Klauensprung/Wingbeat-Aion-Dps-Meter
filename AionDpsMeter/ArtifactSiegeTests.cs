using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class ArtifactSiegeTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        void Layer(PartyWireTests.Writer w, byte state, bool end) { w.Raw(end ? (byte)1 : (byte)0); w.Int(10); w.Raw(state, 1); w.Long(1700000000000); if (end) w.Long(1700001000000); }
        void Artifact(PartyWireTests.Writer w, byte race) { w.Int(100); w.Int(200); w.Raw(race); w.Long(1700001000000); }
        byte[] Body(ushort op, byte state = 2, byte race = 1, bool end = true, ushort result = 0, bool empty = false) {
            var w = new PartyWireTests.Writer();
            if (op == 0x03e3) { Artifact(w, race); return w.Bytes.ToArray(); }
            if (op == 0x02e3) { w.Int(10); w.Long(1700001000000); }
            else if (op == 0x07e3) { w.Raw(BitConverter.GetBytes(result)); w.Var(empty ? 0 : 1); if (!empty) Layer(w, state, end); }
            else Layer(w, state, end);
            if (op != 0x08e3) { w.Var(empty ? 0 : 1); if (!empty) Artifact(w, race); }
            return w.Bytes.ToArray();
        }
        CapturedPayload Packet(ushort op, byte[] body) { var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw((byte)(op >> 8), (byte)op); w.Raw(body); return new CapturedPayload(13328, 50000, w.Bytes.ToArray()) { TimestampMs = 100 }; }
        DecodeResult Decode(ushort op, byte[] body) => new EuGameDecoder().Decode(Packet(op, body));
        using var fixtures = new ReferencePacketFixtures(Path.Combine(temp, "reference-siege-synthetic.jsonl"));
        foreach (ushort op in new ushort[] { 0x02e3, 0x03e3, 0x05e3, 0x07e3, 0x08e3 }) {
            var body = Body(op); fixtures.Add(Packet(op, body));
            check(Decode(op, body).Events.Count == 1 && Enumerable.Range(0, body.Length).All(n => Decode(op, body[..n]).Events.Count == 0) && Decode(op, [..body, 0]).Events.Count == 0,
                "Artifact packet exact reads reject every truncation/trailer " + op);
        }
        foreach (byte state in new byte[] { 0, 1, 2, 3, 9 }) foreach (bool end in new[] { false, true }) fixtures.Add(Packet(0x08e3, Body(0x08e3, state: state, end: end)));
        foreach (byte race in new byte[] { 0, 2, 99 }) fixtures.Add(Packet(0x03e3, Body(0x03e3, race: race)));
        fixtures.Add(Packet(0x07e3, Body(0x07e3, result: 1))); fixtures.Add(Packet(0x07e3, Body(0x07e3, empty: true)));
        check(Decode(0x07e3, Body(0x07e3, result: 1)).Events.Count == 0 && Decode(0x07e3, Body(0x07e3, empty: true)).Events.Count == 1,
            "Failed siege info suppresses updates while successful empty info is emitted");
        var finished = Decode(0x08e3, Body(0x08e3, state: 3)).Events.Single().Network!.Fields.Deserialize<SiegeReading>()!.Layers.Single();
        check(finished is { State: "end", EndsAt: null, EndedAt: 1700000000000, NextAt: 1700001000000 }, "Completed siege separates previous end from next event time");
        var clock = new PartyWireTests.Writer(); clock.Var(14); clock.Raw(0, 0x36); clock.Long(600);
        var decoder = new EuGameDecoder(); decoder.Decode(new CapturedPayload(13328, 50000, clock.Bytes.ToArray()) { TimestampMs = 100 });
        check(decoder.Decode(Packet(0x03e3, Body(0x03e3))).Events.Single().Network!.Fields.Deserialize<ArtifactCapture>()!.Time == 600,
            "Artifact capture time uses the observed server-clock offset");
        var tracker = new ArtifactSiegeTracker();
        void Apply(string kind, long at, object data) => tracker.Apply(new(kind, at, "test", JsonSerializer.SerializeToElement(data)));
        Apply("artifact-siege", 100, new SiegeReading([finished], [new(100, "light")]));
        Apply("artifact-siege", 101, new SiegeReading([], []));
        check(tracker.Layers().Length == 1 && tracker.Artifacts().Single().Owner == "light", "Empty siege snapshots merge without deleting captured ownership or event state");
        Apply("artifact-captured", 102, new ArtifactCapture(100, "dark", 200));
        Apply("artifact-siege", 101, new SiegeReading([], [new(100, "light")]));
        check(tracker.Artifacts().Single().Owner == "dark", "Stale snapshots cannot undo a newer artifact capture");
        Apply("artifact-captured", 103, new ArtifactCapture(100, null, 201));
        check(tracker.Artifacts().Single().Owner == null, "Unrecognized or neutral ownership clears the previous faction without inventing a new owner");
        Apply("abyss-matching", 104, new AbyssMatching([new(2301,"light"),new(2302,"dark")]));
        check(tracker.Artifacts().Length == 1, "Initial server matching preserves already captured siege state");
        Apply("abyss-matching", 104, new AbyssMatching([new(2301,"light"),new(2102,"dark")]));
        check(tracker.Artifacts().Length == 1, "Cross-region server pairs cannot replace the current siege matchup");
        Apply("abyss-matching", 105, new AbyssMatching([new(2303,"light"),new(2304,"dark")]));
        check(tracker.Artifacts().Length == 0 && tracker.Layers().Length == 0, "Changed matchup cannot retain another server pair's siege ownership");
        for (int i = 0; i < 600; i++) Apply("artifact-siege", 200 + i, new SiegeReading([finished with { Event = i }], [new(i, "light")]));
        check(tracker.Layers().Length == 128 && tracker.Artifacts().Length == 512, "Siege retention stays bounded under changing event/artifact IDs");
        var engine = new CombatEngine(); foreach (var e in Decode(0x05e3, Body(0x05e3)).Events) engine.Apply(e); engine.NewFight("Reset");
        check(engine.Siege.Artifacts().Length == 1 && engine.Siege.Layers().Length == 1 && engine.Fight.Network.Count == 0 && engine.Loot.Rows().Length == 0,
            "Manual fight reset retains compact siege state without raw histories or loot changes");
    }
}
