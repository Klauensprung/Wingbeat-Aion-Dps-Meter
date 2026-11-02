using System.IO;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class TrainingTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var engine = new CombatEngine();
        engine.Apply(new("local-player", Actor: new(1, "Training player", true, false, 0, "Cleric", 1)));
        engine.StartTraining(1000, 30, "Attempt A", "Build A", "Synthetic note");
        void Hit(int actor, long time, int damage, bool healing = false) => engine.Apply(new(healing ? "heal" : "damage",
            Damage: new DamageEvent { ActorId = actor, TargetId = 100, Timestamp = time, Damage = damage, SkillCode = 17010001 }));
        Hit(1, 999, 999); Hit(1, 1000, 3000); Hit(2, 2000, 9000); Hit(1, 3000, 1000, true);
        var preview = engine.TrainingSnapshot(10000)!;
        var idle = engine.TrainingSnapshot(15000)!;
        check(preview.Seconds == 9 && idle.Seconds == 14 && ReferenceEquals(preview.Damage, idle.Damage) &&
            ReferenceEquals(idle, engine.TrainingSnapshot(15999)) && PersonalSkills.Build(idle, false).Single().Rate < PersonalSkills.Build(preview, false).Single().Rate,
            "Training overlay DPS includes idle time while immutable event lists and same-second previews are reused");
        engine.AdvanceTime(20000);
        check(engine.TrainingActive, "Training continues across the ordinary fight idle reset");
        Hit(1, 30999, 6000); Hit(1, 31000, 999);
        check(preview.Damage.Count == 1 && engine.TrainingSnapshot(31000) == null,
            "New training hits never mutate an earlier overlay snapshot and finishing clears the preview");
        var saved = engine.SaveCompleted(_ => { }).Single(f => f.Training != null);
        check(!engine.TrainingActive && saved.Damage.Count == 2 && saved.Damage.Sum(h => h.Damage) == 9000 &&
            saved.Healing.Count == 0 && saved.Seconds == 30 && PersonalSkills.Build(saved, false).Single().Rate == 300,
            "Timed training excludes other players, healing and boundary hits, with full-duration DPS including pauses");
        var store = new FightStore(Path.Combine(temp, "training")); store.Save(saved);
        var loaded = store.Load(Path.Combine(store.Root, saved.Id + ".json"));
        check(loaded.Training == saved.Training && loaded.Training?.Group == "Build A" && loaded.Training.Notes == "Synthetic note" &&
            loaded.Seconds == 30 && FightArchiveSummary.Create(loaded).Training == loaded.Training,
            "Training duration, groups and notes survive local saving and compact archive summaries");
        engine.StartTraining(40000, 0, "Unlimited", "", ""); Hit(1, 41000, 1000);
        engine.AdvanceTime(400000); check(engine.TrainingActive, "Unlimited training does not auto-stop at five minutes");
        engine.StopTraining(401000); engine.StopTraining(402000);
        var unlimited = engine.SaveCompleted(_ => { }).Where(f => f.Training != null).ToArray();
        check(unlimited.Length == 1 && unlimited[0].Seconds == 361, "Manual stop saves one attempt with elapsed duration and is idempotent");
        bool rejected = false;
        try { new CombatEngine().StartTraining(0, 30, "Unknown", "", ""); } catch (InvalidOperationException) { rejected = true; }
        check(rejected, "Training requires a known local character rather than guessing identity");
        var first = loaded.Copy();
        var second = loaded.Copy(); second.Id = Guid.NewGuid().ToString("N");
        second.Training = second.Training! with { End = second.Start + 60000, LimitSeconds = 60 };
        second.Damage = [new DamageEvent { ActorId = 1, TargetId = 100, SkillCode = 17010001, Damage = 12000, Timestamp = 2000, IsCritical = true },
            new DamageEvent { ActorId = 1, TargetId = 100, SkillCode = 17020001, Damage = 6000, Timestamp = 3000 },
            new DamageEvent { ActorId = 2, TargetId = 100, SkillCode = 17030001, Damage = 999999, Timestamp = 4000 }];
        var comparison = TrainingComparison.Build(first, second, code => "Skill " + code);
        var common = comparison.Skills.Single(s => s.SkillCode == 17010001);
        check(comparison.DamageA == 9000 && comparison.DamageB == 18000 && comparison.DpsA == comparison.DpsB &&
            common.DpsA == 300 && common.DpsB == 200 && common.DpsChange == -100 && common.HitsA == 2 && common.HitsB == 1 && common.CritB == 100,
            "Training comparison uses local-only per-skill damage, independent elapsed durations, hits and crit rates");
        check(comparison.Skills.Length == 2 && comparison.Skills.Single(s => s.SkillCode == 17020001).ChangePercent == null &&
            Math.Abs(common.ChangePercent!.Value + 100d / 3) < .001 && comparison.Skills.Sum(s => s.DpsChange) == comparison.DpsB - comparison.DpsA,
            "New skills have no fabricated percentage baseline and skill deltas reconcile to total DPS change");
        var reverse = TrainingComparison.Build(second, first, code => code.ToString());
        check(reverse.Skills.Single(s => s.SkillCode == 17020001).ChangePercent == -100 &&
            reverse.Skills.All(r => r.DpsChange == -comparison.Skills.Single(s => s.SkillCode == r.SkillCode).DpsChange),
            "Swapping training attempts reverses deltas and removed skills show a complete loss");
        bool rejectedSummary = false;
        try { TrainingComparison.Build(FightArchiveSummary.Create(first), second, code => code.ToString()); }
        catch (ArgumentException) { rejectedSummary = true; }
        check(rejectedSummary, "Training comparisons require full selected attempts rather than incomplete archive event lists");
        var switched = new CombatEngine();
        switched.Apply(new("local-player", Actor: new(1, "First trainee", true, false, 0, "Cleric", 1) { CharacterId = 10 }));
        switched.StartTraining(1000, 0, "Identity boundary", "", "");
        switched.Apply(new("damage", Damage: new() { ActorId = 1, TargetId = 100, Timestamp = 2000, Damage = 100 }));
        switched.AdvanceTime(5000);
        switched.Apply(new("local-player", Actor: new(2, "Second trainee", true, false, 0, "Cleric", 1) { CharacterId = 20 }));
        var ended = switched.SaveCompleted(_ => { }).Single(f => f.Training != null);
        check(!switched.TrainingActive && ended.LocalPlayer == 1 && ended.Training!.End == 5000 && ended.Damage.Count == 1,
            "Changing local character stops training immediately using the latest observed clock, even without another hit");
        switched.StartTraining(6000, 0, "Same entity", "", "");
        switched.Apply(new("character-link", Link: new(2, 21)));
        check(!switched.TrainingActive, "A changed stable character ID stops training even when the entity and display name are unchanged");

        var reused = new CombatEngine();
        reused.Apply(new("local-player", Actor: new(1, "Trainee", true, false, 0, "Cleric", 0)));
        reused.StartTraining(1000, 0, "Reused target", "", "");
        reused.Apply(new("actor", Actor: new(1, "Trainee", true, false, 0, "Cleric", 1) { CharacterId = 50 }));
        check(reused.TrainingActive && reused.TrainingSnapshot(1000)!.Actors[1].CharacterId == 50,
            "Learning previously unknown server and character metadata enriches the training identity without stopping it");
        reused.Apply(new("actor", Actor: new(100, "First dummy", false, false, 0, "Unknown", 0) { NpcId = 10 }));
        reused.Apply(new("damage", Damage: new() { ActorId = 1, TargetId = 100, Timestamp = 2000, Damage = 100 }));
        var beforeReuse = reused.TrainingSnapshot(2001)!;
        reused.NewFight("Ordinary reset");
        reused.Apply(new("actor", Actor: new(100, "Second dummy", false, false, 0, "Unknown", 0) { NpcId = 20 }));
        reused.Apply(new("damage", Damage: new() { ActorId = 1, TargetId = 100, Timestamp = 3000, Damage = 200 }));
        var afterReuse = reused.TrainingSnapshot(3001)!;
        check(afterReuse.Damage[0].TargetId != 100 && afterReuse.Actors[afterReuse.Damage[0].TargetId].NpcId == 10 &&
            afterReuse.Damage[1].TargetId == 100 && afterReuse.Actors[100].NpcId == 20 && beforeReuse.Damage[0].TargetId == 100 && beforeReuse.Actors[100].NpcId == 10,
            "Training preserves separate targets across entity reuse and ordinary fight resets without mutating earlier previews");
        reused.Apply(new("local-player", Actor: new(2, "Next trainee", true, false, 0, "Cleric", 1)));
        var finalReuse = reused.SaveCompleted(_ => { }).Single(f => f.Training != null);
        check(finalReuse.Damage.Count == 2 && finalReuse.Training!.End == 3001,
            "An identity change immediately after a hit retains that hit at the exclusive training end boundary");
    }
}
