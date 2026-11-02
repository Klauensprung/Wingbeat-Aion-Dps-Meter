using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class DungeonItemUseTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var actor = new Actor(1, "Synthetic", true, false, 0, "Cleric", 2301);
        NetworkEvent Inventory(long at, bool full, long count, byte reason = 14, byte increase = 0, byte modification = 1) =>
            new(full ? "inventory" : "inventory-update", at, "test", JsonSerializer.SerializeToElement(new InventoryBatch(full, [new(100, 110120003, count, 1, modification, increase, reason)])));
        var tracker = new InventoryLootTracker();
        tracker.Apply(Inventory(1, false, 9), actor); check(tracker.Uses.Length == 0, "Unknown initial stack counts cannot invent consumed items");
        tracker.Apply(Inventory(2, true, 10), actor);
        tracker.Apply(Inventory(3, false, 7), actor); check(tracker.Uses is [{ Count: 3 }], "Known inventory decrease with use reason records the exact quantity");
        tracker.Apply(Inventory(4, false, 6, 14, 5), actor); check(tracker.Uses.Length == 0, "Loot-source reason takes precedence over use reason as in reference normalization");
        tracker.Apply(Inventory(5, false, 123, 14, modification: 2), actor); check(tracker.Uses is [{ Count: 6 }], "Removed stacks consume their known previous quantity rather than the removal packet count");
        tracker.Apply(Inventory(4, false, 0), actor); check(tracker.Uses.Length == 0, "Stale packets clear transient use output and cannot repeat a consumption");
        var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: actor));
        void Apply(NetworkEvent e) => engine.Apply(new("network", Network: e));
        Apply(Inventory(10, true, 20)); Apply(Inventory(11, false, 19));
        check(engine.Fight.DungeonRun == null, "Outside-dungeon consumption does not fabricate a run");
        Apply(new("dungeon-state", 20, "test", JsonSerializer.SerializeToElement(new DungeonStateReading(600002, 2, null, null))));
        Apply(Inventory(21, false, 16)); var before = engine.Snapshot();
        engine.NewFight("Same run"); Apply(Inventory(22, false, 14, 38));
        check(before.DungeonRun!.UsedItems.Single().Count == 3 && engine.Fight.DungeonRun!.UsedItems.Single().Count == 5,
            "Dungeon item totals span manual fight resets without mutating older snapshots");
        var store = new FightStore(Path.Combine(temp, "dungeon-item-use")); store.Save(engine.Snapshot());
        var loaded = store.Load(Path.Combine(store.Root, engine.Fight.Id + ".json"));
        var group = ObservedRunGroups.Build([before, FightArchiveSummary.Create(loaded)]).Single();
        check(loaded.HasRecordedData && group.UsedItems.Single().Count == 5 && group.Fights == 0,
            "Saved usage-only dungeon summaries remain visible and use the latest cumulative total without double counting fights");
        Apply(new("local-zone", 30, "test", JsonSerializer.SerializeToElement(new { MapId = 1010 })));
        check(engine.SaveCompleted(_ => { }).Any(f => f.DungeonRun?.UsedItems.SingleOrDefault()?.Count == 5), "Leaving an idle dungeon preserves its item-use summary");
        Apply(Inventory(31, false, 13)); check(engine.Fight.DungeonRun == null && engine.Loot.Rows().Length == 0, "Post-exit consumption cannot enter the previous run or create loot");
        var bounded = new DungeonRunTracker(); bounded.Apply(new("dungeon-state", 1, "test", JsonSerializer.SerializeToElement(new DungeonStateReading(600002, 2, null, null))));
        bounded.RecordUses(2, Enumerable.Range(1, 1030).Select(i => new InventoryUse(i, 1)).ToArray());
        check(bounded.Current is { UsedItems.Length: 1024, OmittedUses: 6 }, "Dungeon item categories are bounded and omissions counted");
        using var fixtures = new ReferencePacketFixtures(Path.Combine(temp, "reference-dungeon-item-use.jsonl"));
        void Packet(bool full, long count, byte decrease, byte increase = 0, byte modification = 1) {
            var w = new PartyWireTests.Writer(); if (full) w.Raw(0, 0); w.Var(1); if (!full) w.Raw(modification);
            w.Raw(0); w.Long(100); w.Int(110120003); w.Long(count); w.Raw(1); w.Int(0); w.Bool(false); w.Text(""); w.Long(0); w.Bool(false);
            if (full) w.Zero(6); else w.Raw(increase, decrease, 0);
            var frame = new PartyWireTests.Writer(); frame.Var(w.Bytes.Count + 6); frame.Raw(full ? (byte)0x11 : (byte)0x1b, 0x56); frame.Raw(w.Bytes.ToArray());
            fixtures.Add(new CapturedPayload(13328, 50000, frame.Bytes.ToArray()) { TimestampMs = 100 });
        }
        for (int reason = 0; reason <= 255; reason++) { Packet(true, 10, 0); Packet(false, 7, (byte)reason); }
        Packet(true, 10, 0); Packet(false, 7, 14, 5); Packet(false, 999, 14, modification: 2);
        // Differential fixtures exercise actual packed wire batches, including
        // repeated instances within one packet and mixed gain/use precedence.
        using var batches = new ReferencePacketFixtures(Path.Combine(temp, "reference-inventory-batches.jsonl"));
        var random = new Random(5661);
        long[] counts = Enumerable.Repeat(5_000_000_000L, 8).ToArray();
        for (int step = 0; step < 512; step++) {
            bool full = step % 17 == 0;
            int size = step == 0 ? 8 : random.Next(1, 13);
            var w = new PartyWireTests.Writer(); if (full) w.Raw(0x10, 0x02); w.Var(size);
            for (int row = 0; row < size; row++) {
                int slot = step == 0 ? row : random.Next(8);
                byte modification = full ? (byte)1 : (byte)random.Next(3);
                byte increase = (byte)(row % 3 == 0 ? 5 : random.Next(256));
                byte decrease = (byte)(row % 3 == 1 ? 14 : random.Next(256));
                long count = Math.Max(0, counts[slot] + random.Next(-100, 101));
                if (!full) w.Raw(modification);
                w.Raw(0); w.Long(5_000_000_000L + slot); w.Int(110120003 + slot); w.Long(count);
                w.Raw(1); w.Int(slot); w.Bool(row % 2 == 0); w.Text(""); w.Long(0); w.Bool(row % 3 == 0);
                if (!full) w.Raw(increase, decrease, 0);
                counts[slot] = !full && modification == 2 ? 0 : count;
            }
            if (full) w.Zero(6);
            var frame = new PartyWireTests.Writer(); frame.Var(w.Bytes.Count + 6);
            frame.Raw(full ? (byte)0x11 : (byte)0x1b, 0x56); frame.Raw(w.Bytes.ToArray());
            batches.Add(new CapturedPayload(13328, 50000, frame.Bytes.ToArray()) { TimestampMs = 1000 + step / 2 });
        }
    }
}
