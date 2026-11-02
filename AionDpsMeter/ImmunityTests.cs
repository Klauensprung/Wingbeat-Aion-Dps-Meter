using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class ImmunityTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        DecodeResult Decode(byte[] body) {
            var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw(0x2d, 0x38); w.Raw(body);
            return new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, w.Bytes.ToArray()) { TimestampMs = 2000 });
        }
        var writer = new PartyWireTests.Writer(); writer.Var(10); writer.Var(2); writer.Int(100); writer.Int(200);
        var bytes = writer.Bytes.ToArray(); var notice = Decode(bytes).Events.Single();
        check(notice.Network!.Fields.Deserialize<ImmunityReading>() is { EntityId: 10, EffectIds.Length: 2 } &&
            Enumerable.Range(0, bytes.Length).All(n => Decode(bytes[..n]).Events.Count == 0) && Decode([..bytes, 0]).Events.Count == 0 &&
            Decode([0, 0]).Events.Count == 0 && Decode([10, 0]).Events.Single().Network!.Fields.Deserialize<ImmunityReading>()!.EffectIds.Length == 0,
            "Immunity validates full list boundaries and entity identity, retaining empty observations without inventing effects");
        var engine = new CombatEngine();
        engine.Apply(new("actor", Actor: new(1, "Player", true, false, 0, "Cleric", 1)));
        engine.Apply(new("actor", Actor: new(10, "Boss", false, false, 0, "Unknown", 0)));
        engine.Apply(new("network", Network: new("boss-cast", 1000, "test", JsonSerializer.SerializeToElement(new { CasterId = 10, SkillCode = 123 }))));
        engine.Apply(new("damage", Damage: new() { ActorId = 1, TargetId = 10, SkillCode = 123, Timestamp = 1000, Damage = 100 }));
        engine.Apply(notice);
        engine.Apply(new("damage", Damage: new() { ActorId = 1, TargetId = 10, SkillCode = 123, Timestamp = 3000, Damage = 100 }));
        var fight = engine.Snapshot(); var encounter = BossEncounterAnalysis.Build(fight, id => id.ToString()).Single();
        check(encounter.Moments.Single(m => m.Kind == "Immunity") is { Timestamp: 2000, Amount: 0 } &&
            encounter.DamageToBoss == 200 && fight.Defense.Count == 0 && EffectAnalysis.Intervals(fight).Length == 0,
            "Boss immunity appears as an instantaneous timeline notice without damage prevention or successful control effects");
        var store = new FightStore(Path.Combine(temp, "immunity")); store.Save(fight);
        var restored = store.Load(Path.Combine(store.Root, fight.Id + ".json"));
        check(BossEncounterAnalysis.Build(restored, id => id.ToString()).Single().Moments.Any(m => m.Kind == "Immunity") &&
            restored.Network.Single(n => n.Kind == "abnormal-immunity").Fields.EnumerateObject().Count() == 2,
            "Saved encounters retain compact immunity evidence for historical timelines");
        var ambiguous = fight.Copy(); ambiguous.Actors[20] = fight.Actors[10] with { Id = 20, OriginalEntityId = 10 };
        check(BossEncounterAnalysis.Build(ambiguous, id => id.ToString()).All(b => b.Moments.All(m => m.Kind != "Immunity")),
            "Reused entity IDs do not assign ambiguous immunity notices to either boss identity");
    }
}
