using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class DungeonMapTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        using (var stream = typeof(DungeonMaps).Assembly.GetManifestResourceStream("Wingbeat.DungeonMaps.json")!) {
            var data = JsonSerializer.Deserialize<DungeonMapData>(stream)!;
            var queries = data.Dungeons.Concat(data.Nightmares).Append(0).Append(1010).Distinct()
                .SelectMany(map => data.GlobalBosses.Concat(data.BossesByMap.Values.SelectMany(ids => ids)).Append(int.MaxValue).Distinct()
                    .Select(npc => new { Map = map, Npc = npc, Named = DungeonMaps.NamesBosses(map), Boss = DungeonMaps.IsBoss(npc, map) })).ToArray();
            check(DungeonMaps.LoadError == null && queries.Any(q => q.Named && q.Boss) && queries.Any(q => q.Named && !q.Boss) &&
                queries.Any(q => !q.Named && q.Boss), "Numeric map boss catalogue distinguishes named map membership from global fallback");
            File.WriteAllText(Path.Combine(temp, "map-boss-classification.json"), JsonSerializer.Serialize(queries));
        }
        check(DungeonMaps.Count == 59 && DungeonMaps.Contains(600002) && DungeonMaps.Contains(200003) && !DungeonMaps.Contains(600999) && !DungeonMaps.Contains(1010),
            "Dungeon map classification includes explicit dungeon/nightmare IDs without guessing numeric ranges");
        NetworkEvent Map(long at, int id) => new("local-zone", at, "test", JsonSerializer.SerializeToElement(new { MapId = id }));
        var tracker = new DungeonRunTracker(); tracker.Apply(Map(100, 600002)); var initial = tracker.Current!;
        tracker.RecordUses(110, [new(1, 2)]); tracker.Apply(Map(120, 600002));
        check(tracker.Current is { State: 0, StartedAt: null, ClearedAt: null, SawPrepare: false } && tracker.Current.Id == initial.Id && tracker.Current.UsedItems.Single().Count == 2,
            "Recognized map starts provisional accounting and duplicate map notices preserve its totals without inventing completion");
        tracker.Apply(Map(90, 1010)); tracker.Apply(new("local-zone", 130, "test", JsonSerializer.SerializeToElement(new { MapId = "invalid" })));
        check(tracker.Current!.Id == initial.Id, "Stale or malformed map notices cannot clear a known dungeon run");
        tracker.Apply(new("dungeon-state", 140, "test", JsonSerializer.SerializeToElement(new DungeonStateReading(600002, 1, null, null))));
        check(tracker.Current!.Id == initial.Id && tracker.Current.SawPrepare && tracker.Current.UsedItems.Single().Count == 2,
            "Later dungeon preparation enriches the map-started run and preserves early item accounting");
        tracker.Apply(Map(150, 1010)); tracker.Apply(Map(160, 600002));
        check(tracker.Current!.Id != initial.Id && tracker.Current.UsedItems.Length == 0, "Leaving and re-entering the same dungeon starts a separate accounting run");
        var cache = new CombatEngine(); cache.Apply(new("local-player", Actor: new(1, "Synthetic", true, false, 0, "Cleric", 2301)));
        void Send(NetworkEvent e) => cache.Apply(new("network", Network: e));
        void Inventory(long at, long count, bool full) => Send(new(full ? "inventory" : "inventory-update", at, "test",
            JsonSerializer.SerializeToElement(new InventoryBatch(full, [new(10, 110120003, count, 1, 1, 0, 14)]))));
        Inventory(1, 10, true); Send(Map(2, 600002)); Inventory(3, 8, false); Send(Map(4, 600002)); Inventory(5, 7, false);
        var beforeExit = cache.Snapshot(); Send(Map(6, 1010));
        check(beforeExit.DungeonRun!.UsedItems.Single().Count == 3 && cache.SaveCompleted(_ => { }).Any(f => f.DungeonRun?.UsedItems.SingleOrDefault()?.Count == 3),
            "Production map entry captures consumption before dungeon-state and preserves its summary on exit");
        var groups = ObservedRunGroups.Build([FightArchiveSummary.Create(beforeExit)]);
        check(groups.Single().Status.Contains("Partial") && groups.Single().ClearTime == "Not captured", "Map-only entry remains explicitly partial and does not invent a clear time");
        // Independent d1 oracle receives normalized events; expected quantities
        // above came through production inventory and run trackers.
        var events = new object[] {
            new { type = "inventory", at = 1, stacks = new[] { new { instance = 10, item = 110120003, count = 10 } } },
            new { type = "map", at = 2, map = 600002 },
            new { type = "inventory-update", at = 3, stacks = new[] { new { instance = 10, item = 110120003, count = 8, reason = "use" } } },
            new { type = "map", at = 4, map = 600002 },
            new { type = "inventory-update", at = 5, stacks = new[] { new { instance = 10, item = 110120003, count = 7, reason = "use" } } },
            new { type = "map", at = 6, map = 1010 }, new { type = "map", at = 7, map = 600999 },
            new { type = "map", at = 8, map = 200003 }, new { type = "map", at = 9, map = 1010 }
        };
        File.WriteAllText(Path.Combine(temp, "reference-dungeon-map-lifecycle.json"), JsonSerializer.Serialize(new {
            Events = events, Runs = new[] {
                new { Map = 600002, Entered = 2, Left = 6, Used = beforeExit.DungeonRun.UsedItems, Received = beforeExit.DungeonRun.ReceivedItems },
                new { Map = 200003, Entered = 8, Left = 9, Used = Array.Empty<DungeonItemTotal>(), Received = Array.Empty<DungeonItemTotal>() }
            }
        }));
    }
}
