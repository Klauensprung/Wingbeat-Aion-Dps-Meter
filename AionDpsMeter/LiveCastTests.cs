using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class LiveCastTests
{
    internal static void Run(Action<bool, string> check)
    {
        NetworkEvent Event(string kind, long at, object value) => new(kind, at, "test", JsonSerializer.SerializeToElement(value));
        var tracker = new LiveCastTracker();
        var start = new CastReading(1, 17090000, 1, 2, 2000, 0, 0, 0, 0);
        tracker.Apply(Event("cast-start", 1000, start), 1);
        check(tracker.Snapshot(2000) is { Status: "Casting", RemainingSeconds: 1, Progress: .5 } && tracker.Snapshot(999) == null && tracker.Snapshot(3000) == null,
            "Live cast timing shows captured duration and hides at its deadline without claiming completion");
        tracker.Apply(Event("cast-start", 900, start with { CastingMs = 9000 }), 1);
        tracker.Apply(Event("cast-start", 1000, start), 1);
        tracker.Apply(Event("cast-stop", 1500, new CastStopReading(2, 6152, start.SkillCode, 0)), 1);
        tracker.Apply(Event("cast-stop", 1500, new CastStopReading(1, 6152, 18000000, 0)), 1);
        check(tracker.Snapshot(2000)?.RemainingSeconds == 1, "Stale starts, duplicate starts and unrelated actor/skill stops cannot alter a live cast");
        tracker.Apply(Event("cast-stop", 2100, new CastStopReading(1, 6152, start.SkillCode, 0)), 1);
        tracker.Apply(Event("cast-start", 1000, start), 1);
        check(tracker.Snapshot(2200) is { Status: "Interrupted", Progress: 0 } && tracker.Snapshot(3100) == null,
            "Interruption is displayed briefly and delayed packets cannot restart the stopped cast");
        tracker.Apply(Event("charge-start", 4000, start with { CastingMs = 0, ChargeRate = .5f }), 1);
        tracker.Apply(Event("cast-stop", 4100, new CastStopReading(1, 0, start.SkillCode, 0)), 1);
        check(tracker.Snapshot(10000) is { Status: "Charging", RemainingSeconds: null, Progress: null },
            "Charge rate alone does not invent duration and cast-stop does not terminate a charge");
        tracker.Apply(Event("charge-stop", 11000, new CastStopReading(1, 0, start.SkillCode, 0)), 1);
        check(tracker.Snapshot(11100)?.Status == "Cancelled", "Explicit charge cancellation is distinguished from completion");
        tracker.Apply(Event("charge-start", 12000, start), 1);
        tracker.Apply(Event("skill-use", 12100, new SkillStartReading(1, start.SkillCode, 1, 2, 0, 0, 0)), 1);
        check(tracker.Snapshot(12100) == null, "Actual skill execution clears the pending action");
        tracker.Apply(Event("cast-start", 13000, start), 1);
        tracker.Apply(Event("entity-death", 13100, new { EntityId = 1 }), 1);
        check(tracker.Snapshot(13100) == null, "Local death clears the pending action");
        tracker.Apply(Event("charge-start", 14000, start), 1);
        tracker.Apply(Event("local-zone", 14100, new { MapId = 1010 }), 0);
        check(tracker.Snapshot(14100) == null, "Map transition clears a cast even while identity is pending");
        var engine = new CombatEngine();
        engine.Apply(new("local-player", Actor: new(1, "Local", true, false, 0, "Cleric", 1)));
        engine.Apply(new("network", Network: Event("cast-start", 15000, start))); engine.NewFight("Reset");
        check(engine.LiveCast.Snapshot(15500)?.RemainingSeconds == 1.5, "Manual DPS reset preserves the current local action");
        engine.Apply(new("network", Network: Event("account-login", 16000, new LoginReading(0, 1, []))));
        engine.Apply(new("local-player", Actor: new(1, "New", true, false, 0, "Cleric", 1)));
        engine.Apply(new("network", Network: Event("cast-start", 15000, start)));
        check(engine.LiveCast.Snapshot(16000) == null, "Account/identity boundaries reject casts delayed from the old session");
        var sameClock = new LiveCastTracker();
        var used = new SkillStartReading(1, start.SkillCode, 1, 2, 0, 0, 0);
        sameClock.Apply(Event("cast-start", 20000, start), 1);
        sameClock.Apply(Event("skill-use", 20000, used), 1);
        sameClock.Apply(Event("cast-start", 20000, start), 1);
        check(sameClock.Snapshot(20100) == null, "Duplicate starts at the execution timestamp cannot resurrect a finished cast");
        sameClock.Apply(Event("cast-start", 20000, start with { SkillCode = 18000000 }), 1);
        check(sameClock.Snapshot(20100)?.SkillCode == 18000000, "Execution deduplication does not suppress a different skill at the same timestamp");
        sameClock.Clear(); sameClock.Apply(Event("skill-use", 21000, used), 1);
        sameClock.Apply(Event("skill-use", 20999, used with { SkillCode = 18000000 }), 1);
        sameClock.Apply(Event("cast-start", 21000, start), 1);
        check(sameClock.Snapshot(21100) == null, "Execution arriving before the same-timestamp start leaves no pending cast");
        sameClock.Apply(Event("cast-start", 22000, start), 1);
        check(sameClock.Snapshot(22100)?.SkillCode == start.SkillCode, "A later genuine cast of the same skill remains visible");
    }
}
