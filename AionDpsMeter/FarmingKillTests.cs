using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class FarmingKillTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var tracker = new FarmingKillTracker(); var counts = new Dictionary<int, long>(); var fixtures = new List<object>(); long at = 0;
        void Step(string kind, int entity = 10, int npc = 100, int actor = 1, int hp = 0, string reason = "died", int owner = 0) {
            at++; int? killed = null;
            if (kind == "damage") tracker.Hit(entity, at, actor == 1 || actor == 2);
            else {
                string name = kind switch { "spawn" => "npc-spawn", "death" => "entity-death", "removed" => "entity-removed", "map" => "local-zone", _ => kind };
                killed = tracker.Apply(new(name, at, "synthetic", JsonSerializer.SerializeToElement(new { EntityId = entity, NpcId = npc, OwnerId = owner, CurrentHp = hp, Removal = reason, MapId = 1010 })));
            }
            if (killed is int id) counts[id] = counts.GetValueOrDefault(id) + 1;
            fixtures.Add(new { Kind = kind, At = at, Entity = entity, Npc = npc, Actor = actor, Hp = hp, Reason = reason, Owner = owner, Expected = new Dictionary<int, long>(counts) });
        }
        Step("identity"); Step("spawn"); Step("damage", actor: 3); Step("death");
        check(counts.Count == 0, "Nearby combat without party engagement cannot count as a farming kill");
        Step("spawn"); Step("damage"); Step("hp"); Step("death"); Step("removed");
        check(counts.GetValueOrDefault(100) == 1, "Zero HP, death and died-removal notifications count one engaged NPC once");
        Step("spawn", entity: 20, npc: 200); Step("damage", entity: 20, actor: 2); Step("removed", entity: 20, reason: "despawned");
        check(!counts.ContainsKey(200), "Engaged NPC despawn without a death reason does not invent a kill");
        Step("spawn", entity: 30, npc: 300); Step("damage", entity: 30, actor: 2); Step("removed", entity: 30);
        check(counts.GetValueOrDefault(300) == 1, "Party engagement followed by explicit died removal counts a farming kill");
        Step("spawn", entity: 40, npc: 400, owner: 1); Step("damage", entity: 40); Step("death", entity: 40);
        check(!counts.ContainsKey(400), "Owned summons are excluded from farming NPC kills");
        Step("spawn", entity: 50, npc: 500); Step("damage", entity: 50); Step("map"); Step("death", entity: 50);
        check(!counts.ContainsKey(500), "Map changes discard engagement evidence instead of attributing an old entity death");
        Step("identity"); Step("spawn", entity: 60, npc: 600); Step("damage", entity: 60); Step("death", entity: 60);
        Step("hp", entity: 60, hp: 100); Step("damage", entity: 60); Step("death", entity: 60);
        check(counts.GetValueOrDefault(600) == 2, "Fresh HP followed by new party engagement permits a subsequent confirmed kill");
        check(tracker.Snapshot().OrderBy(p => p.Key).SequenceEqual(counts.OrderBy(p => p.Key)),
            "Tracker's bounded live kill totals agree with independently accumulated confirmed outcomes");
        string ledgerPath = Path.Combine(temp, "farming-kills.json");
        var killLedger = new FarmingCounterLedger(ledgerPath);
        var engine = new CombatEngine(killTracking: killLedger);
        engine.Apply(new("local-player", Actor: new(1, "Self", true, true, 0, "Cleric", 2301)));
        void Network(string kind, long time, object fields) => engine.Apply(new("network", Network: new(kind, time, "synthetic", JsonSerializer.SerializeToElement(fields))));
        Network("npc-spawn", 10, new { EntityId = 99, NpcId = 100 });
        engine.RecordDamage(new() { Timestamp = 11, ActorId = 1, TargetId = 99, SkillCode = 17010000, Damage = 10 }, false);
        engine.NewFight("Manual reset"); Network("hp", 12, new { EntityId = 99, CurrentHp = 0 });
        Network("entity-death", 13, new { EntityId = 99 });
        check(engine.FarmingKills.Snapshot().GetValueOrDefault(100) == 1,
            "Live ingestion counts party-engaged deaths across DPS reset without duplicating HP/death signals");
        Network("account-login", 20, new LoginReading(0, 2301, []));
        Network("npc-spawn", 21, new { EntityId = 99, NpcId = 200 });
        engine.RecordDamage(new() { Timestamp = 22, ActorId = 1, TargetId = 99, SkillCode = 17010000, Damage = 10 }, false);
        Network("entity-death", 23, new { EntityId = 99 });
        check(engine.FarmingKills.Snapshot().Count == 1,
            "Pending identity cannot mark mobs as engaged using the previous local actor");
        killLedger.Save(); var restoredKills = new FarmingCounterLedger(ledgerPath);
        check(restoredKills.LoadError == null && restoredKills.Snapshot().Counts.GetValueOrDefault(100) == 1 && restoredKills.Snapshot().Counts.Count == 1,
            "Live ingestion persists qualified kills once while pending-identity combat contributes nothing");
        engine = new CombatEngine(killTracking: restoredKills);
        engine.Apply(new("local-player", Actor: new(1, "Self", true, true, 0, "Cleric", 2301)));
        Network("npc-spawn", 30, new { EntityId = 99, NpcId = 100 });
        engine.RecordDamage(new() { Timestamp = 31, ActorId = 1, TargetId = 99, SkillCode = 17010000, Damage = 10 }, false);
        Network("entity-death", 32, new { EntityId = 99 });
        check(restoredKills.Snapshot().Counts[100] == 2, "Restarted capture continues saved kill totals without importing fight history");
        Network("npc-spawn", 33, new { EntityId = 98, NpcId = 200 });
        engine.RecordDamage(new() { Timestamp = 34, ActorId = 1, TargetId = 98, SkillCode = 17010000, Damage = 10 }, false);
        var priorTotals = restoredKills.Snapshot(); restoredKills.Reset(35);
        Network("entity-death", 36, new { EntityId = 98 }); restoredKills.Save();
        var afterReset = new FarmingCounterLedger(ledgerPath).Snapshot();
        check(afterReset.Since == 35 && afterReset.Counts.Count == 1 && afterReset.Counts[200] == 1 && priorTotals.Counts[100] == 2 && engine.Fight.Damage.Count == 2,
            "Persisted kill reset starts counters again without discarding live engagement or fight damage");
        File.WriteAllText(Path.Combine(temp, "reference-farming-kills.json"), JsonSerializer.Serialize(fixtures));
        tracker.Clear(100); tracker.Apply(new("npc-spawn", 101, "synthetic", JsonSerializer.SerializeToElement(new { EntityId = 1, NpcId = 999 })));
        tracker.Hit(1, 99, true);
        check(tracker.Apply(new("entity-death", 102, "synthetic", JsonSerializer.SerializeToElement(new { EntityId = 1 }))) == null,
            "Old-scene damage cannot mark a reused entity as engaged for kill attribution");
        for (int i = 1; i <= 4200; i++) tracker.Apply(new("npc-spawn", 200 + i, "synthetic", JsonSerializer.SerializeToElement(new { EntityId = i, NpcId = 100 })));
        check(tracker.RetainedEntries == 4096, "Farming NPC evidence and removal tombstones have bounded retention");
    }
}
