using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class BossEncounterTests
{
    // Illustrative test data, not a claim about any real boss's abilities or timings.
    internal static Fight Fixture()
    {
        var f = new Fight { Demo = true, LocalPlayer = 1, Label = "Illustrative boss encounter" };
        f.Actors[1] = new(1, "Player 1", true, true, 42000, "Cleric", 1);
        f.Actors[2] = new(2, "Player 2", true, true, 43000, "Unknown", 1) { CachedJob = "Ranger" };
        f.Actors[100] = new(100, "NPC 2300371", false, false, 0, "Unknown", 0) { NpcId = 2300371 };
        f.Actors[101] = new(101, "NPC 2310371", false, false, 0, "Unknown", 0) { NpcId = 2310371 };
        f.Actors[200] = new(200, "Example add", false, false, 0, "Unknown", 0) { NpcId = 999999 };
        void Signal(string kind, long timestamp, object fields) => f.Network.Add(new(kind, timestamp, "fixture", JsonSerializer.SerializeToElement(fields)));
        for (int second = 0; second < 60; second++) {
            f.Damage.Add(new() { Timestamp = 100500 + second * 1000, ActorId = 1, TargetId = 100, SkillCode = 17010000, Damage = 1000 + second * 20 });
            f.Damage.Add(new() { Timestamp = 100500 + second * 1000, ActorId = 2, TargetId = 100, SkillCode = 14010000, Damage = 800 + second * 10 });
        }
        for (int second = 0; second < 60; second += 12) {
            Signal("boss-cast", 100000 + second * 1000, new { CasterId = 100, SkillCode = 1501450 });
            foreach (int target in new[] { 1, 2 }) f.Damage.Add(new() { Timestamp = 101000 + second * 1000, ActorId = 100, TargetId = target, SkillCode = 1501450, Damage = 1200 + second * 5 });
        }
        foreach (int second in new[] { 8, 28, 48 }) {
            Signal("boss-cast", 100000 + second * 1000, new { CasterId = 100, SkillCode = 1501531 });
            f.Damage.Add(new() { Timestamp = 102000 + second * 1000, ActorId = 100, TargetId = 1, SkillCode = 1501531, Damage = 3500 });
        }
        f.Damage.Add(new() { Timestamp = 120000, ActorId = 100, TargetId = 2, SkillCode = 9999001, Damage = 5000 });
        f.Damage.Add(new() { Timestamp = 130000, ActorId = 1, TargetId = 200, SkillCode = 17010000, Damage = 999999 });
        f.Damage.Add(new() { Timestamp = 180000, ActorId = 1, TargetId = 101, SkillCode = 17010000, Damage = 777 });
        f.Defense.Add(new() { Timestamp = 123000, ActorId = 100, TargetId = 1, SkillCode = 1230525, Outcome = DefenseOutcome.Block });
        f.Defense.Add(new() { Timestamp = 134000, ActorId = 100, TargetId = 2, SkillCode = 1230525, Outcome = DefenseOutcome.Evade });
        for (int second = 0; second <= 60; second += 10) Signal("hp", 100000 + second * 1000, new { EntityId = 100, CurrentHp = 600000 - second * 10000 });
        Signal("boss-enrage", 148000, new { EntityId = 100, AbnormalId = 1728 });
        f.Deaths.AddRange([new(2, 150000), new(2, 181000), new(100, 160000)]);
        return f;
    }
    internal static void Run(Action<bool, string> check)
    {
        var names = new SkillNames(); var f = Fixture();
        var rows = BossEncounterAnalysis.Build(f, names.GetSkillName); var boss = rows[0];
        check(rows.Length == 2 && rows[0].EntityId == 100 && rows[1].EntityId == 101 && rows[0].Name == rows[1].Name,
            "Boss detail keeps same-family entities separate and orders by observed engagement");
        check(boss.Start == 100000 && boss.End == 160000 && boss.Seconds == 60 && boss.DeathObserved,
            "Boss window includes pre-impact casts and the observed death instead of unrelated encounter duration");
        check(boss.DamageToBoss == f.Damage.Where(h => h.TargetId == 100).Sum(h => (long)h.Damage) && boss.DamageFromBoss == 28700 && boss.Casts == 8,
            "Direct boss totals exclude adds and other boss entities");
        var arrows = boss.Abilities.Single(a => a.SkillCode == 1501450);
        check(arrows.Casts == 5 && arrows.Hits == 10 && arrows.Targets == 2 && arrows.CastGap == 12 && arrows.BiggestHit == 1440,
            "AoE impacts retain targets and hit counts without inflating cast counts or measured cast gaps");
        check(boss.Targets.Where(t => t.SkillCode == 1501450).All(t => t.Hits == 5 && t.Share == 50) && boss.Targets.Sum(t => t.Damage) == boss.DamageFromBoss,
            "Per-skill target breakdown preserves AoE damage and each target's share");
        check(boss.Abilities.Single(a => a.SkillCode == 9999001) is { Casts: 0, CastGap: null } unknown && unknown.Skill.Contains("9999001") &&
            boss.Abilities.Single(a => a.SkillCode == 1230525) is { Hits: 0, Blocks: 1, Avoids: 1 },
            "Unknown skill IDs and defense-only abilities remain visible without invented casts");
        check(boss.Participants.Length == 2 && boss.Participants[0].DPS == boss.Participants[0].Damage / 60d &&
            boss.Participants.Single(p => p.Id == 2) is { Class: "Ranger", Deaths: 1 } && Math.Abs(boss.Participants.Sum(p => p.Share) - 100) < .001,
            "Boss participants use cached classes, boss-window DPS and window deaths without attributing a killer");
        check(boss.Hp.Length == 7 && boss.Hp[^1].Value == 0 && boss.Moments.Count(m => m.Kind == "Enrage") == 1 &&
            boss.Moments.Zip(boss.Moments.Skip(1)).All(p => p.First.Timestamp <= p.Second.Timestamp),
            "HP including zero, enrage and chronological actions survive boss analysis");
        check(boss.Bins.Length <= 120 && boss.Bins.All(b => b.Width > 0) && boss.Bins.Sum(b => b.ToBoss) == boss.DamageToBoss &&
            boss.Bins.Sum(b => b.FromBoss) == boss.Abilities.Sum(a => a.Damage), "Damage chart bins preserve totals and safe denominators");
        f.Network.Add(f.Network.First(e => e.Kind == "boss-cast"));
        f.Network.Add(new("boss-cast", 110000, "fixture", JsonSerializer.SerializeToElement(new { CasterId = "bad", SkillCode = 1501450 })));
        f.Network.Add(new("hp", 110000, "fixture", JsonSerializer.SerializeToElement(new { EntityId = 100, CurrentHp = -1 })));
        f.Network.Add(new("boss-cast", 110000, "fixture", JsonSerializer.SerializeToElement(new { CasterId = 1, SkillCode = 1501450 })));
        check(BossEncounterAnalysis.Build(f, names.GetSkillName)[0].Casts == 8 && BossEncounterAnalysis.Build(f, names.GetSkillName).Length == 2,
            "Duplicate cast packets, malformed fields and player cast signals do not fabricate boss actions");
        var hitsOnly = Fixture(); hitsOnly.Network.Clear();
        check(BossEncounterAnalysis.Build(hitsOnly, names.GetSkillName)[0] is { Casts: 0, Hp.Length: 0 } hitData && hitData.Abilities.All(a => a.CastGap == null),
            "Hit-only archives remain analysable without cast or HP claims");
        var castOnly = new Fight(); castOnly.Network.Add(new("boss-cast", 900, "fixture", JsonSerializer.SerializeToElement(new { CasterId = 300, SkillCode = 1501450 })));
        var only = BossEncounterAnalysis.Build(castOnly, names.GetSkillName).Single();
        check(only.EntityId == 300 && only.Name == "Entity 300" && only.NpcId == 0 && only.Casts == 1 && only.DamageFromBoss == 0 && only.Seconds == 1,
            "Boss-cast evidence supports unnamed entities and cast-only encounters without dividing by zero");
        var nearby = new Fight(); nearby.Actors[100] = Fixture().Actors[100];
        nearby.Network.Add(new("hp", 1000, "fixture", JsonSerializer.SerializeToElement(new { EntityId = 100, CurrentHp = 1000 })));
        check(BossEncounterAnalysis.Build(nearby, names.GetSkillName).Length == 0, "Nearby boss HP alone does not fabricate a combat encounter");
        var reused = Fixture(); const int archivedId = int.MaxValue;
        reused.Actors[archivedId] = reused.Actors[100] with { Id = archivedId, OriginalEntityId = 100 };
        reused.Damage.Add(new() { ActorId = 1, TargetId = archivedId, Timestamp = 1000, Damage = 555 });
        var reusedRows = BossEncounterAnalysis.Build(reused, names.GetSkillName);
        check(reusedRows.Where(b => b.DisplayEntityId == 100).Count() == 2 && reusedRows.Where(b => b.DisplayEntityId == 100).All(b => b.AmbiguousNetworkIdentity && b.Casts == 0 && b.Hp.Length == 0) &&
            reusedRows.Single(b => b.EntityId == archivedId).DamageToBoss == 555,
            "Reused network entity IDs cannot assign ambiguous cast or HP packets to the wrong boss identity");
        var longFight = Fixture();
        for (int i = 0; i < 40000; i++) longFight.Damage.Add(new() { Timestamp = 100000 + i * 1000L, ActorId = 100, TargetId = 1, SkillCode = 1501450, Damage = int.MaxValue });
        var large = BossEncounterAnalysis.Build(longFight, names.GetSkillName)[0];
        check(large.DamageFromBoss > int.MaxValue && large.Bins.Length <= 120 && large.Moments.Count(m => m.Kind == "Hit") == 40014,
            "Long boss fights retain every inspectable hit, bound chart size and use 64-bit totals");
    }
}
