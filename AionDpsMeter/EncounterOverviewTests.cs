namespace AionDpsMeter.Local;

internal static class EncounterOverviewTests
{
    internal static void Run(Action<bool, string> check)
    {
        var fight = new Fight { LocalPlayer = 1 };
        fight.Actors[1] = new(1, "Example", true, true, 0, "Cleric", 1);
        fight.Actors[2] = new(2, "Example", true, true, 0, "Gladiator", 2);
        fight.Actors[3] = new(3, "Healer", true, true, 0, "Cleric", 1);
        fight.Actors[10] = new(10, "Boss", false, false, 0, "Unknown", 0) { NpcId = 2300371 };
        fight.Actors[11] = new(11, "Add", false, false, 0, "Unknown", 0) { NpcId = 1 };
        fight.Damage.Add(new() { ActorId = 1, TargetId = 10, SkillCode = 17010001, Timestamp = 1000, Damage = 100 });
        fight.Damage.Add(new() { ActorId = 2, TargetId = 10, SkillCode = 17010001, Timestamp = 2000, Damage = 200 });
        fight.Damage.Add(new() { ActorId = 1, TargetId = 11, SkillCode = 17010001, Timestamp = 2000, Damage = 50 });
        fight.Damage.Add(new() { ActorId = 10, TargetId = 1, Timestamp = 2000, Damage = 500 });
        fight.Healing.Add(new() { ActorId = 3, TargetId = 1, Timestamp = 2000, Damage = 70, IsHeal = true });
        fight.Deaths.Add(new(1, 2100));
        fight.Actors[4] = new(4, "Entity 4", true, true, 0, "Cleric", 1);
        fight.Actors[5] = new(5, "   ", true, false, 0, "Unknown", 0);
        fight.Actors[6] = new(6, "Summon", false, false, 0, "Unknown", 0);
        foreach (int id in new[] { 4, 5, 6, 7 })
            fight.Damage.Add(new() { ActorId = id, TargetId = 10, Timestamp = 2000, Damage = 10000 });
        fight.Actors[3] = fight.Actors[3] with { Job = "Unknown", CachedJob = "Cleric" };
        var people = EncounterOverview.Participants(fight);
        check(people.Single(p => p.Id == 3).Class == "Cleric", "Encounter class display resolves cached class when captured class and skill evidence are unavailable");
        check(people.Length == 3 && people.Sum(p => p.Damage) == 350 && people.Single(p => p.Id == 3).Healing == 70 && people.Single(p => p.Id == 1).Deaths == 1,
            "Encounter participants retain add damage, healing-only players and deaths while excluding known enemies");
        check(people.Single(p => p.Id == 2).DamagePercentOfLeader == 100 && people.Single(p => p.Id == 1).DamagePercentOfLeader == 75 && people.Single(p => p.Id == 3).DamagePercentOfLeader == 0,
            "Encounter damage backdrops scale to the leading participant, excluding enemy damage");
        var healingOnly = fight.Copy(); healingOnly.Damage.Clear();
        check(EncounterOverview.Participants(healingOnly).All(p => p.DamagePercentOfLeader == 0) && EncounterOverview.Participants(new Fight()).Length == 0,
            "Healing-only and empty encounters have no damage backdrop");
        check(people.Select(p => p.Id).SequenceEqual(new[] { 2, 1, 3 }) && people.Select(p => p.DPS).SequenceEqual(people.Select(p => p.DPS).OrderDescending()),
            "Encounter ranks only named players by descending DPS, retaining named healers");
        var named = fight.Copy(); named.Actors[4] = named.Actors[4] with { Name = "New player" };
        check(EncounterOverview.Participants(named).First().Id == 4 && EncounterOverview.Participants(fight).All(p => p.Id != 4),
            "An unnamed player appears in DPS order when their captured name arrives");
        check(EncounterOverview.Skills(fight, fight.Damage.Select(h => h.ActorId), _ => "Skill").All(s => s.ActorId is 1 or 2),
            "Encounter skill breakdown excludes unnamed actors and NPCs even with unfiltered actor IDs");
        var all = EncounterOverview.Skills(fight, people.Select(p => p.Id), code => "Example skill");
        check(all.Length == 2 && all.Select(s => s.ActorId).Distinct().Count() == 2 && all.Sum(s => s.Damage) == 350 && Math.Abs(all.Sum(s => s.DamageShare) - 100) < .001,
            "Encounter skill attribution distinguishes same-name players and totals match participant damage");
        var one = EncounterOverview.Skills(fight, new[] { 1 }, code => "Example skill");
        check(one.Single() is { ActorId: 1, Damage: 150, Hits: 2, DamageShare: 100 }, "Participant filter preserves both boss and add hits in their skill breakdown");
        check(EncounterOverview.Title(fight) == "Kromede's Desire" && EncounterOverview.Title(new Fight { Label = "Training" }) == "Training",
            "Encounter titles use verified NPC catalogue matches and retain labels for unknown encounters");
    }
}
