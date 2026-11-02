using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class FieldBossTrackerTests
{
    internal static void Run(Action<bool, string> check)
    {
        const long now = 1700000000000;
        NetworkEvent Event(int map, long seen, params FieldBossSlot[] slots) => new("field-boss-list", seen, "synthetic", JsonSerializer.SerializeToElement(new FieldBossList(map, slots)));
        var tracker = new FieldBossTracker();
        var initial = Event(1010, now, new FieldBossSlot(1, false, null, null, null, now + 60000));
        tracker.Apply(initial);
        var row = tracker.Rows(now).Single();
        check(FieldBossReferences.Rows.Count == 48 && row.Map == "Verteron" && row.Boss == "Neikel of the East" && row.Remaining == "0.00:01:00",
            "Field-boss timer rows use pinned reference names and reported UTC deadlines");
        check(tracker.Rows(now + 60000).Single().Status == "Timer elapsed; refresh in game",
            "A field-boss countdown reaching zero does not fabricate a live spawn");
        tracker.Apply(Event(1010, now - 1, new FieldBossSlot(1, true, 1, 2, 3, now)));
        check(tracker.Rows(now).Single().Status == "Reported respawn", "Stale map lists cannot overwrite newer field-boss observations");
        tracker.Apply(Event(1010, now + 1, new FieldBossSlot(2, true, 1, 2, 3, now)));
        check(tracker.Rows(now + 2) is [{ Slot: 2, Status: "Reported alive" }],
            "A new map list replaces its slot set instead of retaining disappeared entries");
        tracker.Apply(Event(9999, now, new FieldBossSlot(1, false, null, null, null, 0)));
        check(tracker.Rows(now).Single(r => r.MapId == 9999) is { Map: "Map 9999", Boss: "Slot 1", Status: "Respawn unknown" },
            "Unknown map identities and missing deadlines remain explicit without NPC guessing");
        for (int i = 0; i < 40; i++) tracker.Apply(Event(2000 + i, now + i, new FieldBossSlot(1, false, null, null, null, 0)));
        check(tracker.MapCount == 32 && tracker.Rows(now + 100).Length == 32,
            "Field-boss state evicts old maps at a fixed memory bound");
        var engine = new CombatEngine(); engine.Apply(new("network", Network: initial)); engine.NewFight("Next");
        check(engine.FieldBosses.MapCount == 1, "Resetting combat preserves field-boss observations independently of fight history");
        engine.Apply(new("local-player", Actor: new(1, "Player 1", true, false, 0, "Cleric", 1)));
        check(engine.FieldBosses.MapCount == 0, "Character and zone boundaries clear field-boss state to prevent cross-session attribution");
        engine.Apply(new("network", Network: initial));
        var login = new NetworkEvent("account-login", now + 10, "test", JsonSerializer.SerializeToElement(new LoginReading(0, 2301, [])));
        engine.Apply(new("network", Network: login));
        check(engine.FieldBosses.MapCount == 0, "Login clears old field-boss observations before a new local-player packet");
        engine.Apply(new("network", Network: initial with { Timestamp = now + 9 }));
        check(engine.FieldBosses.MapCount == 0, "Delayed field-boss lists cannot restore the previous session after login");
        engine.Apply(new("network", Network: initial with { Timestamp = now + 11 }));
        engine.Apply(new("network", Network: login));
        check(engine.FieldBosses.MapCount == 1, "Fresh field-boss lists are accepted and duplicate login preserves them");
        engine.Apply(new("local-player", Actor: new(2, "Player 2", true, false, 0, "Cleric", 2301)));
        engine.Apply(new("network", Network: initial with { Timestamp = now + 9 }));
        check(engine.FieldBosses.MapCount == 0, "New local identity does not remove field-boss stale-packet protection");
    }
}
