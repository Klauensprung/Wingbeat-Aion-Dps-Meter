using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class MapEventTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        byte[] Body(bool join, byte status = 1, int stage = 1, int step = 2) {
            var w = new PartyWireTests.Writer(); w.Int(100); if (join) w.Text("Synthetic event"); w.Raw(status); w.Var(stage); w.Var(step);
            w.Var(2); w.Var(128); w.Var(0); if (join) w.Long(12345); w.Var(2); w.Raw(1, 0, 255, 255); return w.Bytes.ToArray();
        }
        byte[] Frame(ushort opcode, byte[] body) { var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw((byte)(opcode >> 8), (byte)opcode); w.Raw(body); return w.Bytes.ToArray(); }
        DecodeResult Decode(ushort opcode, byte[] body) => new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, Frame(opcode, body)) { TimestampMs = 10 });
        foreach (bool join in new[] { false, true }) {
            var body = Body(join); ushort opcode = join ? (ushort)0x2e92 : (ushort)0x2d92;
            check(Decode(opcode, body).Events.Single().Network!.Fields.Deserialize<MapEventReading>() is { EventId: 100, Stage: 1, Step: 2 } &&
                Enumerable.Range(0, body.Length).All(n => Decode(opcode, body[..n]).Events.Count == 0) && Decode(opcode, [..body, 0]).Events.Count == 0,
                $"Map event join={join} consumes objectives/variables and rejects every truncation/trailer");
        }
        var tracker = new MapEventTracker(); var joinEvent = Decode(0x2e92, Body(true)).Events.Single().Network!;
        var changed = Decode(0x2d92, Body(false, 3, 2, 1)).Events.Single().Network! with { Timestamp = 20 };
        check(tracker.Apply(changed) == null && tracker.Snapshot().Length == 0, "Unknown map progress does not invent a joined event");
        check(tracker.Apply(joinEvent) == null && tracker.Snapshot().Single().Name == "Synthetic event" && tracker.Apply(changed) is { Done: true, Stage: 2, Step: 1 },
            "Joined event provides its name and changed progress reports completion independently of dungeon state");
        check(tracker.Apply(changed) == null && tracker.Apply(joinEvent) == null && tracker.Snapshot().Single().Done,
            "Duplicate and stale map progress cannot regress or repeat an event transition");
        var leave = Decode(0x2f92, BitConverter.GetBytes(100)).Events.Single().Network! with { Timestamp = 30 };
        tracker.Apply(leave); tracker.Apply(joinEvent);
        check(tracker.Snapshot().Length == 0, "Map event departure clears state and rejects stale rejoin");
        tracker.Apply(joinEvent with { Timestamp = 40 }); tracker.Apply(new("local-zone", 50, "test", JsonSerializer.SerializeToElement(new { MapId = 1010 })));
        check(tracker.Snapshot().Length == 0, "Map boundary clears current joined event state");
        for (int id = 1; id <= 140; id++) tracker.Apply(new("map-event-join", 50 + id, "test", JsonSerializer.SerializeToElement(new MapEventReading(id, "Event", 1, 1, 1))));
        check(tracker.Snapshot().Length == 128, "Map event state is bounded across many distinct joins");
        var engine = new CombatEngine(); engine.Apply(new("network", Network: joinEvent)); engine.NewFight("Next");
        check(engine.MapEvents.Snapshot().Length == 1 && engine.Fight.Network.Count == 0, "Map events survive manual fight reset without retained network history");
        var login = new NetworkEvent("account-login", 30, "test", JsonSerializer.SerializeToElement(new LoginReading(0, 2301, [])));
        engine.Apply(new("network", Network: login));
        check(engine.MapEvents.Snapshot().Length == 0, "Reconnect without map preparation clears previous-session event participation");
        engine.Apply(new("network", Network: joinEvent with { Timestamp = 29 }));
        engine.Apply(new("network", Network: changed with { Timestamp = 31 }));
        check(engine.MapEvents.Snapshot().Length == 0, "Old joins and progress without a fresh join cannot recreate prior-session events");
        engine.Apply(new("network", Network: joinEvent with { Timestamp = 32 }));
        engine.Apply(new("network", Network: login));
        check(engine.MapEvents.Snapshot().Length == 1, "Duplicate login preserves freshly joined events");
        using var fixtures = new ReferencePacketFixtures(Path.Combine(temp, "reference-map-events-synthetic.jsonl"));
        long at = 1;
        void Send(ushort op, byte[] body) => fixtures.Add(new CapturedPayload(13328, 50000, Frame(op, body)) { TimestampMs = at++ });
        Send(0x2d92, Body(false)); Send(0x2e92, Body(true)); Send(0x2d92, Body(false));
        Send(0x2d92, Body(false, 2, 2, 1)); Send(0x2d92, Body(false, 3, 2, 1)); Send(0x2d92, Body(false, 3, 2, 1));
        Send(0x2f92, BitConverter.GetBytes(100)); Send(0x2d92, Body(false, 3, 3, 1));
    }
}
