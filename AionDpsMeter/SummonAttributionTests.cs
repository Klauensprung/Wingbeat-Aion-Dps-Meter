using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class SummonAttributionTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var engine = new CombatEngine();
        void Player(int id, string name) => engine.Apply(new("actor", Actor: new(id, name, true, false, 0, "Unknown", 1)));
        void Summon(int owner) => engine.Apply(new("actor", Actor: new(10, "Summon", false, false, 0, "Unknown", 0) { NpcId = 2000001, OwnerId = owner }));
        void Hit(long time, bool heal = false) => engine.RecordDamage(new() { Timestamp = time, ActorId = 10, TargetId = 20, SkillCode = 17090000, Damage = 100 }, heal);
        Player(1, "First"); Summon(1); Hit(1000); Hit(1100, true);
        var before = engine.Snapshot();
        check(Analysis.Totals(before).Single().Damage == 100 && before.Healing.Single() is { ActorId: 1, SourceActorId: 10 } &&
            before.Damage.Single() is { ActorId: 1, SourceActorId: 10, TargetId: 20 },
            "Summon outgoing damage and healing count once for the owner while retaining source and recipient");
        Summon(2); Hit(1200); Player(2, "Second");
        var after = engine.Snapshot();
        check(Analysis.Totals(after).Count(r => r.Damage == 100) == 2 && after.Damage[0].SourceActorId != 10 &&
            after.Actors[after.Damage[0].SourceActorId].OwnerId == 1 && before.Damage.Single().SourceActorId == 10,
            "Reused summon identity preserves old attribution and late owner identity names only its own hits");
        Player(2, "Replacement"); Hit(1300);
        var reused = engine.Snapshot();
        check(reused.Damage[^1].ActorId != 2 && reused.Name(reused.Damage[^1].ActorId) == "Second" &&
            reused.Damage[1].ActorId == reused.Damage[^1].ActorId,
            "Reusing an owner entity cannot silently redirect a previous summon to the replacement player");
        var store = new FightStore(Path.Combine(temp, "summon-attribution")); store.Save(reused);
        var saved = store.Load(Path.Combine(store.Root, reused.Id + ".json"));
        check(saved.Damage[^1].SourceActorId == reused.Damage[^1].SourceActorId &&
            Analysis.Totals(saved).SequenceEqual(Analysis.Totals(reused)),
            "Saved fights preserve summon attribution and source identities");
        engine.NewFight("Reset"); Hit(2000);
        check(engine.Fight.Damage.Single().ActorId == 10,
            "Ordinary reset drops ownership links to identities that were archived and not carried forward");
        Summon(1); engine.NewFight("Same owner"); Hit(2100);
        check(engine.Fight.Damage.Single().ActorId == 1, "Ordinary DPS reset preserves current summon ownership");
        engine.Apply(new("network", Network: new("local-zone", 2200, "test", JsonSerializer.SerializeToElement(new { MapId = 1010 })))); Hit(2300);
        check(engine.Fight.Damage[^1].ActorId == 10 && engine.Fight.Damage[0].ActorId == 1,
            "Zone entry clears future summon attribution without rewriting historical hits");
        engine.NewFight("Defense"); Summon(1); Player(3, "Enemy owner");
        engine.Apply(new("actor", Actor: new(30, "Enemy summon", false, false, 0, "Unknown", 0) { NpcId = 2000002, OwnerId = 3 }));
        engine.Apply(new("defense", Defense: new() { Timestamp = 3000, ActorId = 10, TargetId = 30, SkillCode = 17090000, Outcome = DefenseOutcome.Evade }));
        check(engine.Fight.Defense.Single() is { ActorId: 1, TargetId: 3, SourceActorId: 10, SourceTargetId: 30 } &&
            Analysis.Defense(engine.Fight).Single(r => r.Character == "First").Avoided == 1 &&
            Analysis.Defense(engine.Fight).Single(r => r.Character == "Enemy owner").Evaded == 1,
            "Summon avoidance resolves attacker and defender owners without losing either original entity");
        Summon(2);
        check(engine.Fight.Defense.Single().SourceActorId != 10 && engine.Fight.Defense.Single().ActorId == 1,
            "Reused summons preserve original defense source identity and credited owner");
        engine.NewFight("Personal breakdown"); Summon(1); engine.Fight.LocalPlayer = 1;
        Hit(4000); engine.RecordDamage(new() { Timestamp = 4100, ActorId = 1, TargetId = 20, SkillCode = 17090000, Damage = 50 }, false);
        var personal = PersonalSkills.Build(engine.Fight, false).Single();
        check(personal.Amount == 150 && personal.SummonAmount == 100 && personal.SummonSources == "Summon" && personal.Hits == 2,
            "Personal skill breakdown exposes summon contribution without duplicating skill totals or hits");
        var defenseSaved = engine.Snapshot();
        defenseSaved.Defense.Add(new() { Timestamp = 4000, ActorId = 1, TargetId = 3, SourceActorId = 10, SourceTargetId = 30, Outcome = DefenseOutcome.Resist });
        store.Save(defenseSaved);
        check(store.Load(Path.Combine(store.Root, defenseSaved.Id + ".json")).Defense.Single() is { SourceActorId: 10, SourceTargetId: 30 },
            "Defense attribution retains both source IDs through disk storage");
    }
}
