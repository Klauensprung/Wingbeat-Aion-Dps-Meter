namespace AionDpsMeter.Local;

internal static class BossCatalogueTests
{
    public static void Run(Action<bool, string> check)
    {
        var catalog = BossCatalogue.Default;
        check(catalog.LoadError == null && catalog.Bosses.Length == 521 && catalog.DungeonCount == 222 && catalog.NpcCount == 1262 && catalog.AmbiguousNpcCount == 0,
            "Entire pinned source catalogue loads from embedded resources without external files");
        var kromede = catalog.Find(2300371)!;
        check(kromede.Name == "Kromede's Desire" && catalog.Find(2310371)?.Key == kromede.Key && catalog.Find(2980269)?.Key == kromede.Key,
            "Dungeon difficulty and Nightmare variants resolve to their source boss family");
        check(kromede.InstanceKeys.Contains("fire-temple") && kromede.InstanceKeys.Contains("nightmare/kromedes-desire") && catalog.Find(int.MaxValue) == null,
            "Catalogue preserves multiple possible instances and never guesses unknown IDs");
        var unassigned = catalog.Bosses.First(b => !b.FieldBoss && b.InstanceKeys.Length == 0);
        check(!catalog.IsEncounterBoss(unassigned.Variants[0].NpcId), "Unassigned hero/legend rank alone cannot classify an encounter boss");
        var fight = new Fight { LocalPlayer = 1 };
        fight.Actors[1] = new(1, "Example", true, true, 0, "Cleric", 1);
        fight.Actors[10] = new(10, "Unresolved name", false, false, 0, "Unknown", 0) { NpcId = 2300371 };
        fight.Actors[11] = new(11, "Unresolved name", false, false, 0, "Unknown", 0) { NpcId = 2310371 };
        fight.Actors[12] = new(12, "Kromede's Desire", false, false, 0, "Unknown", 0);
        foreach (var (actor, target, damage, time) in new[] { (1,10,100,1000L), (1,11,200,2000L), (10,1,50,2100L), (1,12,999,2200L) })
            fight.Damage.Add(new() { ActorId = actor, TargetId = target, Damage = damage, Timestamp = time });
        fight.Deaths.Add(new(10, 2500));
        var row = BossAnalysis.Build(fight).Single();
        check(row.DamageToBoss == 300 && row.DamageFromBoss == 50 && row.Entities == 2 && row.ObservedDeaths == 1 && row.Boss == kromede.Name,
            "Boss analysis groups ID variants, measures both directions, and excludes same-name unknown actors");
        check(fight.Damage.Sum(d => d.Damage) == 1349 && BossAnalysis.Summary(fight).Contains(kromede.Name),
            "Boss analysis annotates history without filtering or altering original fight events");
        CombatEngine Encounter() {
            var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: fight.Actors[1]));
            engine.Apply(new("actor", Actor: fight.Actors[10]));
            engine.Apply(new("damage", Damage: new() { ActorId = 1, TargetId = 10, Timestamp = 1000, Damage = 100 }));
            return engine;
        }
        var adds = Encounter(); string first = adds.Fight.Id;
        adds.AdvanceTime(16000); adds.Apply(new("damage", Damage: new() { ActorId = 1, TargetId = 20, Timestamp = 17000, Damage = 100 }));
        check(adds.Fight.Id == first && adds.Fight.Damage.Count == 2, "Known boss catalogue supports add-phase recovery even for a short initial engagement");
        var next = Encounter(); string previous = next.Fight.Id;
        next.AdvanceTime(16000); next.Apply(new("actor", Actor: new(20, "Another boss", false, false, 0, "Unknown", 0) { NpcId = 2300310 }));
        next.Apply(new("damage", Damage: new() { ActorId = 1, TargetId = 20, Timestamp = 17000, Damage = 100 }));
        check(next.Fight.Id != previous && next.Fight.Damage.Count == 1, "A different catalogued boss after downtime cannot be mistaken for the previous boss's add phase");
    }
}
