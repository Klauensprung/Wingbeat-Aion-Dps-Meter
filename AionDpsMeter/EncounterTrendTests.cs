namespace AionDpsMeter.Local;

internal static class EncounterTrendTests
{
    internal static Fight Attempt(int day, string encounter, int seconds, int damage, int healing, int server = 2301)
    {
        var fight = FeatureTests.Sample(day); fight.Label = encounter;
        fight.Actors[1] = fight.Actors[1] with { Server = server };
        fight.Damage.Clear(); fight.Healing.Clear();
        fight.Damage.Add(new() { ActorId = 1, TargetId = 100, Timestamp = 1000, Damage = damage / 2 });
        fight.Damage.Add(new() { ActorId = 1, TargetId = 100, Timestamp = 1000 + seconds * 1000L, Damage = damage - damage / 2 });
        if (healing > 0) fight.Healing.Add(new() { ActorId = 1, TargetId = 1, Timestamp = 1000, Damage = healing, IsHeal = true });
        return fight;
    }

    internal static void Run(Action<bool, string> check)
    {
        var early = Attempt(1, "Training guardian", 10, 1000, 100);
        var later = Attempt(2, "Training guardian", 30, 9000, 900, 2302);
        var other = Attempt(3, "Training golem", 10, 10000, 500);
        var demo = Attempt(4, "Training guardian", 10, 99000, 9000); demo.Demo = true;
        var idle = Attempt(5, "Training guardian", 10, 0, 0);
        var data = Benchmarks.Trends([other, later, early, demo, idle, later.Copy()]);
        var rows = EncounterTrends.ForCharacter(data, "Example cleric", "Cleric");
        check(rows.Length == 3 && rows.Select(r => r.FightId).SequenceEqual(new[] { early.Id, later.Id, other.Id }),
            "Encounter trends merge server histories, deduplicate imported fights and sort attempts chronologically");
        var summaries = EncounterTrends.Summarize(rows, false);
        var guardian = summaries.Single(r => r.Encounter == "Training guardian");
        check(summaries.Length == 2 && guardian is { Attempts: 2, Average: 200, Latest: 300, Best: 300 } && guardian.FightId == later.Id,
            "Encounter means weight each attempt equally and keep different encounters separate");
        check(summaries[0].Encounter == "Training golem" && summaries[0] is { Attempts: 1, Average: 1000, Latest: 1000, Best: 1000 },
            "Encounter summaries show the most recently fought encounter first, including single attempts");
        check(EncounterTrends.Summarize(rows, true).Single(r => r.Encounter == "Training guardian") is { Average: 20, Latest: 30, Best: 30 },
            "Healing trends use encounter HPS independently of damage and server metadata");
        check(EncounterTrends.ForEncounter(rows, "TRAINING GUARDIAN").Select(r => r.FightId).SequenceEqual(new[] { early.Id, later.Id }),
            "Selected encounter timelines contain only matching attempts in chronological order");
        check(data.All(r => r.FightId != demo.Id && r.FightId != idle.Id && r.Character == "Example cleric"),
            "Demo fights and idle roster members cannot dilute encounter averages");
        var first = rows[0];
        var mixed = rows.Append(first with { FightId = "other-character", Character = "Other cleric" })
            .Append(first with { FightId = "other-class", Job = "Ranger" }).Append(first);
        check(EncounterTrends.ForCharacter(mixed, "EXAMPLE CLERIC", "cleric").Length == 3,
            "Character selection is case-insensitive, excludes other characters and classes, and ignores duplicate attempts");
        var many = Enumerable.Range(1, 120).Select(i => first with { FightId = $"attempt-{i:D3}", Date = first.Date.AddDays(i), DPS = i }).ToArray();
        check(EncounterTrends.Summarize(many, false).Single() is { Attempts: 120, Average: 60.5, Latest: 120, Best: 120 },
            "Encounter averages retain all saved attempts beyond the chart's 100-point display limit");
        var boss = Attempt(6, "Local fight", 10, 3000, 0);
        boss.Actors[100] = boss.Actors[100] with { NpcId = 2300371 };
        check(Benchmarks.Trends([boss]).Single().Encounter == "Kromede's Desire",
            "Trends use recognized boss names instead of the generic capture label");
        check(EncounterTrends.Summarize([], false).Length == 0 && EncounterTrends.ForCharacter([], "Missing", "Cleric").Length == 0,
            "Empty archives and missing character histories produce no fabricated trend rows");
    }
}
