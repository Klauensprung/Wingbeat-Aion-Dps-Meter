using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class SceneIdentityBoundaryTests
{
    internal static void Run(Action<bool, string> check)
    {
        var engine = new CombatEngine();
        var owner = new Actor(1, "Original", true, false, 0, "Cleric", 2301);
        engine.Apply(new("local-player", Actor: owner));
        void Send(string kind, long at, object fields) => engine.Apply(new("network", Network: new(kind, at, "test", JsonSerializer.SerializeToElement(fields))));
        Send("local-zone", 10, new { MapId = 1010, X = 1, Y = 2, Z = 3 });
        Send("local-target", 11, new { EntityId = 100 });
        Send("party-boss-target", 12, new { EntityId = 200 });
        var before = engine.Snapshot();
        Send("account-login", 20, new LoginReading(0, 2301, []));
        check(engine.Position.Latest == null && engine.Fight.LocalPosition == null &&
            engine.Fight.LocalTarget?.EntityId is null or 0 && engine.Fight.PartyBossTarget?.EntityId is null or 0,
            "Accepted login clears old position and target state before new local identity arrives");
        Send("local-zone", 19, new { MapId = 1010, X = 4, Y = 5, Z = 6 });
        Send("local-position", 19, new { X = 4, Y = 5, Z = 6 });
        Send("local-target", 19, new { EntityId = 101 });
        Send("party-boss-target", 19, new { EntityId = 201 });
        check(engine.Position.Latest == null && engine.Spatial.MapId == 0 && engine.Fight.LocalTarget?.EntityId is null or 0 && engine.Fight.PartyBossTarget?.EntityId is null or 0,
            "Pre-login positions, zones and targets cannot recreate the previous scene");
        Send("local-position", 21, new { X = 7, Y = 8, Z = 9 });
        check(engine.Position.Latest == null, "Post-login position without a captured map cannot reuse the old map");
        Send("local-zone", 22, new { MapId = 1110, X = 10, Y = 11, Z = 12, AwaitingIdentity = true });
        Send("actor-position", 23, new { ActorId = 1, X = 999, Y = 999, Z = 999 });
        check(engine.Position.Latest is { MapId: 1110, X: 10 }, "Pending identity cannot treat an old actor ID as current local movement");
        engine.Apply(new("local-player", Actor: owner with { Id = 2, Name = "New owner" }));
        Send("local-zone", 19, new { MapId = 1010, X = 4, Y = 5, Z = 6 });
        Send("local-target", 19, new { EntityId = 101 });
        check(engine.Position.Latest == null && engine.Fight.LocalTarget == null, "Identity confirmation does not erase the stale-event boundary");
        Send("local-position", 23, new { X = 15, Y = 16, Z = 17 });
        check(engine.Position.Latest is { MapId: 1110, X: 15 }, "Fresh position after identity confirmation uses the already captured prepared map");
        Send("local-zone", 24, new { MapId = 1110, X = 20, Y = 21, Z = 22 });
        Send("target", 25, new { ActorId = 2, TargetId = 300 });
        check(engine.Position.Latest is { MapId: 1110, X: 20 } && engine.Fight.LocalTarget?.EntityId == 300 && before.LocalPosition?.MapId == 1010 && before.LocalTarget?.EntityId == 100,
            "Fresh new-character position and target work without rewriting historical snapshots");
        Send("account-login", 20, new LoginReading(0, 2301, []));
        check(engine.Position.Latest is { X: 20 } && engine.Fight.LocalTarget?.EntityId == 300, "Duplicate earlier login cannot clear the new character's position or target");
        Send("local-zone", 30, new { MapId = 1010, X = 30, Y = 31, Z = 32, AwaitingIdentity = true });
        check(engine.Fight.LocalTarget == null, "Map preparation clears old local targeting before identity confirmation");
        engine = new CombatEngine();
        engine.Apply(new("local-player", Actor: owner));
        Send("local-zone", 10, new { MapId = 600002 });
        Send("account-login", 20, new LoginReading(0, 2301, []));
        engine.Apply(new("local-player", Actor: owner with { Id = 2, Name = "New owner" }));
        Send("local-zone", 19, new { MapId = 600002 });
        Send("dungeon-state", 19, new DungeonStateReading(600002, 1, null, null));
        check(engine.Fight.DungeonRun == null, "Pre-login dungeon packets cannot recreate an old run after identity confirmation");
        Send("zone-state", 19, new ZoneStateReading(600002, 2, 1700000000000, 1, 123));
        check(engine.Fight.ObservedRun == null, "Pre-login instance metadata cannot recreate an observed run after identity confirmation");
        Send("zone-state", 25, new ZoneStateReading(600012, 2, 1700000001000, 1, 456));
        check(engine.Fight.ObservedRun?.Reference == 456, "Current-session instance metadata remains available after reconnect");
        Send("local-zone", 26, new { MapId = 600012 });
        string? freshRun = engine.Fight.DungeonRun?.Id;
        Send("dungeon-deaths", 19, new { Count = 9 });
        check(freshRun != null && engine.Fight.DungeonRun?.ReportedDeaths == null,
            "Fresh dungeon state works while delayed previous-session deaths are ignored");
    }
}
