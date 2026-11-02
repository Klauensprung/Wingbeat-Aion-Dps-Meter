using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class InventoryLootTests
{
    internal static void Run(Action<bool, string> check)
    {
        LiveRetention(check);
        var local = new Actor(1, "Player 1", true, true, 0, "Cleric", 1) { CharacterId = 30 };
        NetworkEvent Event(long time, bool snapshot, params InventoryStack[] rows) => new(snapshot ? "inventory" : "inventory-update",
            time, "synthetic", JsonSerializer.SerializeToElement(new InventoryBatch(snapshot, rows)));
        InventoryStack Stack(long count, byte modification = 1, byte reason = 5, long instance = 5000000000) =>
            new(instance, 110120003, count, 1, modification, reason);
        var tracker = new InventoryLootTracker();
        var login = new CombatEngine();
        login.Apply(new("network", Network: new("inventory", 1000, "test", JsonSerializer.SerializeToElement(new InventoryBatch(true, [Stack(40)], 528)))));
        login.Apply(new("local-player", Actor: local));
        login.Apply(new("network", Network: Event(1100, false, Stack(43))));
        check(login.Loot.Rows().Single().Quantity == 3,
            "Observed nonzero-result inventory rows survive first identity binding and establish the real loot baseline");
        login.Apply(new("local-player", Actor: local));
        login.Apply(new("network", Network: Event(1200, false, Stack(44))));
        login.Apply(new("network", Network: Event(1201, false, Stack(45))));
        check(login.Loot.Rows().Sum(r => r.Quantity) == 4,
            "An actual reconnect discards old-session counts, then measures gains after the fresh baseline");
        login.Apply(new("local-player", Actor: local with { Name = "Different player" }));
        login.Apply(new("network", Network: Event(1300, false, Stack(90))));
        login.Apply(new("network", Network: Event(1400, false, Stack(92))));
        check(login.Loot.Rows().Sum(r => r.Quantity) == 6 && login.Loot.Rows().Any(r => r.Character == "Different player" && r.Quantity == 2),
            "Reused entity IDs with a different character name clear the previous character's inventory baseline");
        check(tracker.Apply(Event(1, true, Stack(10)), local).Length == 0 && tracker.Apply(Event(2, false, Stack(13)), local) is [{ Quantity: 3 }],
            "Inventory snapshot establishes a baseline; only a subsequent positive delta is loot");
        check(tracker.Apply(Event(2, false, Stack(13)), local).Length == 0 && tracker.Apply(Event(1, false, Stack(99)), local).Length == 0,
            "Repeated and stale inventory updates do not add loot");
        tracker.Apply(Event(3, false, Stack(20, reason: 19)), local);
        check(tracker.Apply(Event(4, false, Stack(21)), local) is [{ Quantity: 1 }],
            "Shop and other non-loot increases still update the baseline");
        tracker.Apply(Event(5, false, Stack(21, 2)), local);
        check(tracker.Apply(Event(6, false, Stack(4, 0, 41)), local) is [{ Quantity: 4, Source: "gather" }],
            "Removal zeroes a stack and gathering is classified from the increase reason");
        tracker.Clear();
        check(tracker.Apply(Event(7, false, Stack(50)), local).Length == 0 && tracker.Apply(Event(8, false, Stack(52)), local) is [{ Quantity: 2 }],
            "Capture started mid-session establishes an unknown stack without counting its existing items");
        check(tracker.Apply(Event(9, false, Stack(6000000000, 0, 24, 6000000000)), local) is [{ Quantity: 6000000000, Source: "quest" }],
            "Explicitly added reward stacks support quantities and instance IDs beyond UInt32");
        check(tracker.Apply(Event(10, false, Stack(55)), local with { CharacterId = 31 }).Length == 0,
            "Character switches discard the preceding inventory baseline");
        tracker.Clear(); tracker.Apply(Event(11, true, Stack(10)), local with { CharacterId = 0 });
        check(tracker.Apply(Event(12, false, Stack(11)), local) is [{ Quantity: 1 }],
            "Later identity enrichment retains a captured baseline without retroactive loot");
        var wideOwner = local with { PersistentId = 6000000001 };
        var wideOther = wideOwner with { PersistentId = 10294967297 }; // Same low 32 bits.
        tracker.Clear(); tracker.Apply(Event(100, true, Stack(10)), wideOwner);
        tracker.Apply(Event(101, false, Stack(12)), wideOwner);
        check(tracker.Apply(Event(102, false, Stack(90)), wideOther).Length == 0 &&
            tracker.Apply(Event(103, false, Stack(92)), wideOther) is [{ Quantity: 2 }],
            "Full-width character identity changes isolate inventory baselines even with identical entity, name, server and legacy ID");
        tracker.Clear(); tracker.Apply(Event(104, true, Stack(10)), wideOwner);
        check(tracker.Apply(Event(105, false, Stack(11)), wideOwner with { PersistentId = 0, CharacterId = 0 }) is [{ Quantity: 1 }] &&
            tracker.Apply(Event(106, false, Stack(90)), wideOther).Length == 0,
            "Sparse identity updates preserve a known full-width owner so later changes still reset inventory");
        tracker.Clear(); tracker.Apply(Event(107, true, Stack(10)), local);
        check(tracker.Apply(Event(108, false, Stack(12)), wideOwner) is [{ Quantity: 2 }],
            "Learning a previously unknown full-width character ID preserves the current inventory baseline");
        var identityEngine = new CombatEngine(); identityEngine.Apply(new("local-player", Actor: wideOwner));
        void IdentityInventory(NetworkEvent n) => identityEngine.Apply(new("network", Network: n));
        IdentityInventory(Event(110, true, Stack(10))); IdentityInventory(Event(111, false, Stack(12)));
        identityEngine.Apply(new("actor", Actor: wideOther));
        IdentityInventory(Event(112, false, Stack(90))); IdentityInventory(Event(113, false, Stack(92)));
        var ownershipRows = identityEngine.Loot.Rows();
        check(ownershipRows.Length == 2 && ownershipRows.All(r => r.Quantity == 2) && ownershipRows.Select(r => r.Session).Distinct().Count() == 2 &&
            LootLedger.FromFight(identityEngine.Snapshot().ForStorage()).Rows().Sum(r => r.Quantity) == 4,
            "Production identity replacement separates same-name loot sessions and persists only real gains");
        var unlinked = new CombatEngine(); unlinked.Apply(new("local-player", Actor: local with { CharacterId = 0 }));
        unlinked.Apply(new("network", Network: Event(13, false, Stack(100))));
        unlinked.Apply(new("network", Network: Event(14, false, Stack(107))));
        check(unlinked.Loot.Rows().Single().Quantity == 7 &&
            LootLedger.FromFight(unlinked.Snapshot().ForStorage()).Rows().Single() is { Quantity: 7, Character: "Player 1" },
            "Local inventory gains persist without a character-link packet using an isolated inventory session");
        unlinked.Apply(new("local-player", Actor: local with { CharacterId = 0, Name = "Player 2" }));
        unlinked.Apply(new("network", Network: Event(15, false, Stack(200))));
        unlinked.Apply(new("network", Network: Event(16, false, Stack(202))));
        check(unlinked.Loot.Rows().Length == 2 && unlinked.Loot.Rows().Sum(r => r.Quantity) == 9,
            "Unlinked characters with reused entity IDs cannot merge inventory reward sessions");

        var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: local));
        void Send(NetworkEvent message) => engine.Apply(new("network", Network: message));
        Send(Event(20, true, Stack(10)));
        Send(new("local-zone", 21, "synthetic", JsonSerializer.SerializeToElement(new { MapId = 1010 })));
        engine.NewFight("Synthetic reset"); Send(Event(22, false, Stack(12)));
        Send(new("local-zone", 23, "synthetic", JsonSerializer.SerializeToElement(new { MapId = 2010 })));
        Send(Event(24, false, Stack(15)));
        var fight = engine.Snapshot();
        check(engine.Loot.Rows().Single(r => r.MapId == 1010).Quantity == 2 && engine.Loot.Rows().Single(r => r.MapId == 2010).Quantity == 3 &&
            fight.Network.All(n => n.Kind is not ("inventory" or "inventory-update")),
            "Inventory counts survive DPS resets and map changes while raw inventory is excluded from fight history");
        check(LootLedger.FromFight(fight).Rows().Sum(r => r.Quantity) == 5 && LootLedger.FromFight(fight.ForStorage()).Rows().Sum(r => r.Quantity) == 5,
            "Inventory loot survives compact fight storage with unchanged totals");
        Send(Event(25, false, Stack(16), Stack(17), Stack(2, 0, 31, 7000000000)));
        check(LootLedger.FromFight(engine.Snapshot()).Rows().Sum(r => r.Quantity) == 9,
            "Multiple gains at one timestamp, including the same stack, are not deduplicated away");
        engine.Apply(new("local-player", Actor: local)); Send(Event(26, false, Stack(100)));
        check(LootLedger.FromFight(engine.Snapshot()).Rows().Length == 0, "Explicit reconnect resets the inventory baseline");
        var ledger = new LootLedger(); ledger.Add(new(1, 0, 1, 6000000000, 30, 1), "Player 1");
        LootLedger.Validate(ledger.Summary());
        check(ledger.Rows().Single().Quantity == 6000000000, "Saved loot accepts valid 64-bit reward amounts");
        tracker.Clear(); tracker.Apply(Event(27, true, Stack(40)), local);
        tracker.Apply(Event(28, true, Stack(7, instance: 9000000000)), local);
        check(tracker.Count == 2 && tracker.Apply(Event(29, false, Stack(41)), local) is [{ Quantity: 1 }],
            "Repeated inventory lists preserve counts for omitted instances instead of awarding the entire stack");
        tracker.Apply(Event(30, true), local);
        check(tracker.Apply(Event(31, false, Stack(41)), local).Length == 0,
            "An empty depot list does not turn an unchanged known stack into loot");
        tracker.Apply(Event(32, true, Stack(50)), local);
        check(tracker.Apply(Event(33, false, Stack(52)), local) is [{ Quantity: 2 }],
            "A repeated list refreshes counts it actually supplies without generating gains");
        tracker.Bind(local, reconnect: true);
        check(tracker.Apply(Event(34, false, Stack(60)), local).Length == 0 && tracker.Count == 1,
            "Preserving depot-list counts does not preserve another login's baseline");
        tracker.Clear();
        for (int batch = 0; batch < 3; batch++) tracker.Apply(Event(30 + batch, false,
            Enumerable.Range(1, 8192).Select(i => Stack(1, instance: batch * 8192L + i)).ToArray()), local);
        check(tracker.Count == 0 && tracker.Apply(Event(34, false, Stack(1, 0)), local).Length == 0,
            "An over-capacity inventory fails closed instead of turning evicted stacks into loot");
        tracker.Apply(Event(35, true, Stack(10)), local);
        check(tracker.Apply(Event(36, false, Stack(11)), local).Length == 1, "A new complete snapshot recovers inventory tracking after capacity overflow");
        tracker.Clear();
        for (int batch = 0; batch < 3; batch++) tracker.Apply(Event(40 + batch, true,
            Enumerable.Range(1, 8192).Select(i => Stack(1, instance: batch * 8192L + i)).ToArray()), local);
        check(tracker.Count == 0 && tracker.Apply(Event(43, false, Stack(2, 0)), local).Length == 0,
            "Accumulated depot lists still fail closed at the inventory capacity limit");
        tracker.Apply(Event(44, true, Stack(10)), local);
        check(tracker.Count == 1 && tracker.Apply(Event(45, false, Stack(12)), local) is [{ Quantity: 2 }],
            "A valid depot list restores a fresh baseline after cumulative list overflow");
    }
    static void LiveRetention(Action<bool, string> check)
    {
        CombatEngine Engine() {
            var engine = new CombatEngine();
            engine.Apply(new("local-player", Actor: new(1, "Loot owner", true, true, 0, "Cleric", 1)));
            engine.Apply(new("actor", Actor: new(10, "Target", false, false, 0, "Unknown", 0) { NpcId = 100 }));
            return engine;
        }
        void Inventory(CombatEngine e, long at, long count, bool full = false) => e.Apply(new("network", Network: new(full ? "inventory" : "inventory-update", at, "test",
            JsonSerializer.SerializeToElement(new InventoryBatch(full, [new(5000000000, 110120003, count, 1, 1, 5)])))));
        void Hit(CombatEngine e, long at, bool heal = false) => e.Apply(new(heal ? "heal" : "damage", Damage: new() { ActorId = 1, TargetId = heal ? 1 : 10, Timestamp = at, Damage = 100 }));
        var many = Engine(); Inventory(many, 0, 0, true);
        for (int n = 1; n <= 20000; n++) Inventory(many, n, n);
        var snapshot = many.Snapshot();
        check(many.Fight.Network.Count == 0 && LootLedger.FromFight(snapshot).Rows() is [{ Quantity: 20000, Pickups: 20000 }] && SavedHistory.Includes(snapshot),
            "Twenty thousand live pickups retain one bounded summary row and no raw inventory or pickup log");
        Inventory(many, 20001, 20001);
        check(LootLedger.FromFight(snapshot).Rows().Single().Quantity == 20000 && LootLedger.FromFight(many.Fight).Rows().Single().Quantity == 20001,
            "Detached loot snapshots do not mutate when more items arrive");
        var stored = JsonSerializer.Deserialize<Fight>(JsonSerializer.Serialize(snapshot.ForStorage()))!;
        check(stored.Network.Count == 0 && LootLedger.FromFight(stored).Rows().SequenceEqual(LootLedger.FromFight(snapshot).Rows()) &&
            SavedHistory.LootPickups(FightArchiveSummary.Create(stored)) == 20000,
            "Compact save/load and archive pickup counts preserve live loot summaries without packet records");
        many.NewFight("Manual"); Inventory(many, 20002, 20002);
        check(LootLedger.FromFight(many.Fight).Rows() is [{ Quantity: 1, Pickups: 1 }] && many.Loot.Rows().Single().Quantity == 20002,
            "Manual fight reset clears fight loot while session history and inventory baseline continue");
        foreach (bool healingPause in new[] { false, true }) {
            var engine = Engine(); Hit(engine, 1000); string id = engine.Fight.Id;
            Inventory(engine, 1100, 10, true); Inventory(engine, 1200, 12);
            var beforePause = engine.Snapshot(); engine.AdvanceTime(16000);
            Inventory(engine, 17000, 15);
            if (healingPause) { Hit(engine, 18000, true); engine.AdvanceTime(33000); }
            Inventory(engine, 34000, 19); Hit(engine, 35000);
            var rows = LootLedger.FromFight(engine.Fight).Rows();
            check(engine.Fight.Id == id && rows is [{ Quantity: 9, Pickups: 3 }] && engine.Fight.Healing.Count == (healingPause ? 1 : 0) &&
                LootLedger.FromFight(beforePause).Rows() is [{ Quantity: 2 }] && engine.Fight.Network.Count == 0,
                "Recovered encounter retains each pickup once without logs" + (healingPause ? " across a healing-only pause" : ""));
            engine.EndFight("Next"); var writes = new Dictionary<string, Fight>();
            engine.SaveCompleted(f => writes[f.Id] = f.ForStorage());
            check(writes.TryGetValue(id, out var final) && SavedHistory.LootPickups(final) == 3 && LootLedger.FromFight(final).Rows().Single().Quantity == 9,
                "Completed recovery snapshots preserve the final loot pickup count");
        }
    }

}
