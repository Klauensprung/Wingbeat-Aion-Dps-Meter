using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class BossSignalTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        DecodeResult Decode(ushort opcode, byte[] body) {
            var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw((byte)(opcode >> 8), (byte)opcode); w.Raw(body);
            return new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, w.Bytes.ToArray()) { TimestampMs = 1500 });
        }
        check(Decode(0x0040, [10, 255]).Events.Single().Network!.Fields.GetProperty("Phase").GetByte() == 255 &&
            Decode(0x0040, [10]).Events.Count == 0 && Decode(0x0040, [10, 1, 0]).Events.Count == 0 && Decode(0x0040, [0, 1]).Events.Count == 0,
            "Phase packets preserve all byte values and reject incomplete, extended or unidentified records");
        check(Decode(0x528d, [10]).Events.Single().Network!.Kind == "npc-aggro" && Decode(0x528d, []).Events.Count == 0 &&
            Decode(0x528d, [10, 0]).Events.Count == 0 && Decode(0x528d, [0]).Events.Count == 0,
            "Initial aggro requires an exact positive entity record and does not invent its recipient");
        check(Decode(0x2092, [1]).Events.Single().Network!.Kind == "party-boss-target" &&
            Decode(0x2092, [0]).Events.Single().Network!.Fields.GetProperty("EntityId").GetInt32() == 0 &&
            Decode(0x2092, []).Events.Count == 0 && Decode(0x2092, [128]).Events.Count == 0 && Decode(0x2092, [1, 0]).Events.Count == 0,
            "Party boss-target accepts a target or explicit clear and rejects truncated or extended records");
        var engine = new CombatEngine();
        engine.Apply(new("local-player", Actor: new(1, "Player", true, true, 0, "Cleric", 1)));
        engine.Apply(new("actor", Actor: new(10, "Boss", false, false, 0, "Unknown", 0)));
        void Event(string kind, long at, object fields) => engine.Apply(new("network", Network: new(kind, at, "test", JsonSerializer.SerializeToElement(fields))));
        void Hit(long at) => engine.Apply(new("damage", Damage: new() { ActorId = 1, TargetId = 10, Timestamp = at, Damage = 100 }));
        Event("boss-cast", 1000, new { CasterId = 10, SkillCode = 123 }); Hit(1000);
        foreach (var e in Decode(0x0040, [10, 2]).Events.Concat(Decode(0x528d, [10]).Events)) engine.Apply(e);
        Event("actor-phase", 1600, new { EntityId = 10, Phase = 2 });
        Event("actor-phase", 1700, new { EntityId = 10, Phase = 3 });
        Event("target", 1800, new { ActorId = 10, TargetId = 1 });
        Event("target", 1900, new { ActorId = 10, TargetId = 1 });
        Event("target", 2000, new { ActorId = 10, TargetId = 0 });
        Event("local-target", 2100, new { EntityId = 10 }); Hit(3000);
        foreach (var e in Decode(0x2092, [1]).Events) engine.Apply(e);
        var fight = engine.Snapshot(); var boss = BossEncounterAnalysis.Build(fight, id => id.ToString()).Single();
        check(fight.PartyBossTarget?.EntityId == 1 && fight.LocalTarget?.EntityId == 10 && PartyBossTarget.Label(fight) == "Boss aggro: Player" &&
            !fight.Network.Any(e => e.Kind == "party-boss-target"),
            "Party boss aggro remains separate from local selection and retains one compact state without packet history");
        check(boss.Moments.Count(m => m.Kind == "Phase") == 2 && boss.Moments.Count(m => m.Kind == "Aggro") == 1 &&
            boss.Moments.Count(m => m.Kind == "Target") == 2 && boss.Moments.Single(m => m.Kind == "Target" && m.TargetId == 0).Detail.Contains("cleared") &&
            boss.DamageToBoss == 200 && engine.Fight.LocalTarget?.EntityId == 10,
            "Boss timeline shows phase changes, aggro and actual boss targets without duplicate states or confusing player selection");
        var markers = boss.Moments.Where(BossTimelineEvents.IsMarker).ToArray();
        check(markers.Count(m => m.Kind == "Phase") == 2 && markers.Count(m => m.Kind == "Aggro") == 1 && markers.Count(m => m.Kind == "Target") == 2 &&
            markers.All(m => BossTimelineEvents.Includes(m, 123)),
            "Phase, aggro and target observations enter the rendered marker lane and survive skill filtering");
        var unknownSkill = new BossMoment(1000, 0, "Hit", 0, 1, 10, "Unknown skill");
        check(!BossTimelineEvents.IsMarker(unknownSkill) && !BossTimelineEvents.Includes(unknownSkill, 123) && BossTimelineEvents.Includes(unknownSkill, 0) &&
            new[] { "Combat state", "Displacement", "Immunity", "Stagger break", "Stagger coverage", "Boss death", "Enrage" }
                .All(kind => BossTimelineEvents.IsMarker(unknownSkill with { Kind = kind }) && BossTimelineEvents.Includes(unknownSkill with { Kind = kind }, 123)),
            "Encounter observations remain inspectable while unidentified skill activity stays in its own filtered lane");
        var store = new FightStore(Path.Combine(temp, "boss-signals")); store.Save(fight);
        var saved = store.Load(Path.Combine(store.Root, fight.Id + ".json"));
        check(saved.PartyBossTarget == fight.PartyBossTarget, "Full saved fight preserves latest party boss target");
        check(BossEncounterAnalysis.Build(saved, id => id.ToString()).Single().Moments.SequenceEqual(boss.Moments),
            "Compact saved boss signals reproduce live encounter moments");
        var empty = new Fight(); empty.Actors[10] = fight.Actors[10]; empty.Network.AddRange(fight.Network.Where(n => n.Kind is "actor-phase" or "npc-aggro" or "target"));
        check(BossEncounterAnalysis.Build(empty, id => id.ToString()).Length == 0, "Phase, aggro and target notifications alone do not invent a boss encounter");
        engine.NewFight("Next");
        check(engine.Fight.PartyBossTarget?.EntityId == 1, "Manual fight reset preserves current party boss aggro");
        Event("party-boss-target", 1400, new { EntityId = 2 });
        check(engine.Fight.PartyBossTarget?.EntityId == 1, "Stale party boss-target notification cannot replace newer state");
        Event("party-boss-target", 3100, new { EntityId = 0 });
        check(engine.Fight.PartyBossTarget?.EntityId == 0 && PartyBossTarget.Label(engine.Fight) == "", "Explicit clear removes party aggro indicator");
        Event("party-boss-target", 3200, new { EntityId = 1 });
        Event("entity-removed", 3300, new { EntityId = 1 });
        check(engine.Fight.PartyBossTarget?.EntityId == 0, "Removed party entity clears its boss aggro indicator");
        Event("party-boss-target", 3400, new { EntityId = 1 });
        engine.Apply(new("actor", Actor: new(1, "Replacement", true, true, 0, "Cleric", 1)));
        check(engine.Fight.PartyBossTarget?.EntityId == 0, "Entity ID reuse does not transfer boss aggro to another character");
        Event("party-boss-target", 3500, new { EntityId = 1 });
        engine.Apply(new("local-player", Actor: new(2, "Other", true, true, 0, "Cleric", 1)));
        check(engine.Fight.PartyBossTarget == null, "Character appearance clears previous party boss target");
        using (var fixtures = new ReferencePacketFixtures(Path.Combine(temp, "reference-party-target-synthetic.jsonl"))) {
            foreach (int id in new[] { 1, 128, 0 }) {
                var body = new PartyWireTests.Writer(); body.Var(id);
                var frame = new PartyWireTests.Writer(); frame.Var(body.Bytes.Count + 6); frame.Raw(0x20, 0x92); frame.Raw(body.Bytes.ToArray());
                fixtures.Add(new CapturedPayload(13328, 50000, frame.Bytes.ToArray()) { TimestampMs = 100 });
            }
        }
    }
}
