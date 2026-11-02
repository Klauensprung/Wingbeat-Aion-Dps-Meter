using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class GatheringLedgerTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        string path = Path.Combine(temp, "gathering-tracking.json");
        var ledger = new FarmingCounterLedger(path);
        var engine = new CombatEngine(gatheringTracking: ledger);
        var fixtures = new List<object>();
        void Step(string kind, long at, int id = 0, int objectId = 0) {
            if (kind == "reset") ledger.Reset(at);
            else if (kind == "restart") { ledger.Save(); ledger = new(path); engine = new(gatheringTracking: ledger); }
            else if (kind == "dps-reset") engine.NewFight("Next fight");
            else {
                string name = kind switch { "object" => "world-object", "map" => "local-zone", "object-removed" => "world-object-removed", _ => kind };
                object fields = kind switch {
                    "object" => new WorldObjectReading(id, objectId, 1, 2, 3, 0, true, 0, 0, 0, 0),
                    "map" => new { MapId = id }, "object-removed" => new WorldObjectRemoval(id, 0, 0, 0), _ => new { EntityId = id }
                };
                engine.Apply(new("network", Network: new(name, at, "synthetic", JsonSerializer.SerializeToElement(fields))));
            }
            fixtures.Add(new { Kind = kind, At = at, Entity = id, Object = objectId, Expected = ledger.Snapshot().Counts });
        }
        Step("object", 1, 1, 1500045); Step("gathered", 2, 1);
        var old = ledger.Snapshot(); var fight = engine.Snapshot();
        Step("dps-reset", 3); Step("gathered", 4, 1);
        check(ledger.Snapshot().Counts[1500045] == 2 && old.Counts[1500045] == 1 && fight.GatheredObjects[1500045] == 1,
            "Independent gathering tracking survives DPS resets and returns immutable snapshots");
        Step("map", 5, 1110); Step("gathered", 6, 1);
        Step("object", 7, 2, 1590001); Step("gathered", 8, 2); Step("object-removed", 9, 2); Step("gathered", 10, 2);
        check(ledger.Snapshot().Counts[1500045] == 2 && ledger.Snapshot().Counts[1590001] == 1,
            "Map changes preserve cumulative gathers but unknown and removed objects cannot add counts");
        Step("restart", 11);
        check(ledger.LoadError == null && ledger.Snapshot().Counts[1500045] == 2 && ledger.Snapshot().Counts[1590001] == 1,
            "Gathering tracking survives application restart without loading saved fight history");
        Step("object", 12, 3, 1590001); Step("gathered", 13, 3);
        var priorFight = engine.Snapshot(); Step("reset", 14); Step("gathered", 15, 3);
        check(ledger.Snapshot() is { Since: 14, Omitted: 0 } reset && reset.Counts.Count == 1 && reset.Counts[1590001] == 1 && priorFight.GatheredObjects[1590001] == 1,
            "Explicit tracking reset preserves live objects and archived fight summaries while restarting counters");
        Step("restart", 16);
        check(ledger.Snapshot().Since == 14 && ledger.Snapshot().Counts[1590001] == 1,
            "Explicit gathering reset and subsequent totals persist together");
        File.WriteAllText(Path.Combine(temp, "reference-gathering-tracking.json"), JsonSerializer.Serialize(fixtures));

        var bounded = new FarmingCounterLedger(); var objects = new WorldObjectTracker();
        for (int id = 1; id <= 4100; id++) {
            objects.Apply(new("world-object", id, "synthetic", JsonSerializer.SerializeToElement(new WorldObjectReading(1, id, 0, 0, 0, 0, true, 0, 0, 0, 0))));
            bounded.Apply(objects, new("gathered", id, "synthetic", JsonSerializer.SerializeToElement(new { EntityId = 1 })));
        }
        check(bounded.Snapshot() is { Counts.Count: 4096, Omitted: 4 }, "Independent gathering tracking bounds object categories and reports omissions");
        string broken = Path.Combine(temp, "gathering-invalid.json"); File.WriteAllText(broken, "{invalid");
        var invalid = new FarmingCounterLedger(broken); invalid.Save();
        check(invalid.LoadError != null && invalid.Snapshot().Counts.Count == 0 && File.ReadAllText(broken) == "{invalid",
            "Malformed tracking file is retained and never silently replaced during load");
        File.WriteAllText(broken, JsonSerializer.Serialize(new FarmingCounterSnapshot(0, new() { [0] = -1 }, 0)));
        check(new FarmingCounterLedger(broken).LoadError != null, "Invalid persisted gathering IDs and counts fail safely without crashing startup");
        string blocked = Path.Combine(temp, "gathering-blocked.json"); Directory.CreateDirectory(blocked);
        var retry = new FarmingCounterLedger(blocked); retry.Reset(100); bool failed = false;
        try { retry.Save(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
        Directory.Delete(blocked); retry.Save();
        check(failed && new FarmingCounterLedger(blocked).Snapshot().Since == 100 && !File.Exists(blocked + ".tmp"),
            "Failed gathering save preserves pending revision for retry and cleans its temporary file");
    }
}
