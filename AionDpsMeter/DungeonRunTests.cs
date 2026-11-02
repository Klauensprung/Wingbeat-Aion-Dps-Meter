using System.Text.Json;
using System.IO;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class DungeonRunTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        const long start = 1700000000000;
        NetworkEvent Event(string kind, long time, object value) => new(kind, time, "synthetic", JsonSerializer.SerializeToElement(value));
        NetworkEvent State(byte phase, long time, long? began = null, long? deadline = null) => Event("dungeon-state", time, new DungeonStateReading(600002, phase, began, deadline));
        NetworkEvent Result(long time, bool success = true) => Event("dungeon-result", time, new DungeonResultReading(600002, success, null, 2, []));
        var tracker = new DungeonRunTracker(); tracker.Apply(Result(start)); var completed = tracker.Current!;
        tracker.Apply(State(3, start + 100, start, start + 600000));
        check(tracker.Current!.Id == completed.Id && tracker.Current.ClearedAt == start && tracker.Current.ClearSeconds == null,
            "Result-before-state completion retains success without interpreting a post-clear timer as run duration");
        tracker.Apply(Result(start + 500)); tracker.Apply(State(2, start - 1, start - 10000, start));
        check(tracker.Current.ClearedAt == start && tracker.Current.StartedAt == null, "Repeated results and stale states cannot move completion or invent a start");
        tracker.Apply(State(4, start + 900)); tracker.Apply(State(1, start + 1000));
        check(tracker.Current.Id != completed.Id && tracker.Current.ClearedAt == null && tracker.Current.SawPrepare,
            "Preparing a new run after an explicit end creates a separate run identity");
        tracker.Apply(State(2, start + 2000, start + 1500, start + 900000)); tracker.Apply(Result(start + 6500));
        check(tracker.Current.ClearSeconds == 5, "Clear duration uses the process-state start and successful result time");
        tracker.Apply(Event("local-zone", start + 7000, new { MapId = 1010 }));
        check(tracker.Current == null, "A zone transition clears the active dungeon context");
        tracker.Apply(Result(start + 8000, false));
        check(tracker.Current == null, "A delayed result for a previous dungeon cannot recreate its context after a known map exit");
        tracker.Clear(); tracker.Apply(Result(start + 8000, false));
        check(tracker.Current!.ClearedAt == null, "A failed dungeon result never marks a run cleared");

        var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: new(1, "Player 1", true, true, 0, "Cleric", 1) { CharacterId = 30 }));
        void Send(NetworkEvent e) => engine.Apply(new("network", Network: e));
        Send(State(1, start)); Send(State(2, start + 1000, start + 1000, start + 601000));
        engine.Apply(new("damage", Damage: new() { ActorId = 1, TargetId = 100, SkillCode = 17010001, Damage = 100, Timestamp = start + 2000 }));
        var first = engine.Snapshot(); engine.NewFight("Synthetic next fight"); Send(Result(start + 11000));
        var final = engine.Snapshot();
        check(first.DungeonRun!.ClearedAt == null && final.DungeonRun!.Id == first.DungeonRun.Id && final.DungeonRun.ClearSeconds == 10,
            "DPS resets preserve dungeon identity and old snapshots while later completion updates the new snapshot");
        var group = ObservedRunGroups.Build([FightArchiveSummary.Create(first), FightArchiveSummary.Create(final)]).Single();
        check(group.Fights == 1 && group.Status == "Completion captured" && group.ClearTime != "Not captured" && group.Members.Single().Damage == 100,
            "Compact history groups a completion-only snapshot with its earlier fight and preserves exact combat totals");
        var store = new FightStore(Path.Combine(temp, "dungeon-runs")); store.Save(final);
        var loaded = store.Load(Path.Combine(store.Root, final.Id + ".json"));
        check(JsonSerializer.Serialize(loaded.DungeonRun) == JsonSerializer.Serialize(final.DungeonRun) && loaded.HasRecordedData, "Completion-only run metadata survives disk storage");
        Send(Event("local-zone", start + 12000, new { MapId = 1010 }));
        check(engine.Fight.DungeonRun == null, "An idle completed-run snapshot yields to the next zone");
        var queued = engine.SaveCompleted(_ => { });
        check(queued.Any(f => f.DungeonRun?.ClearedAt == start + 11000 && f.Id != final.Id),
            "Leaving before autosave queues a separate immutable completion record instead of losing the run");
        engine.Apply(new("local-player", Actor: new(1, "Player 1", true, true, 0, "Cleric", 1)));
        check(engine.Fight.DungeonRun == null, "Explicit character reconnect clears the dungeon tracker");
        var partial = first.Copy(); partial.DungeonRun = completed;
        check(ObservedRunGroups.Build([partial]).Single() is { Status: "Completion captured; partial entry", ClearTime: "Not captured" },
            "Completion without a process-state start is labeled partial and has no fabricated clear duration");
        var progress = new DungeonRunTracker();
        progress.Apply(Event("dungeon-boss-killed", start, new { NpcId = 123 }));
        progress.Apply(Event("dungeon-deaths", start, new { Count = 4 }));
        check(progress.Current == null, "Unscoped boss-kill and death-count notifications cannot invent a dungeon");
        progress.Apply(State(2, start, start, start + 600000));
        progress.Apply(Event("dungeon-boss-killed", start + 1, new { NpcId = 123 })); var beforeKills = progress.Current!;
        progress.Apply(Event("dungeon-boss-killed", start + 2, new { NpcId = 123 }));
        progress.Apply(Event("dungeon-boss-killed", start + 3, new { NpcId = 456 }));
        progress.Apply(Event("dungeon-deaths", start + 4, new { Count = 3 }));
        progress.Apply(Event("dungeon-deaths", start + 2, new { Count = 99 }));
        check(progress.Current is { BossKills.Length: 2, ReportedDeaths: 3, ClearedAt: null } && beforeKills.BossKills!.Length == 1,
            "Dungeon progress deduplicates boss templates, rejects stale counts and preserves earlier immutable summaries without asserting a clear");
        var progressFight = new Fight { LocalPlayer = 1, DungeonRun = progress.Current };
        progressFight.Actors[1] = new(1, "Progress player", true, true, 0, "Cleric", 1);
        store.Save(progressFight); var savedProgress = store.Load(Path.Combine(store.Root, progressFight.Id + ".json"));
        check(savedProgress.HasRecordedData && SavedHistory.Includes(savedProgress) && savedProgress.DungeonRun!.BossKills!.SequenceEqual(progress.Current!.BossKills!) &&
            ObservedRunGroups.Build([FightArchiveSummary.Create(savedProgress)]).Single() is { CapturedBossKills: 2, ReportedDeaths: "3", Fights: 0, ClearTime: "Not captured" },
            "Progress-only run summaries survive storage and expose captured boss kills and reported death counts");
        progress.Apply(Event("local-zone", start + 5, new { MapId = 1010 }));
        progress.Apply(Event("dungeon-deaths", start + 6, new { Count = 8 }));
        check(progress.Current == null, "Zone exits discard the context needed to attribute later death counts");
        var idle = new CombatEngine(); idle.Apply(new("local-player", Actor: progressFight.Actors[1]));
        idle.Apply(new("network", Network: State(2, start)));
        idle.Apply(new("network", Network: Event("dungeon-boss-killed", start + 1, new { NpcId = 123 })));
        idle.Apply(new("network", Network: Event("local-zone", start + 2, new { MapId = 1010 })));
        check(idle.SaveCompleted(_ => { }).Any(f => f.DungeonRun?.BossKills?.Length == 1) && idle.Fight.Network.All(n => n.Kind != "dungeon-boss-killed"),
            "Leaving an idle run preserves compact boss-kill progress without retaining its raw notification");
        var bounded = new DungeonRunTracker(); bounded.Apply(State(2, start));
        for (int i = 1; i <= 70; i++) bounded.Apply(Event("dungeon-boss-killed", start + i, new { NpcId = i }));
        check(bounded.Current!.BossKills!.Length == 64, "Dungeon boss progress has bounded retention even with excessive unique notifications");
        bool invalid = false;
        try { DungeonRunTracker.Validate(bounded.Current with { ReportedDeaths = -1 }); } catch (InvalidDataException) { invalid = true; }
        check(invalid, "Imported dungeon progress rejects negative reported death counts");
    }
}
