using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class DungeonSplitTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: new(1, "Synthetic", true, false, 0, "Cleric", 2301)));
        var events = new List<object>();
        void Send(string kind, long at, object data) => engine.Apply(new("network", Network: new(kind, at, "test", JsonSerializer.SerializeToElement(data))));
        void Map(long at, int map) { events.Add(new { type = "map", at, map }); Send("local-zone", at, new { MapId = map }); }
        void State(long at, byte phase) {
            events.Add(new { type = "dungeon-state", at, map = 600002, state = phase == 1 ? "prepare" : "end" });
            Send("dungeon-state", at, new DungeonStateReading(600002, phase, null, null));
        }
        void Progress(long at, int step, bool done = false, bool emit = true) {
            Send("map-event-progress", at, new MapEventReading(10, null, done ? (byte)3 : (byte)1, 2, step));
            if (emit) events.Add(new { type = "map-event", at, name = "Open the gate", stage = 2, step, done });
        }
        Map(1, 600002); Send("map-event-join", 2, new MapEventReading(10, "Open the gate", 1, 2, 0));
        Progress(3, 1); check(engine.Fight.DungeonRun!.Splits.Length == 1, "Dungeon checkpoints record changed map progress rather than the initial join snapshot");
        State(4, 1); check(engine.Fight.DungeonRun!.Splits.Length == 0, "First prepare discards pre-entry checkpoints as the reference does");
        Progress(5, 2); var earlier = engine.Snapshot(); Progress(6, 2, emit: false); Progress(7, 3, true); State(8, 1);
        check(engine.Fight.DungeonRun!.Splits is [{ Step: 2 }, { Step: 3, Done: true }] && earlier.DungeonRun!.Splits.Length == 1,
            "Duplicate progress and repeated prepare preserve two immutable checkpoint summaries");
        State(9, 4); State(10, 1); var first = engine.SaveCompleted(_ => { }).Single();
        Progress(11, 4); Map(12, 1010); var second = engine.SaveCompleted(_ => { }).Single();
        check(first.DungeonRun!.Splits.Length == 2 && second.DungeonRun!.Splits is [{ Timestamp: 11, Step: 4 }],
            "Same-map restart and exit preserve checkpoints under their own run identities without combat");
        var store = new FightStore(Path.Combine(temp, "dungeon-splits")); store.Save(first);
        var restored = store.Load(Path.Combine(store.Root, first.Id + ".json"));
        var groups = ObservedRunGroups.Build([FightArchiveSummary.Create(earlier), FightArchiveSummary.Create(restored)]);
        check(groups.Single().Splits.Length == 2 && restored.Network.All(n => !n.Kind.StartsWith("map-event")) && restored.DungeonRun!.Splits.SequenceEqual(first.DungeonRun.Splits),
            "Saved and compact run checkpoints retain the latest cumulative summary without raw map-event packets or double counting");
        var tracker = new DungeonRunTracker(); tracker.Apply(new("local-zone", 0, "test", JsonSerializer.SerializeToElement(new { MapId = 600002 })));
        for (int i = 1; i <= 260; i++) tracker.RecordSplit(new(10, "Gate", 1, i, false, i));
        var bounded = tracker.Current!; tracker.RecordSplit(new(10, "Stale", 1, 999, false, 1));
        check(bounded.Splits.Length == 256 && bounded.OmittedSplits == 4 && ReferenceEquals(bounded, tracker.Current),
            "Checkpoint retention is bounded with explicit omitted count and rejects stale updates");
        bool invalid = false; try { DungeonRunTracker.Validate(bounded with { Splits = [new(261, "Gate", 1, 1, false)] }); } catch (InvalidDataException) { invalid = true; }
        check(invalid, "Imported checkpoint timestamps must lie within the run observations");
        File.WriteAllText(Path.Combine(temp, "reference-dungeon-splits.json"), JsonSerializer.Serialize(new {
            IncludeSplits = true, Events = events, Runs = new[] { first, second }.Select(f => new {
                Map = f.DungeonRun!.MapId, Entered = f.DungeonRun.EnteredAt, Left = f == first ? 10 : 12,
                Used = f.DungeonRun.UsedItems, Received = f.DungeonRun.ReceivedItems, Splits = f.DungeonRun.Splits
            })
        }));
    }
}
