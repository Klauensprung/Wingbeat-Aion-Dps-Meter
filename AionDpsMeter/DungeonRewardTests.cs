using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class DungeonRewardTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: new(1, "Synthetic", true, false, 0, "Cleric", 2301)));
        var events = new List<object>();
        void Map(long at, int map) {
            events.Add(new { type = "map", at, map });
            engine.Apply(new("network", Network: new("local-zone", at, "test", JsonSerializer.SerializeToElement(new { MapId = map }))));
        }
        void Inventory(long at, long count, bool full = false, bool used = false) {
            events.Add(full ? new { type = "inventory", at, stacks = new[] { new { instance = 10, item = 110120003, count } } } :
                (object)new { type = "inventory-update", at, stacks = new[] { new { instance = 10, item = 110120003, count, reason = used ? "use" : "loot", source = used ? null : "drop" } } });
            engine.Apply(new("network", Network: new(full ? "inventory" : "inventory-update", at, "test", JsonSerializer.SerializeToElement(
                new InventoryBatch(full, [new(10, 110120003, count, 1, 1, used ? (byte)0 : (byte)5, used ? (byte)14 : (byte)0)])))));
        }
        Map(1, 1010); Inventory(2, 50, true); Inventory(3, 52);
        engine.RecordDamage(new() { Timestamp = 3, ActorId = 1, TargetId = 99, SkillCode = 17010000, Damage = 100 }, false);
        Map(4, 600002); Inventory(5, 55); Inventory(6, 54, used: true);
        check(engine.Fight.DungeonRun == null && engine.Fight.Damage.Single().Damage == 100 && engine.HasCompletedFights,
            "New dungeon rewards do not relabel combat from a previous map and expose compact progress for autosave");
        bool failed = false; try { engine.SaveCompleted(_ => throw new IOException("Synthetic write failure")); } catch (IOException) { failed = true; }
        var first = engine.SaveCompleted(_ => { }).Single();
        check(failed && first.DungeonRun is { MapId: 600002, ReceivedItems: [{ Count: 3 }], UsedItems: [{ Count: 1 }] } && !first.HasEvents,
            "Detached dungeon progress survives a failed write and retains only compact received/used quantities");
        Map(7, 600012); Inventory(8, 58); Map(9, 1010); Inventory(10, 59);
        var second = engine.SaveCompleted(_ => { }).Single();
        check(second.DungeonRun is { MapId: 600012, ReceivedItems: [{ Count: 4 }], UsedItems.Length: 0 } && first.Id != second.Id,
            "Back-to-back dungeon rewards retain separate run identities through exit without new combat");
        check(engine.Loot.Rows().Sum(r => r.Quantity) == 10 && SavedHistory.LootPickups(engine.Fight) == 4 && engine.Fight.Network.All(n => n.Kind != "loot-pickup"),
            "Run summaries reuse authoritative gains without adding duplicate loot-history pickups");
        var store = new FightStore(Path.Combine(temp, "dungeon-rewards")); store.Save(first); store.Save(second);
        var restored = store.Load(Path.Combine(store.Root, first.Id + ".json"));
        var groups = ObservedRunGroups.Build([FightArchiveSummary.Create(restored), FightArchiveSummary.Create(second)]);
        check(groups.Length == 2 && groups.Sum(g => g.ReceivedItems.Sum(i => i.Count)) == 7 && restored.Network.Count == 0,
            "Reward-only runs survive disk and compact history without raw packet retention");
        var tracker = new DungeonRunTracker(); tracker.Apply(new("local-zone", 1, "test", JsonSerializer.SerializeToElement(new { MapId = 600002 })));
        var gains = Enumerable.Range(1, 1030).Select(i => new LootPickup(2, 0, i, 1, 0, 2301)).ToArray(); tracker.RecordRewards(2, gains);
        check(tracker.Current is { ReceivedItems.Length: 1024, OmittedReceived: 6 }, "Dungeon reward item types have bounded retention and explicit omitted quantities");
        var older = tracker.Current; tracker.RecordRewards(1, gains);
        check(ReferenceEquals(older, tracker.Current), "Stale reward batches cannot alter newer dungeon totals");
        File.WriteAllText(Path.Combine(temp, "reference-dungeon-rewards.json"), JsonSerializer.Serialize(new {
            Events = events, Runs = new[] {
                new { Map = first.DungeonRun!.MapId, Entered = first.DungeonRun.FirstObserved, Left = 7, Used = first.DungeonRun.UsedItems, Received = first.DungeonRun.ReceivedItems },
                new { Map = second.DungeonRun!.MapId, Entered = second.DungeonRun.FirstObserved, Left = 9, Used = second.DungeonRun.UsedItems, Received = second.DungeonRun.ReceivedItems }
            }
        }));

        engine = new CombatEngine(); engine.Apply(new("local-player", Actor: new(1, "Synthetic", true, false, 0, "Cleric", 2301)));
        events.Clear();
        const long clock = 1700000000000;
        void State(long at, byte phase, long? began = null) {
            events.Add(new { type = "dungeon-state", at, map = 600002,
                state = new[] { "none", "prepare", "process", "complete", "end" }[phase], startedAt = began, endsAt = began + 600000 });
            engine.Apply(new("network", Network: new("dungeon-state", at, "test", JsonSerializer.SerializeToElement(new DungeonStateReading(600002, phase, began, began + 600000)))));
        }
        Map(clock - 10, 600002); Inventory(clock - 9, 49, true); Inventory(clock - 8, 50);
        State(clock, 1); Inventory(clock + 1, 50, true);
        State(clock + 2, 2, clock); Inventory(clock + 3, 53);
        var original = engine.Fight.DungeonRun!;
        State(clock + 4, 2, clock + 1000); State(clock + 5, 3); State(clock + 6, 1);
        check(engine.Fight.DungeonRun is { ReceivedItems: [{ Count: 4 }] } same && same.Id == original.Id && same.StartedAt == clock,
            "Refreshed process timers and prepare without an explicit end preserve run identity, first start and rewards");
        check(engine.Fight.DungeonRun!.EnteredAt == clock && engine.Fight.DungeonRun.FirstObserved == clock - 10,
            "First prepare establishes entry time without erasing earlier observations or moving on repeated prepare");
        State(clock + 7, 4); State(clock + 8, 2, clock + 2000); State(clock + 9, 1);
        var previousRun = engine.SaveCompleted(_ => { }).Single();
        check(previousRun.DungeonRun!.Id == original.Id && engine.Fight.DungeonRun!.Id != original.Id && engine.Fight.DungeonRun.ReceivedItems.Length == 0,
            "An explicit end remains a restart signal across intervening process updates and archives the old reward summary");
        Inventory(clock + 10, 57); var restarted = engine.Fight.DungeonRun!; Map(clock + 11, 1010);
        var nextRun = engine.SaveCompleted(_ => { }).Single();
        check(nextRun.DungeonRun!.Id == restarted.Id && nextRun.DungeonRun.ReceivedItems is [{ Count: 4 }],
            "Same-map restarted run receives only subsequent inventory gains and survives exit");
        File.WriteAllText(Path.Combine(temp, "reference-dungeon-reentry.json"), JsonSerializer.Serialize(new {
            Events = events, Runs = new[] {
                new { Map = original.MapId, Entered = original.EnteredAt, Left = clock + 9, Used = previousRun.DungeonRun.UsedItems, Received = previousRun.DungeonRun.ReceivedItems },
                new { Map = restarted.MapId, Entered = restarted.EnteredAt, Left = clock + 11, Used = nextRun.DungeonRun.UsedItems, Received = nextRun.DungeonRun.ReceivedItems }
            }
        }));
        store.Save(previousRun);
        var savedEntry = store.Load(Path.Combine(store.Root, previousRun.Id + ".json")).DungeonRun!;
        check(savedEntry.EnteredAt == clock && savedEntry.FirstObserved == clock - 10 && savedEntry.ReceivedItems.Single().Count == 4,
            "Prepared entry and earlier reward observations survive storage together");
        var legacy = savedEntry with { PreparedAt = null };
        DungeonRunTracker.Validate(legacy);
        check(legacy.EnteredAt == legacy.FirstObserved, "Older run summaries without prepare timestamps retain their observed entry fallback");
        bool invalidEntry = false;
        try { DungeonRunTracker.Validate(savedEntry with { PreparedAt = savedEntry.LastObserved + 1 }); }
        catch (InvalidDataException) { invalidEntry = true; }
        check(invalidEntry, "Imported run summaries reject prepare timestamps beyond their observations");

        engine = new CombatEngine(); engine.Apply(new("local-player", Actor: new(1, "First owner", true, false, 0, "Cleric", 2301) { PersistentId = 650000000000000001 }));
        Map(1, 1010); engine.RecordDamage(new() { Timestamp = 2, ActorId = 1, TargetId = 99, Damage = 1 }, false);
        Map(3, 600002); Inventory(4, 50, true); Inventory(5, 52);
        engine.Apply(new("local-player", Actor: new(1, "Second owner", true, false, 0, "Cleric", 2301) { PersistentId = 650000000000000002 }));
        var oldOwnerRun = engine.SaveCompleted(_ => { }).Single(f => f.DungeonRun?.ReceivedItems.Length > 0);
        check(oldOwnerRun.Actors[oldOwnerRun.LocalPlayer].PersistentId == 650000000000000001 && oldOwnerRun.DungeonRun!.ReceivedItems.Single().Count == 2 && engine.Fight.DungeonRun == null,
            "Character switches preserve unsaved detached rewards under the original full identity and clear active run attribution");
    }
}
