using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class DungeonScopeTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        const long start = 1700000000000;
        var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: new(1, "Synthetic", true, false, 0, "Cleric", 2301)));
        var events = new List<object>();
        void Send(string kind, long at, object value) => engine.Apply(new("network", Network: new(kind, at, "test", JsonSerializer.SerializeToElement(value))));
        void Map(int tick, int map) { events.Add(new { type = "map", at = start + tick, map }); Send("local-zone", start + tick, new { MapId = map }); }
        void State(int tick, int map, long began) { events.Add(new { type = "dungeon-state", at = start + tick, map, state = "process", startedAt = began, endsAt = began + 60000 }); Send("dungeon-state", start + tick, new DungeonStateReading(map, 2, began, began + 60000)); }
        void Clear(int tick, int map) { events.Add(new { type = "dungeon-cleared", at = start + tick, map, clearLevel = 2 }); Send("dungeon-result", start + tick, new DungeonResultReading(map, true, null, 2, [])); }
        Map(1, 600002); State(2, 600002, start); var original = engine.Fight.DungeonRun!;
        Clear(3, 600012); State(4, 600012, start + 1);
        check(engine.Fight.DungeonRun!.Id == original.Id && engine.Fight.DungeonRun.ClearedAt == null && engine.Fight.DungeonRun.StartedAt == start,
            "Mismatched dungeon result and process state cannot replace a run on the confirmed current map");
        Clear(5, 600002); var first = engine.Fight.DungeonRun!; Map(6, 1010); Clear(7, 600002);
        check(engine.Fight.DungeonRun == null && engine.SaveCompleted(_ => { }).Single().DungeonRun!.Id == first.Id,
            "Completion after a known map exit cannot manufacture a second run or duplicate the archived completion");
        Map(8, 600012); State(9, 600012, start + 8); Clear(10, 600002);
        check(engine.Fight.DungeonRun!.ClearedAt == null, "Old dungeon completion cannot mark the newly entered dungeon cleared");
        Clear(12, 600012); var second = engine.Fight.DungeonRun!; Map(13, 1010);
        Map(14, 600002); State(15, 600002, start + 14);
        events.Add(new { type = "dungeon-cleared", at = start + 16, map = 600002 });
        Send("dungeon-result", start + 16, new DungeonResultReading(600002, true, null, 0, []));
        check(engine.Fight.DungeonRun is { ClearedAt: not null, ClearLevel: null },
            "Zero clear level marks completion without inventing a known clear grade");
        Clear(17, 600002); var third = engine.Fight.DungeonRun!;
        check(third.ClearLevel == 2 && third.ClearedAt == start + 16,
            "Later clear grade fills an unspecified grade without changing the first completion time");
        Map(18, 1010);
        File.WriteAllText(Path.Combine(temp, "reference-dungeon-scope.json"), JsonSerializer.Serialize(new {
            IncludeCompletion = true, Events = events, Runs = new[] { first, second, third }.Select(r => new {
                Map = r.MapId, Entered = r.EnteredAt, Left = r == first ? start + 6 : r == second ? start + 13 : start + 18,
                Used = r.UsedItems, Received = r.ReceivedItems, r.StartedAt, r.ClearedAt, r.ClearLevel
            })
        }));
    }
}
