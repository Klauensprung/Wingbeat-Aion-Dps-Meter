using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class FarmingLootTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var totals = new FarmingCounterLedger(); var tracker = new FarmingLootTracker(totals); var fixtures = new List<object>(); long at = 0;
        void Batch(bool full, params InventoryStack[] rows) {
            var batch = new InventoryBatch(full, rows); tracker.Apply(new(full ? "inventory" : "inventory-update", ++at, "synthetic", JsonSerializer.SerializeToElement(batch)));
            fixtures.Add(new { Reset = false, Batch = batch, Expected = totals.Snapshot().Counts });
        }
        InventoryStack Stack(long instance, long count, byte reason = 5, byte modification = 1) => new(instance, 110120003, count, 1, modification, reason);
        Batch(false, Stack(10, 10)); check(totals.Snapshot().Counts.Count == 0, "Farming loot does not assume an unknown initial stack was newly acquired");
        Batch(false, Stack(10, 12)); Batch(true, Stack(10, 20), Stack(11, 30)); Batch(false, Stack(10, 23));
        check(totals.Snapshot().Counts[110120003] == 5, "Farming loot counts positive acquired quantities while full inventory only sets baseline");
        Batch(true, Stack(11, 30)); Batch(false, Stack(10, 24));
        check(totals.Snapshot().Counts[110120003] == 29, "Replacement farming inventory forgets omitted instances independently of dungeon depot merging");
        Batch(false, Stack(10, 40, 0)); Batch(false, Stack(10, 41)); Batch(false, Stack(10, 999, modification: 2)); Batch(false, Stack(10, 3)); Batch(false, Stack(10, 3));
        check(totals.Snapshot().Counts[110120003] == 33, "Non-loot updates advance baseline, removal clears its count, and repeated updates do not duplicate farming loot");
        totals.Reset(at); fixtures.Add(new { Reset = true, Batch = (InventoryBatch?)null, Expected = totals.Snapshot().Counts }); Batch(false, Stack(10, 5));
        check(totals.Snapshot().Counts[110120003] == 2, "Resetting farming loot keeps the current inventory baseline");
        long amount = 5;
        for (int reason = 0; reason <= 255; reason++) Batch(false, Stack(10, ++amount, (byte)reason));
        check(totals.Snapshot().Counts[110120003] == 23, "Farming source classification counts 21 captured loot reasons across all byte values");
        File.WriteAllText(Path.Combine(temp, "reference-farming-loot.json"), JsonSerializer.Serialize(fixtures));
        var boundedTotals = new FarmingCounterLedger(); var bounded = new FarmingLootTracker(boundedTotals);
        void Large(bool full, long stamp, InventoryStack[] rows) => bounded.Apply(new(full ? "inventory" : "inventory-update", stamp, "synthetic", JsonSerializer.SerializeToElement(new InventoryBatch(full, rows))));
        Large(true, 1, Enumerable.Range(1, 8192).Select(i => Stack(i, 1)).ToArray());
        Large(false, 2, Enumerable.Range(8193, 8192).Select(i => Stack(i, 1, 0)).ToArray());
        Large(false, 3, [Stack(16385, 4, modification: 0)]); Large(false, 4, [Stack(16386, 5, modification: 0)]);
        check(bounded.RetainedStacks == 0 && boundedTotals.Snapshot().Counts.Count == 0, "Farming inventory overflow suppresses partial and subsequent gains until a fresh full baseline");
        Large(true, 5, []); Large(false, 6, [Stack(1, 3)]);
        check(boundedTotals.Snapshot().Counts[110120003] == 3, "Fresh full inventory recovers bounded farming loot tracking");
        var wide = new FarmingCounterLedger(); wide.Record(1, long.MaxValue); wide.Record(1, 3);
        for (int id = 2; id <= 4096; id++) wide.Record(id);
        wide.Record(4097, 9);
        check(wide.Snapshot().Counts[1] == long.MaxValue && wide.Snapshot().Omitted == 12,
            "Farming quantity overflow and excess categories preserve representable counts and omitted quantities without crashing");
        string path = Path.Combine(temp, "farming-loot.json"); var persisted = new FarmingCounterLedger(path); var engine = new CombatEngine(lootTracking: persisted);
        void Send(string kind, long stamp, object value) => engine.Apply(new("network", Network: new(kind, stamp, "synthetic", JsonSerializer.SerializeToElement(value))));
        Send("inventory", 1, new InventoryBatch(true, [Stack(1, 20)])); Send("inventory-update", 2, new InventoryBatch(false, [Stack(1, 25)]));
        engine.NewFight("Reset DPS"); Send("inventory-update", 3, new InventoryBatch(false, [Stack(1, 27)])); persisted.Save();
        check(new FarmingCounterLedger(path).Snapshot().Counts[110120003] == 7, "Live farming loot quantities persist independently of DPS reset and fight history");
        Send("account-login", 10, new LoginReading(0, 2301, [])); Send("inventory-update", 9, new InventoryBatch(false, [Stack(1, 100)]));
        Send("inventory-update", 11, new InventoryBatch(false, [Stack(1, 200)])); Send("inventory-update", 12, new InventoryBatch(false, [Stack(1, 202)]));
        check(persisted.Snapshot().Counts[110120003] == 9, "Login clears farming inventory baseline and rejects stale traffic without resetting cumulative quantities");
    }
}
