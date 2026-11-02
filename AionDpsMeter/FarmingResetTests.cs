using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;
namespace AionDpsMeter.Local;
internal static class FarmingResetTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var paths = Enumerable.Range(0, 6).Select(i => Path.Combine(temp, $"farming-reset-{i}.json")).ToArray();
        var ledgers = paths.Select(p => new FarmingCounterLedger(p)).ToArray();
        var engine = new CombatEngine(killTracking: ledgers[0], gatheringTracking: ledgers[1], lootTracking: ledgers[2],
            soulTracking: ledgers[3], petRegistrationTracking: ledgers[4], petLevelTracking: ledgers[5]);
        engine.Apply(new("local-player", Actor: new(1, "Self", true, true, 0, "Cleric", 2301)));
        void Send(string kind, long at, object fields) => engine.Apply(new("network", Network: new(kind, at, "synthetic", JsonSerializer.SerializeToElement(fields))));
        InventoryStack Stack(long count) => new(1, 110120003, count, 1, 1, 5);
        Send("inventory", 1, new InventoryBatch(true, [Stack(20)]));
        Send("pet-roster", 2, new CapturedPetRoster(0, [new(1002, 1)], [], 0));
        Send("npc-spawn", 3, new { EntityId = 99, NpcId = 100 });
        engine.RecordDamage(new() { Timestamp = 4, ActorId = 1, TargetId = 99, SkillCode = 17010000, Damage = 10 }, false);
        foreach (var ledger in ledgers) { ledger.Record(9999, 7); ledger.Save(); }
        engine.ResetFarmingTracking(5);
        check(ledgers.All(l => l.Snapshot() is { Since: 5, Omitted: 0, Counts.Count: 0 }), "Reset all clears all six farming categories at one start time");
        foreach (var ledger in ledgers) ledger.Save();
        check(paths.All(p => new FarmingCounterLedger(p).Snapshot() is { Since: 5, Counts.Count: 0 }), "Combined farming reset persists every category");
        Send("inventory-update", 6, new InventoryBatch(false, [Stack(23)]));
        Send("pet-progress-decrease", 7, new { Gains = new[] { new CapturedPetProgress(1002, 25) } });
        Send("pet-added", 8, new CapturedPetLevel(1002, 2));
        Send("entity-death", 9, new { EntityId = 99 });
        check(ledgers[2].Snapshot().Counts.GetValueOrDefault(110120003) == 3, "Combined reset preserves inventory baseline for subsequent acquired quantities");
        check(ledgers[5].Snapshot().Counts.GetValueOrDefault(1002) == 1 && ledgers[4].Snapshot().Counts.Count == 0,
            "Combined reset preserves pet baseline without counting existing registration or duplicate upgrade confirmation");
        check(ledgers[0].Snapshot().Counts.GetValueOrDefault(100) == 1 && engine.Fight.Damage.Count == 1,
            "Combined reset preserves engagement and current fight damage");
    }
}
