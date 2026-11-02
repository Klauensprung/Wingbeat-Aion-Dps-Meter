using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class DungeonIdentityTests
{
    internal static void Run(Action<bool, string> check)
    {
        var engine = new CombatEngine();
        var first = new Actor(1, "First", true, true, 0, "Cleric", 2301);
        var second = first with { Id = 2, Name = "Second" };
        void Identity(Actor actor) => engine.Apply(new("local-player", Actor: actor));
        void Send(string kind, long at, object fields) => engine.Apply(new("network", Network: new(kind, at, "test", JsonSerializer.SerializeToElement(fields))));
        void Inventory(long at, long count, bool full = false) => Send(full ? "inventory" : "inventory-update", at,
            new InventoryBatch(full, [new(100, 110120003, count, 1, 1, 5)], full ? (ushort)528 : (ushort)0));
        Identity(first);
        Send("local-zone", 10, new { MapId = 600002 });
        Inventory(11, 20, true); Inventory(12, 23);
        var previous = engine.Snapshot();
        Send("account-login", 20, new LoginReading(0, 2301, []));
        Send("local-zone", 21, new { MapId = 600002, AwaitingIdentity = true });
        Send("dungeon-state", 22, new DungeonStateReading(600002, 1, null, null));
        Send("dungeon-deaths", 23, new { Count = 2 });
        Send("zone-state", 24, new ZoneStateReading(600002, 2, 1700000000000, 1, 456));
        Inventory(25, 80, true);
        check(engine.Fight.DungeonRun?.Id == previous.DungeonRun?.Id && engine.Fight.DungeonRun?.ReportedDeaths == null,
            "Incoming dungeon state cannot mutate the previous character's run during identity handoff");
        Identity(second);
        check(engine.Fight.DungeonRun is { MapId: 600002, SawPrepare: true, ReportedDeaths: 2 } run && run.Id != previous.DungeonRun?.Id &&
            run.ReceivedItems.Length == 0 && engine.Fight.ObservedRun?.Reference == 456,
            "Prepared dungeon and instance metadata survive confirmation with a fresh run identity and no old rewards");
        Inventory(26, 84);
        check(engine.Fight.DungeonRun?.ReceivedItems is [{ Count: 4 }], "New owner rewards attach to the prepared dungeon after confirmation");
        var saved = engine.SaveCompleted(_ => { });
        check(saved.Any(f => f.LocalPlayer == first.Id && f.DungeonRun?.Id == previous.DungeonRun?.Id &&
            f.DungeonRun!.ReceivedItems is [{ Count: 3 }] && f.DungeonRun.ReportedDeaths == null),
            "Previous character's compact dungeon rewards remain available for autosave without incoming progress");
        var confirmed = engine.Fight.DungeonRun;
        Send("account-login", 20, new LoginReading(0, 2301, []));
        Send("dungeon-deaths", 27, new { Count = 3 });
        check(engine.Fight.DungeonRun?.Id == confirmed?.Id && engine.Fight.DungeonRun?.ReportedDeaths == 3,
            "Duplicate login cannot detach a confirmed dungeon run");
        Send("local-zone", 30, new { MapId = 600012, AwaitingIdentity = true });
        Send("dungeon-deaths", 31, new { Count = 1 });
        Identity(second);
        check(engine.Fight.DungeonRun is { MapId: 600012, ReportedDeaths: 1, ReceivedItems.Length: 0 },
            "Map preparation without account login preserves incoming dungeon state at identity confirmation");
        Identity(second);
        check(engine.Fight.DungeonRun == null && engine.Fight.ObservedRun == null,
            "Unprepared explicit identity reset still clears old dungeon context");
        Send("account-login", 40, new LoginReading(0, 2301, []));
        Send("local-zone", 41, new { MapId = 600002, AwaitingIdentity = true });
        Send("dungeon-deaths", 42, new { Count = 8 });
        Send("local-zone", 43, new { MapId = 600012, AwaitingIdentity = true });
        Identity(second);
        check(engine.Fight.DungeonRun is { MapId: 600012, ReportedDeaths: null },
            "A newer preparation replaces unconfirmed dungeon state instead of merging scenes");

        engine = new CombatEngine(); Identity(first);
        Send("local-zone", 10, new { MapId = 1010 }); Inventory(11, 20, true);
        engine.RecordDamage(new() { Timestamp = 12, ActorId = 1, TargetId = 99, SkillCode = 17010000, Damage = 100 }, false);
        Send("local-zone", 13, new { MapId = 600002 }); Inventory(14, 23);
        Send("account-login", 20, new LoginReading(0, 2301, []));
        Send("local-zone", 21, new { MapId = 600012, AwaitingIdentity = true });
        Send("dungeon-deaths", 22, new { Count = 2 }); Identity(second);
        bool failed = false;
        try { engine.SaveCompleted(_ => throw new System.IO.IOException("Synthetic save failure")); }
        catch (System.IO.IOException) { failed = true; }
        saved = engine.SaveCompleted(_ => { });
        check(failed && saved.Count(f => f.DungeonRun is { MapId: 600002, ReceivedItems: [{ Count: 3 }] } && f.LocalPlayer == 1 && !f.HasEvents) == 1,
            "Detached previous-owner rewards survive reconnect and a failed autosave exactly once");
        check(saved.All(f => f.DungeonRun?.MapId != 600012) && engine.Fight.DungeonRun is { MapId: 600012, ReportedDeaths: 2 },
            "Unconfirmed progress is not archived under the previous owner during handoff");
        engine = new CombatEngine(); Identity(first);
        void Roster(long at, string name) => engine.Apply(new("party", Party: new([], "Synthetic roster") {
            Timestamp = at, Summaries = [new(name, 2301, "Cleric", 50, 200, 100)] }));
        Roster(9, "Old member"); Send("local-zone", 10, new { MapId = 600002 });
        Send("account-login", 20, new LoginReading(0, 2301, []));
        Roster(21, "Incoming member");
        Send("local-zone", 22, new { MapId = 600012, AwaitingIdentity = true });
        Roster(23, "Late incoming member");
        check(engine.Fight.DungeonRun?.Party is [{ Name: "Old member" }],
            "Pending dungeon rosters do not change the previous run's member summary");
        Identity(second);
        check(engine.Fight.DungeonRun?.Party is [{ Name: "Incoming member" }, { Name: "Late incoming member" }],
            "Dungeon rosters captured before and after map preparation survive identity confirmation");
        Send("account-login", 30, new LoginReading(0, 2301, []));
        Identity(second);
        Roster(29, "Stale member");
        Send("local-zone", 31, new { MapId = 600002 });
        check(engine.Fight.DungeonRun?.Party.Length == 0,
            "Pre-login roster cannot populate a fresh dungeon after identity confirmation");
        Roster(32, "Fresh member");
        check(engine.Fight.DungeonRun?.Party is [{ Name: "Fresh member" }],
            "Fresh post-login roster still populates the current dungeon");
    }
}
