namespace AionDpsMeter.Local;

internal static class PvpOpponentTests
{
    internal static void Run(Action<bool, string> check)
    {
        var fight = PvpTests.Duel();
        fight.Actors[4] = new(4, "Party example", true, true, 0, "Cleric", 1);
        fight.Actors[3] = new(3, "Other opponent", true, false, 0, "Ranger", 1);
        fight.Damage.Add(new() { ActorId = 4, TargetId = 2, SkillCode = 17010001, Timestamp = 18000, Damage = 600 });
        fight.Damage.Add(new() { ActorId = 2, TargetId = 4, SkillCode = 12010001, Timestamp = 17000, Damage = 500 });
        fight.Damage.Add(new() { ActorId = 3, TargetId = 1, SkillCode = 12010001, Timestamp = 100000, Damage = 90000 });
        fight.Damage.Add(new() { ActorId = 1, TargetId = 9, SkillCode = 17010001, Timestamp = 200000, Damage = 90000 });
        var own = PvpOpponentAnalysis.Build(fight, 2, false, code => "Skill " + code);
        check(own.DamageToOpponent == 100 && own.DamageFromOpponent == 3000 && own.ToOpponent.Seconds == 10 && own.FromOpponent.Seconds == 10,
            "Opponent drill-down isolates the direct exchange and excludes other enemies, allies and PvE from both timelines");
        var party = PvpOpponentAnalysis.Build(fight, 2, true, code => "Skill " + code);
        check(party.DamageToOpponent == 700 && party.DamageFromOpponent == 3500 &&
            party.Skills.Where(s => s.Direction == "To opponent").Sum(s => s.Damage) == 700 &&
            party.Skills.Where(s => s.Direction == "From opponent").Sum(s => s.Damage) == 3500 &&
            party.Skills.Where(s => s.Direction == "To opponent").Sum(s => s.DPS) == 70,
            "Party exchange skill rows reconcile to directional totals and use the same exchange-wide DPS clock");
        var to = EncounterTimeline.Build(party.ToOpponent, new[] { 1, 4 });
        var from = EncounterTimeline.Build(party.FromOpponent, new[] { 2 });
        check(to.Sum(b => b.Damage) == 700 && from.Sum(b => b.Damage) == 3500 && to.Select(b => (b.From, b.To)).SequenceEqual(from.Select(b => (b.From, b.To))),
            "Opponent timeline buckets preserve both totals with matching time boundaries");
        fight.Damage.Add(new() { ActorId = 1, TargetId = 2, Timestamp = 7210000, Damage = 1 });
        var longExchange = PvpOpponentAnalysis.Build(fight, 2, false, code => code.ToString());
        check(EncounterTimeline.Build(longExchange.ToOpponent, new[] { 1 }).Length == 120 && own.DamageToOpponent == 100,
            "Long opponent timelines remain bounded and new hits do not alter an earlier exchange snapshot");
        bool rejected = false;
        try { PvpOpponentAnalysis.Build(fight, 9, true, code => code.ToString()); } catch (ArgumentException) { rejected = true; }
        check(rejected, "Opponent inspection rejects known NPC targets");
    }
}
