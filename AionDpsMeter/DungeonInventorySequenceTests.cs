using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class DungeonInventorySequenceTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var engine = new CombatEngine();
        engine.Apply(new("local-player", Actor: new(1, "Sequence owner", true, false, 0, "Cleric", 2301)));
        var events = new List<object>(); var runs = new List<object>();
        var random = new Random(79213); long time = 1700000000000;
        long[] counts = [100, 80, 60, 40]; decimal expectedLoot = 0;
        void Send(string kind, object fields) => engine.Apply(new("network", Network: new(kind, time, "test", JsonSerializer.SerializeToElement(fields))));
        void Map(int map) { time++; events.Add(new { type = "map", at = time, map }); Send("local-zone", new { MapId = map }); }
        void State(int map, byte phase) {
            time++; events.Add(new { type = "dungeon-state", at = time, map, state = phase == 1 ? "prepare" : "end" });
            Send("dungeon-state", new DungeonStateReading(map, phase, null, null));
        }
        void Inventory(int index, bool snapshot, int change = 0, bool loot = false, bool use = false, bool remove = false, bool added = false) {
            time++; long before = counts[index]; counts[index] = remove ? 0 : Math.Max(0, before + change);
            long count = counts[index]; int item = 110120003 + index; long instance = 100 + index;
            events.Add(snapshot ? new { type = "inventory", at = time, stacks = new[] { new { instance, item, count } } } :
                (object)new { type = "inventory-update", at = time, stacks = new[] { new { instance, item, count, added, reason = loot ? "loot" : use ? "use" : null, source = loot ? "drop" : null } } });
            Send(snapshot ? "inventory" : "inventory-update", new InventoryBatch(snapshot,
                [new(instance, item, remove ? 999 : count, 1, remove ? (byte)2 : added ? (byte)0 : (byte)1, loot ? (byte)5 : (byte)0, use ? (byte)14 : (byte)0)]));
            if (!snapshot && loot && count > before) expectedLoot += count - before;
        }
        void EndRun(long left) {
            var run = engine.Snapshot().DungeonRun ?? throw new InvalidOperationException("Sequence run disappeared.");
            runs.Add(new { Map = run.MapId, Entered = run.EnteredAt, Left = left, Used = run.UsedItems, Received = run.ReceivedItems });
        }
        Map(1010);
        for (int i = 0; i < counts.Length; i++) Inventory(i, true);
        for (int run = 0; run < 40; run++) {
            int map = run % 2 == 0 ? 600002 : 600012;
            Inventory(run % 4, false, 2, loot: true); // Outside-run gains retain the same stack baseline.
            Map(map); State(map, 1);
            for (int step = 0; step < 30; step++) {
                int index = random.Next(4);
                switch (step % 7) {
                    case 0: Inventory(index, true); break; // Partial depot refresh must retain other stacks.
                    case 1: Inventory(index, false, random.Next(1, 8), loot: true); break;
                    case 2: Inventory(index, false, -random.Next(1, 5), use: true); break;
                    case 3: Inventory(index, false, 0, loot: true); break; // Duplicate count.
                    case 4: Inventory(index, false, 3); break; // Purchase updates baseline without loot.
                    case 5: Inventory(index, false, remove: true, use: true); break;
                    default: Inventory(index, false, 4, loot: true, added: true); break;
                }
                if (step == 10) engine.NewFight("Manual DPS reset during run");
                if (step == 20) {
                    State(map, 4); EndRun(time + 1); State(map, 1); // Explicit same-map restart.
                }
            }
            EndRun(time + 1); Map(1010);
            engine.SaveCompleted(_ => { });
        }
        check(engine.Loot.Rows().Sum(r => r.Quantity) == expectedLoot,
            "Continuous dungeon inventory sequence counts loot exactly once across partial snapshots, removals, re-additions and DPS resets");
        check(runs.Count == 80 && engine.Fight.Network.All(e => e.Kind is not ("inventory" or "inventory-update" or "loot-pickup")),
            "Eighty map/restart run summaries retain no raw inventory or pickup history");
        File.WriteAllText(Path.Combine(temp, "reference-dungeon-inventory-sequence.json"), JsonSerializer.Serialize(new { Events = events, Runs = runs }));
    }
}
