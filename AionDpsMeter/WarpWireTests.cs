using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class WarpWireTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        byte[] Body(bool entering, byte flags, int actor = 1) {
            var w = new PartyWireTests.Writer(); w.Var(actor); w.Raw(flags);
            foreach (float n in new[] { 10.5f, -20.25f, 30f, .5f }) w.Raw(BitConverter.GetBytes(n));
            if (!entering || (flags & 1) != 0) w.Raw(2);
            if (!entering || (flags & 2) != 0) w.Raw(3);
            if (!entering) w.Raw(4);
            if ((flags & (entering ? 4 : 1)) != 0) w.Int(123);
            if ((flags & (entering ? 8 : 2)) != 0) w.Int(456);
            w.Var(500); return w.Bytes.ToArray();
        }
        DecodeResult Decode(bool entering, byte[] body, long at = 2000) {
            var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw(entering ? (byte)0x3d : (byte)0x3c, 0x37); w.Raw(body);
            return new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, w.Bytes.ToArray()) { TimestampMs = at });
        }
        foreach (bool entering in new[] { false, true }) {
            bool valid = true, rejected = true;
            for (byte flags = 0; flags < (entering ? 16 : 4); flags++) {
                var body = Body(entering, flags); var value = Decode(entering, body).Events.Single().Network!.Fields.Deserialize<WarpReading>()!;
                valid &= value is { ActorId: 1, X: 10.5f, Y: -20.25f, Z: 30, Direction: .5f, Duration: 500 } && value.Entering == entering &&
                    value.SkillId == ((flags & (entering ? 4 : 1)) != 0 ? 123 : 0) && value.EffectId == ((flags & (entering ? 8 : 2)) != 0 ? 456 : 0) &&
                    value.MoveState == (!entering || (flags & 1) != 0 ? 2 : 0) && value.MoveSubstate == (!entering || (flags & 2) != 0 ? 3 : 0) && value.Reason == (entering ? 0 : 4);
                rejected &= Enumerable.Range(0, body.Length).All(n => Decode(entering, body[..n]).Events.Count == 0) && Decode(entering, [..body, 0]).Events.Count == 0;
            }
            check(valid && rejected && Decode(entering, Body(entering, 128)).Events.Count == 0 && Decode(entering, Body(entering, 0, 0)).Events.Count == 0,
                $"Warp {(entering ? "entry" : "leave")} validates every optional-field combination, truncation, trailer and header");
        }
        var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: new(1, "Local", true, true, 0, "Cleric", 1)));
        engine.Apply(new("network", Network: new("local-zone", 1000, "test", JsonSerializer.SerializeToElement(new { MapId = 1010, X = 0, Y = 0, Z = 0 }))));
        foreach (var e in Decode(false, Body(false, 3)).Events) engine.Apply(e);
        foreach (var e in Decode(true, Body(true, 15, 2), 3000).Events) engine.Apply(e);
        foreach (var e in Decode(true, Body(true, 15), 1500).Events) engine.Apply(e);
        check(engine.Position.Latest is { X: 10.5, Y: -20.25, Timestamp: 2000 } && engine.Fight.Network.All(e => e.Kind != "actor-position"),
            "Warp updates only the latest local position and retains no raw movement events");
        engine.NewFight("Reset"); var store = new FightStore(Path.Combine(temp, "warp")); store.Save(engine.Snapshot());
        check(store.Load(Path.Combine(store.Root, engine.Fight.Id + ".json")).LocalPosition is { X: 10.5, MapId: 1010 },
            "Latest warp position survives fight reset and saved metadata");
        var tracker = new LocalPositionTracker(); tracker.Apply(Decode(true, Body(true, 0)).Events.Single().Network!, 1);
        check(tracker.Latest == null, "Warp coordinates do not invent a map when zone identity is unknown");
    }
}
