using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class WingTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        byte[] Frame(byte op, byte[] body) { var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw(op, 0x56); w.Raw(body); return w.Bytes.ToArray(); }
        DecodeResult Decode(byte op, byte[] body) => new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, Frame(op, body)));
        var list = new PartyWireTests.Writer(); list.Var(2); list.Int(100); list.Raw(0); list.Int(200); list.Raw(12);
        using var fixtures = new ReferencePacketFixtures(Path.Combine(temp, "reference-wings-synthetic.jsonl"));
        foreach (var appearance in new[] { LocalCharacterWireTests.Body(1, "Synthetic"), LocalCharacterWireTests.Body(1, "Synthetic", wing: 100, skin: 200) }) {
            var frame = new PartyWireTests.Writer(); frame.Var(appearance.Length + 6); frame.Raw(0x33, 0x36); frame.Raw(appearance);
            fixtures.Add(new CapturedPayload(13328, 50000, frame.Bytes.ToArray()) { TimestampMs = 1 });
        }
        void Verify(byte op, byte[] body, string name) {
            check(Decode(op, body).Events.Count == 1 && Enumerable.Range(0, body.Length).All(n => Decode(op, body[..n]).Events.Count == 0) && Decode(op, [..body, 0]).Events.Count == 0,
                name + " consumes the complete payload and rejects truncations/trailers");
            fixtures.Add(new CapturedPayload(13328, 50000, Frame(op, body)) { TimestampMs = 1 });
        }
        Verify(0x72, list.Bytes.ToArray(), "Owned wing list"); Verify(0x72, [0], "Empty wing list");
        foreach (bool skin in new[] { false, true }) {
            var change = new PartyWireTests.Writer(); change.Var(1); change.Bool(skin); change.Int(skin ? 0 : 200);
            Verify(0x76, change.Bytes.ToArray(), skin ? "Wing skin clearing" : "Equipped wing update");
        }
        var engine = new CombatEngine();
        void Send(string kind, long time, object value) => engine.Apply(new("network", Network: new(kind, time, "test", JsonSerializer.SerializeToElement(value))));
        Send("wings", 1, new { Wings = new CapturedWing[] { new(100, 0), new(200, 12) } });
        Send("local-zone", 2, new { MapId = 1010, AwaitingIdentity = true });
        engine.Apply(new("local-player", Actor: new(1, "First", true, false, 0, "Cleric", 1)));
        Send("wing-change", 3, new { EntityId = 1, Skin = false, WingId = 200 });
        Send("wing-change", 4, new { EntityId = 1, Skin = true, WingId = 300 });
        var before = engine.Snapshot();
        Send("wing-change", 2, new { EntityId = 1, Skin = false, WingId = 100 });
        Send("wing-change", 5, new { EntityId = 2, Skin = false, WingId = 999 });
        check(engine.Fight.Actors[1].Wings is { Owned.Length: 2, Equipped: 200, Skin: 300 } && engine.Fight.Network.Count == 0,
            "Wing baseline survives initial map preparation, with separate equipment/skin state and stale/other-actor changes ignored");
        Send("wings", 6, new { Wings = Array.Empty<CapturedWing>() });
        Send("wing-change", 6, new { EntityId = 1, Skin = true, WingId = 0 });
        check(engine.Fight.Actors[1].Wings is { Owned.Length: 0, Equipped: 200, Skin: 0 } && before.Actors[1].Wings!.Owned.Length == 2,
            "Empty wing list clears ownership without conflating equipment or mutating previous snapshots");
        Send("local-zone", 10, new { MapId = 1010, AwaitingIdentity = true });
        Send("wings", 11, new { Wings = new CapturedWing[] { new(400, 3) } });
        Send("local-zone", 10, new { MapId = 1010, AwaitingIdentity = true });
        engine.Apply(new("local-player", Actor: new(2, "Second", true, false, 0, "Assassin", 1)));
        check(engine.Fight.Actors[2].Wings is { Owned.Length: 1, Equipped: null, Skin: null } next && next.Owned[0].Id == 400,
            "Wing lists bind to the new identity without inheriting the previous character's equipped wing");
        var store = new FightStore(Path.Combine(temp, "wings")); store.Save(before);
        check(store.Load(Path.Combine(store.Root, before.Id + ".json")).Actors[1].Wings is { Equipped: 200, Skin: 300, Owned.Length: 2 },
            "Owned wings and separate equipment/skin observations persist with saved fights");
    }
}
