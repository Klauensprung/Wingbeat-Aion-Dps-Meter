using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class DisplacementTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        byte[] Body(bool timed, int actor = 1, float x = 10.5f, byte move = 2) {
            var w = new PartyWireTests.Writer(); w.Var(actor); w.Var(10); w.Int(123);
            w.Raw(BitConverter.GetBytes(x)); w.Raw(BitConverter.GetBytes(-20.5f)); w.Raw(BitConverter.GetBytes(30f));
            if (timed) w.Int(500); w.Raw(move, 1, 2); return w.Bytes.ToArray();
        }
        DecodeResult Decode(bool timed, byte[] body, long time = 2000) {
            var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw(timed ? (byte)0x14 : (byte)0x16, 0x38); w.Raw(body);
            return new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, w.Bytes.ToArray()) { TimestampMs = time });
        }
        foreach (bool timed in new[] { false, true }) {
            byte[] body = Body(timed);
            var value = Decode(timed, body).Events.Single().Network!.Fields.Deserialize<DisplacementReading>()!;
            check(value is { ActorId: 1, CasterId: 10, X: 10.5f, Y: -20.5f, MoveType: 2 } && value.DurationMs == (timed ? 500 : 0) &&
                Enumerable.Range(0, body.Length).All(n => Decode(timed, body[..n]).Events.Count == 0) && Decode(timed, [..body, 0]).Events.Count == 0 &&
                Decode(timed, Body(timed, 0)).Events.Count == 0 && Decode(timed, Body(timed, x: float.NaN)).Events.Count == 0,
                $"Displacement {(timed ? "start" : "instant")} validates signed coordinates, timing and every record boundary");
        }
        var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: new(1, "Local", true, true, 0, "Cleric", 1)));
        engine.Apply(new("actor", Actor: new(10, "Boss", false, false, 0, "Unknown", 0)));
        void Event(string kind, long at, object fields) => engine.Apply(new("network", Network: new(kind, at, "test", JsonSerializer.SerializeToElement(fields))));
        Event("local-zone", 1000, new { MapId = 1010, X = 0, Y = 0, Z = 0 });
        Event("boss-cast", 1000, new { CasterId = 10, SkillCode = 123 });
        engine.Apply(new("damage", Damage: new() { ActorId = 1, TargetId = 10, SkillCode = 123, Timestamp = 1000, Damage = 100 }));
        foreach (var e in Decode(true, Body(true)).Events) engine.Apply(e);
        foreach (var e in Decode(false, Body(false, actor: 2, x: 90), 2500).Events) engine.Apply(e);
        foreach (var e in Decode(false, Body(false, x: 80), 1500).Events) engine.Apply(e);
        check(engine.Position.Latest is { X: 10.5, Y: -20.5, Timestamp: 2000 } && engine.Fight.Defense.Count == 0,
            "Only current local displacement updates the player marker; stale or other-actor movement does not");
        engine.Apply(new("damage", Damage: new() { ActorId = 1, TargetId = 10, SkillCode = 123, Timestamp = 3000, Damage = 100 }));
        var fight = engine.Snapshot();
        check(BossEncounterAnalysis.Build(fight, id => id.ToString()).Single().Moments.Any(m => m.Kind == "Displacement" && m.TargetId == 1 && m.Detail.Contains("500 ms")),
            "Boss-caused player displacement appears in the encounter timeline without inventing a control countdown");
        var store = new FightStore(Path.Combine(temp, "displacement")); store.Save(fight);
        var saved = store.Load(Path.Combine(store.Root, fight.Id + ".json"));
        check(BossEncounterAnalysis.Build(saved, id => id.ToString()).Single().Moments.Any(m => m.Kind == "Displacement") &&
            saved.Network.Where(e => e.Kind == "displacement").All(e => !e.Fields.TryGetProperty("X", out _)),
            "Saved displacement retains compact encounter evidence and discards raw movement coordinates");
        var isolated = new LocalPositionTracker(); isolated.Apply(Decode(false, Body(false)).Events.Single().Network!, 1);
        check(isolated.Latest == null && DisplacementWire.Name(255) == null, "Displacement cannot invent an unknown map or label an unknown movement type");
    }
}
