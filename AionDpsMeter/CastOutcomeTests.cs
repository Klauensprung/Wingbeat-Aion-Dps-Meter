using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class CastOutcomeTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var fight = new Fight(); fight.Actors[1] = new(1, "Caster", true, true, 0, "Cleric", 1);
        void Add(string kind, long at, object value) => fight.Network.Add(new(kind, at, "test", JsonSerializer.SerializeToElement(value)));
        CastReading Start(int skill = 17090000) => new(1, skill, 1, 2, 1000, 0, 0, 0, 0);
        Add("cast-stop", 500, new CastStopReading(1, 6152, 17090000, 0));
        Add("cast-start", 1000, Start()); Add("cast-stop", 1100, new CastStopReading(1, 6152, 17090000, 0));
        Add("cast-stop", 1200, new CastStopReading(1, 0, 17090000, 0));
        Add("cast-start", 2000, Start()); Add("cast-stop", 2100, new CastStopReading(1, 0, 17090000, 0));
        Add("cast-start", 3000, Start()); Add("cast-stop", 3100, new CastStopReading(1, 2, 17090000, 0));
        Add("cast-start", 4000, Start());
        Add("charge-start", 5000, Start()); Add("charge-stop", 5100, new CastStopReading(1, 6152, 17090000, 0));
        var summary = CastOutcomes.Build(fight);
        File.WriteAllText(Path.Combine(temp, "reference-cast-outcomes.json"), JsonSerializer.Serialize(new { Events = fight.Network, Expected = summary.Rows }));
        check(summary.Rows.Single(r => r.Kind == "cast") is { Starts: 4, Cancelled: 1, Interrupted: 1, Failed: 1, NoStopRecorded: 1 } &&
            summary.Rows.Single(r => r.Kind == "charge") is { Starts: 1, Interrupted: 1 },
            "Cast summaries distinguish explicit outcomes without counting duplicate/orphan stops or assuming success");
        var store = new FightStore(Path.Combine(temp, "cast-outcomes")); store.Save(fight);
        var saved = store.Load(Path.Combine(store.Root, fight.Id + ".json"));
        check(saved.Network.Count == 0 && CastOutcomes.Build(saved).Rows.SequenceEqual(summary.Rows) && fight.SavedCasts == null && saved.HasRecordedData,
            "Saved cast outcomes replace raw starts/stops and preserve results without mutating the original fight");
        var archive = FightArchiveSummary.Create(saved);
        check(archive.Network.Count == 0 && CastOutcomes.Build(archive).Rows.SequenceEqual(summary.Rows) && archive.Name(1) == "Caster",
            "Compact archive retains cast summaries and caster identity with no packet log");
        var copied = saved.Copy(); copied.SavedCasts!.Rows[0] = copied.SavedCasts.Rows[0] with { Starts = 99 };
        check(saved.SavedCasts!.Rows[0].Starts != 99, "Copying saved cast summaries isolates their mutable row array");
        fight.Network.Clear(); Add("cast-start", 1000, Start()); Add("local-zone", 1100, new { MapId = 1010 });
        Add("cast-stop", 1200, new CastStopReading(1, 6152, 17090000, 0));
        check(CastOutcomes.Build(fight).Rows.Single() is { Starts: 1, Interrupted: 1 }, "Within one recorded fight, a map event preserves reference cast-stop association");
        File.WriteAllText(Path.Combine(temp, "reference-cast-outcomes-boundary.json"), JsonSerializer.Serialize(new { Events = fight.Network, Expected = CastOutcomes.Build(fight).Rows }));
        fight.Network.Clear();
        Add("cast-start", 3000, Start());
        Add("charge-start", 1000, Start());
        Add("cast-stop", 2000, new CastStopReading(1, 6152, 17090000, 0));
        Add("charge-stop", 1500, new CastStopReading(1, 0, 17090000, 0));
        var delivered = CastOutcomes.Build(fight);
        check(delivered.Rows.Single(r => r.Kind == "cast") is { Starts: 1, NoStopRecorded: 1 } &&
            delivered.Rows.Single(r => r.Kind == "charge") is { Starts: 1, Interrupted: 1, Cancelled: 0 },
            "Delivered cast order determines the latest action even when timestamps arrive out of order");
        File.WriteAllText(Path.Combine(temp, "reference-cast-outcomes-delivery.json"), JsonSerializer.Serialize(new { Events = fight.Network, Expected = delivered.Rows }));
        var reducer = new CastOutcomeAccumulator();
        foreach (var message in fight.Network) reducer.Apply(message);
        check(reducer.Snapshot().Rows.SequenceEqual(delivered.Rows), "Incremental cast reduction agrees with historical delivery-order reduction");
        var isolated = reducer.Snapshot(); isolated.Rows[0] = isolated.Rows[0] with { Starts = 99 };
        check(reducer.Snapshot().Rows.All(r => r.Starts == 1), "Cast accumulator snapshots cannot mutate running counts");
        var many = new CastOutcomeAccumulator();
        var repeatedStart = fight.Network[0]; var repeatedStop = fight.Network[2];
        for (int i = 0; i < 100000; i++) { many.Apply(repeatedStart); many.Apply(repeatedStop); }
        check(many.RowCount == 1 && many.PendingCount == 0 && many.Snapshot().Rows.Single() is { Starts: 100000, Interrupted: 100000 },
            "Two hundred thousand cast messages reduce to one row with no retained pending actions");
        fight.Network.Clear();
        for (int i = 1; i <= 2050; i++) Add("cast-start", i, Start(i));
        var bounded = CastOutcomes.Build(fight);
        var boundedReducer = new CastOutcomeAccumulator();
        foreach (var message in fight.Network) boundedReducer.Apply(message);
        check(boundedReducer.RowCount == 2048 && boundedReducer.PendingCount == 2048 && boundedReducer.Snapshot().OmittedStarts == 2,
            "Incremental cast rows and pending associations both remain bounded across unique skills");
        check(bounded.Rows.Length == 2048 && bounded.OmittedStarts == 2, "Cast summaries have an explicit 2048-row bound and report omitted starts");
        fight.Actors[2] = new(2, "Archived caster", true, false, 0, "Cleric", 1) { OriginalEntityId = 1 };
        check(CastOutcomes.Build(fight) is { Rows.Length: 0, OmittedStarts: 2050 }, "Ambiguous reused entity IDs cannot misattribute raw cast history");
        LiveEngine(check, temp);
        bool Reject(SavedCastOutcomes value) { try { CastOutcomes.Validate(value); return false; } catch (InvalidDataException) { return true; } }
        check(Reject(new([summary.Rows[0], summary.Rows[0]], 0)) && Reject(new([summary.Rows[0] with { Interrupted = int.MaxValue }], 0)) &&
            Reject(new([summary.Rows[0] with { Kind = "unknown" }], 0)), "Imported cast summaries reject duplicate rows, impossible outcomes and unknown kinds");
    }
    static void LiveEngine(Action<bool, string> check, string temp)
    {
        AionDpsMeter.Models.NetworkEvent Event(string kind, long at, object value) => new(kind, at, "test", JsonSerializer.SerializeToElement(value));
        var start = new CastReading(1, 17090000, 1, 10, 1000, 0, 0, 0, 0);
        var stop = new CastStopReading(1, 6152, 17090000, 0);
        CombatEngine Engine() {
            var e = new CombatEngine();
            e.Apply(new("local-player", Actor: new(1, "Caster", true, true, 0, "Cleric", 1)));
            e.Apply(new("actor", Actor: new(10, "Target", false, false, 0, "Unknown", 0) { NpcId = 100 }));
            return e;
        }
        void Feed(CombatEngine e, AionDpsMeter.Models.NetworkEvent n) => e.Apply(new("network", Network: n));
        void Hit(CombatEngine e, long at, bool heal = false) => e.Apply(new(heal ? "heal" : "damage", Damage: new() { ActorId = 1, TargetId = heal ? 1 : 10, Timestamp = at, Damage = 100 }));
        var engine = Engine(); var begin = Event("cast-start", 1000, start); var end = Event("cast-stop", 1100, stop);
        for (int i = 0; i < 10000; i++) { Feed(engine, begin); Feed(engine, end); }
        check(engine.Fight.Network.Count == 0 && engine.Fight.LiveCasts is { RowCount: 1, PendingCount: 0 } &&
            CastOutcomes.Build(engine.Fight).Rows.Single() is { Starts: 10000, Interrupted: 10000 },
            "Live engine retains one cast summary and no network log for twenty thousand cast packets");
        Feed(engine, begin); var detached = engine.Snapshot(); Feed(engine, end);
        check(CastOutcomes.Build(detached).Rows.Single().Interrupted == 10000 && CastOutcomes.Build(engine.Fight).Rows.Single().Interrupted == 10001,
            "Live cast snapshots isolate both counts and pending associations");
        var store = new FightStore(Path.Combine(temp, "live-casts")); store.Save(engine.Snapshot());
        check(CastOutcomes.Build(store.Load(Path.Combine(store.Root, engine.Fight.Id + ".json"))).Rows.SequenceEqual(CastOutcomes.Build(engine.Fight).Rows),
            "Live cast aggregates survive save/load without raw packets");
        engine.NewFight("Manual reset"); Feed(engine, end);
        check(CastOutcomes.Build(engine.Fight).Rows.Length == 0, "Manual reset cannot attach a stop to the prior fight's cast");
        Feed(engine, begin);
        engine.Apply(new("actor", Actor: new(1, "Different caster", true, true, 0, "Cleric", 1)));
        Feed(engine, end); Feed(engine, begin); Feed(engine, end);
        var identities = CastOutcomes.Build(engine.Fight).Rows;
        check(identities.Length == 2 && identities.Single(r => r.ActorId == 1).Interrupted == 1 &&
            identities.Single(r => r.ActorId != 1) is { Starts: 1, Interrupted: 0 } && engine.Fight.Name(identities.Single(r => r.ActorId != 1).ActorId) == "Caster",
            "Reused entity IDs archive cast counts under the original identity and clear pending association");
        var differentFight = Engine(); Hit(differentFight, 1000); string oldFight = differentFight.Fight.Id;
        Feed(differentFight, begin); differentFight.AdvanceTime(16000); Feed(differentFight, end);
        differentFight.Apply(new("damage", Damage: new() { ActorId = 1, TargetId = 20, Timestamp = 17000, Damage = 100 }));
        check(differentFight.Fight.Id != oldFight && CastOutcomes.Build(differentFight.Fight).Rows.Length == 0,
            "Choosing a different encounter discards the recovery cast continuation without leaking prior counts");
        var castOnly = new CombatEngine(); Feed(castOnly, begin); castOnly.EndFight("Next");
        var completed = castOnly.SaveCompleted(_ => { });
        check(completed.Length == 1 && CastOutcomes.Build(completed[0]).Rows.Single().Starts == 1 && completed[0].Network.Count == 0,
            "Cast-only encounters remain saveable after raw event retention is removed");
        foreach (bool healingPause in new[] { false, true }) {
            var recovery = Engine(); Hit(recovery, 1000); string original = recovery.Fight.Id;
            var events = new List<AionDpsMeter.Models.NetworkEvent>();
            void Cast(string kind, long at, object value) { var n = Event(kind, at, value); events.Add(n); Feed(recovery, n); }
            Cast("cast-start", 15000, start); recovery.AdvanceTime(16000);
            Cast("cast-stop", 17000, stop);
            if (healingPause) { Hit(recovery, 18000, true); recovery.AdvanceTime(33000); }
            Cast("charge-start", 34000, start); Cast("charge-stop", 34100, stop with { Result = 0 });
            Hit(recovery, 35000);
            var results = CastOutcomes.Build(recovery.Fight);
            check(recovery.Fight.Id == original && results.Rows.Single(r => r.Kind == "cast") is { Starts: 1, Interrupted: 1 } &&
                results.Rows.Single(r => r.Kind == "charge") is { Starts: 1, Cancelled: 1 } && recovery.Fight.Network.Count == 0 && recovery.Fight.Healing.Count == (healingPause ? 1 : 0),
                "Automatic recovery preserves cross-pause cast stops without logs" + (healingPause ? " through a healing-only segment" : ""));
            File.WriteAllText(Path.Combine(temp, healingPause ? "reference-cast-recovery-healing.json" : "reference-cast-recovery.json"),
                JsonSerializer.Serialize(new { Events = events, Expected = results.Rows }));
            Cast("cast-stop", 36000, stop);
            check(CastOutcomes.Build(recovery.Fight).Rows.SequenceEqual(results.Rows), "Recovered cast state still rejects duplicate stops");
        }
    }

}
