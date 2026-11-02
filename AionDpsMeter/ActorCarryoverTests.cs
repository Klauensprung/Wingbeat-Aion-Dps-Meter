using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class ActorCarryoverTests
{
    internal static void Run(Action<bool, string> check)
    {
        var cache = new ActorCarryover(); var fight = new Fight { LocalPlayer = 1 };
        for (int id = 1; id <= 5000; id++) {
            fight.Actors[id] = new(id, "Entity " + id, id <= 2, id == 2, 0, "Unknown", 1);
            cache.Observe(id);
        }
        var selected = cache.Select(fight);
        check(selected.Count == ActorCarryover.Limit + 2 && selected.ContainsKey(1) && selected.ContainsKey(2) &&
            selected.ContainsKey(5000) && !selected.ContainsKey(3) && fight.Actors.Count == 5000,
            "Carryover bounds recent identities while retaining local/party actors and leaving the original fight untouched");
        cache.Observe(3); selected = cache.Select(fight);
        check(selected.ContainsKey(3) && !selected.ContainsKey(905), "An observed older actor becomes recent and displaces the least recently used identity");
        fight.Actors[5000] = fight.Actors[5000] with { OriginalEntityId = 3 };
        check(!cache.Select(fight).ContainsKey(5000), "Archived reused-entity aliases remain in history rather than entering the next fight");
        cache.Clear(); check(cache.Select(fight).Keys.Order().SequenceEqual(new[] { 1, 2 }),
            "Clearing carryover drops unrelated identities while preserving required party metadata");
        var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: new(1, "Local", true, true, 0, "Cleric", 1)));
        engine.Apply(new("actor", Actor: new(2, "Old named target", false, false, 0, "Unknown", 0) { NpcId = 2000001 }));
        engine.Apply(new("damage", Damage: new DamageEvent { ActorId = 1, TargetId = 2, Damage = 1, Timestamp = 1000 }));
        for (int id = 3; id < 4200; id++) engine.Apply(new("actor", Actor: new(id, "Recent target", false, false, 0, "Unknown", 0) { NpcId = 2000002 }));
        var snapshot = engine.Snapshot(); engine.NewFight("Reset");
        check(snapshot.Name(2) == "Old named target" && snapshot.Damage.Count == 1 && !engine.Fight.Actors.ContainsKey(2) &&
            engine.Fight.Actors.Count <= ActorCarryover.Limit + 1,
            "Engine reset evicts stale carryover without removing identities from already captured damage snapshots");
        engine.Apply(new("damage", Damage: new DamageEvent { ActorId = 1, TargetId = 4199, Damage = 1, Timestamp = 2000 }));
        check(engine.Fight.Name(4199) == "Recent target", "Recent targets retain their captured identity when combat resumes after reset");
    }
}
