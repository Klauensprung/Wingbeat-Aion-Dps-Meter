namespace AionDpsMeter.Local;

internal static class CapturedClassTests
{
    internal static void Run(Action<bool, string> check)
    {
        var groups = new Dictionary<int, string> { [5] = "Gladiator", [9] = "Templar", [13] = "Ranger", [17] = "Assassin",
            [21] = "Spiritmaster", [25] = "Sorcerer", [29] = "Cleric", [33] = "Chanter", [45] = "Brawler" };
        check(groups.All(p => Enumerable.Range(p.Key, 4).All(id => CapturedClass.Name(id) == p.Value)) &&
            new[] { -1, 0, 1, 4, 37, 44, 49, int.MaxValue }.All(id => CapturedClass.Name(id) == "Unknown"),
            "Captured roster classes map all known template variants while base and unknown groups stay unknown");
        var engine = new CombatEngine();
        var owner = new Actor(1, "Owner", true, false, 0, "Unknown", 2301);
        engine.Apply(new("actor", Actor: owner));
        engine.Apply(new("actor", Actor: new(10, "Summon", false, false, 0, "Unknown", 0) { NpcId = 2000001, OwnerId = 1 }));
        engine.RecordDamage(new() { ActorId = 10, TargetId = 20, Timestamp = 1000, SkillCode = 11010000, Damage = 100 }, false);
        check(ClassEvidence.Infer(engine.Fight).Count == 0 && engine.Snapshot().Actors[1].CachedJob == "Unknown",
            "Attributed summon skills do not infer or cache a class for the owner");
        engine.Apply(new("party-profiles", Profiles: [new("Owner", 2301, 10000, 0)]));
        engine.Apply(new("actor", Actor: owner with { Cp = 20000, CpSource = "Captured party", Job = "Spiritmaster" }));
        check(engine.Snapshot().Actors[1] is { Cp: 20000, Job: "Spiritmaster", CpSource: "Captured party" },
            "Current captured roster class and CP outrank older party-finder profile evidence");
        engine.Apply(new("actor", Actor: owner with { Cp = 0, CpSource = "Captured party" }));
        check(engine.Snapshot().Actors[1] is { Cp: 0, CpSource: "Captured party" },
            "A captured zero CP is not silently replaced with a stale profile or cached value");
        engine.Fight.LocalPlayer = 1;
        engine.Apply(new("actor", Actor: owner with { Cp = 30000, CpSource = "Local character packet" }));
        engine.Apply(new("actor", Actor: owner with { Cp = 20000, CpSource = "Captured party" }));
        check(engine.Fight.Actors[1] is { Cp: 30000, CpSource: "Local character packet" },
            "Local character CP remains authoritative over a party-roster copy");
        engine.Apply(new("actor", Actor: owner with { Cp = 40000, CpSource = "Entered locally" }));
        engine.Apply(new("actor", Actor: owner with { Cp = 20000, CpSource = "Captured party" }));
        check(engine.Fight.Actors[1] is { Cp: 40000, CpSource: "Entered locally" },
            "Explicit local CP override remains authoritative over captured roster data");
    }
}
