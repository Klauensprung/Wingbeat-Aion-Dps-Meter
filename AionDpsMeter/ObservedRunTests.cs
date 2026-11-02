using System.Text.Json;
using System.IO;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class ObservedRunTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var run = new ObservedRun(600012, 1700000003000, 2, 123, 2, 1000);
        NetworkEvent State(int phase, long time, long stamp = 1700000003000) => new("zone-state", time, "synthetic",
            JsonSerializer.SerializeToElement(new ZoneStateReading(600012, phase, stamp, 2, 123)));
        var tracker = new ObservedRunTracker(); tracker.Apply(State(2, 1000)); tracker.Apply(State(3, 2000)); tracker.Apply(State(2, 3000));
        check(tracker.Current is { State: 3, ObservedAt: 2000 }, "Repeated observed run phases do not rewind state or shift its first observation");
        tracker.Apply(new("local-zone", 4000, "synthetic", JsonSerializer.SerializeToElement(new { MapId = 600012 })));
        tracker.Apply(State(2, 3500));
        check(tracker.Current == null, "Same-area teleports clear instance attribution and reject older phase packets");
        var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: new(1, "Local", true, true, 0, "Cleric", 1)));
        engine.Apply(new("network", Network: State(2, 1000)));
        engine.Apply(new("damage", Damage: new DamageEvent { ActorId = 1, TargetId = 100, Damage = 10, Timestamp = 1500 }));
        var saved = engine.Snapshot(); engine.NewFight("Reset");
        check(engine.Fight.ObservedRun == run && !engine.Fight.MixedRunContexts && saved.ObservedRun == run,
            "Observed run context survives a DPS reset and remains immutable in the previous snapshot");
        engine.Apply(new("damage", Damage: new DamageEvent { ActorId = 1, TargetId = 100, Damage = 10, Timestamp = 2000 }));
        engine.Apply(new("network", Network: State(2, 2500, 1700001000000)));
        check(!engine.Fight.MixedRunContexts && engine.Fight.ObservedRun == run,
            "Leaving a run after its final hit preserves the completed fight's original grouping");
        engine.Apply(new("damage", Damage: new DamageEvent { ActorId = 1, TargetId = 100, Damage = 10, Timestamp = 3000 }));
        check(engine.Fight.MixedRunContexts, "A fight spanning different instance timestamps is excluded from automatic run grouping");
        engine.Apply(new("local-player", Actor: new(2, "Other", true, true, 0, "Ranger", 1)));
        check(engine.Fight.ObservedRun == null && !engine.Fight.MixedRunContexts, "Character resets discard observed run attribution");
        CombatEngine Recovery(bool different) {
            var e = new CombatEngine(); e.Apply(new("local-player", Actor: new(1, "Local", true, true, 0, "Cleric", 1)));
            e.Apply(new("actor", Actor: new(100, "Boss", false, false, 0, "Unknown", 0) { NpcId = 100 }));
            e.Apply(new("network", Network: State(2, 1000)));
            e.Apply(new("damage", Damage: new DamageEvent { ActorId = 1, TargetId = 100, Damage = 10, Timestamp = 1500 }));
            e.AdvanceTime(16500);
            if (different) e.Apply(new("network", Network: State(2, 17000, 1700001000000)));
            e.Apply(new("damage", Damage: new DamageEvent { ActorId = 1, TargetId = 100, Damage = 20, Timestamp = 18000 }));
            return e;
        }
        check(Recovery(false).Fight.Damage.Count == 2 && Recovery(true).Fight.Damage.Count == 1,
            "Idle fight recovery can resume the same run but cannot merge reused targets from another instance timestamp");
        var first = FeatureTests.Sample(); first.ObservedRun = run; first.Deaths.Add(new(first.LocalPlayer, 2000));
        var second = first.Copy(); second.Id = Guid.NewGuid().ToString("N"); second.ObservedRun = run with { State = 3, ObservedAt = 100000 };
        second.Damage = second.Damage.Select(h => h with { Timestamp = h.Timestamp + 60000 }).ToList();
        second.Healing = second.Healing.Select(h => h with { Timestamp = h.Timestamp + 60000 }).ToList();
        var group = ObservedRunGroups.Build([first, second, first]).Single();
        var member = group.Members.Single(m => m.Character == first.Name(first.LocalPlayer));
        check(group.Fights == 2 && group.CapturedSeconds == 90 && member.Damage == 4000 && member.Healing == 2000 && member.Deaths == 2 &&
            Math.Abs(member.CapturedDps - 44.4) < .01 && group.Status.Contains("unconfirmed"),
            "Observed runs aggregate unique fights, party deaths and elapsed-interval rates without asserting a clear");
        var compact = ObservedRunGroups.Build([FightArchiveSummary.Create(first), FightArchiveSummary.Create(second)]).Single();
        check(compact.Members.SequenceEqual(group.Members) && compact.Encounters.All(f => f.Damage.Count == 0 && f.Network.Count == 0),
            "Compact archive summaries reproduce run totals and deaths without retaining raw combat or network events");
        second.Actors[1] = second.Actors[1] with { CharacterId = 123 };
        var enriched = ObservedRunGroups.Build([first, second]).Single();
        check(enriched.Fights == 2 && enriched.Members.Single(m => m.Character == first.Name(1)).Damage == 4000,
            "A uniquely resolved character ID enriches earlier named captures without splitting a run or member totals");
        const long wide = 650000000000000001;
        var identified = first.Copy(); identified.Actors[1] = identified.Actors[1] with { PersistentId = wide, CharacterId = 0 };
        var renamed = second.Copy(); renamed.Actors[1] = renamed.Actors[1] with { PersistentId = wide, CharacterId = 0, Name = "Renamed character" };
        var renamedGroup = ObservedRunGroups.Build([FightArchiveSummary.Create(identified), FightArchiveSummary.Create(renamed)]).Single();
        check(renamedGroup.Character == "Renamed character" && renamedGroup.Members.Single(m => m.Character == "Renamed character").Damage == 4000,
            "Full persistent identity joins renamed characters across compact run snapshots and displays the latest name");
        var namesake = second.Copy(); namesake.Actors[1] = namesake.Actors[1] with { PersistentId = wide + 1, CharacterId = 0 };
        check(ObservedRunGroups.Build([identified, namesake]).Length == 2,
            "Same-name characters with distinct full IDs never merge dungeon history through a truncated or missing legacy ID");
        var unknown = first.Copy(); unknown.Id = Guid.NewGuid().ToString("N"); unknown.Actors[1] = unknown.Actors[1] with { PersistentId = 0, CharacterId = 0 };
        check(ObservedRunGroups.Build([identified, namesake, unknown]).Length == 3 && ObservedRunGroups.Build([identified, unknown]).Length == 1,
            "Name-only history enriches a unique full identity but remains isolated when namesakes make attribution ambiguous");
        var reusedServer = renamed.Copy(); reusedServer.Actors[1] = reusedServer.Actors[1] with { Server = 9999 };
        check(ObservedRunGroups.Build([identified, reusedServer]).Length == 2,
            "Full persistent identity remains server-scoped in dungeon history");
        var store = new FightStore(Path.Combine(temp, "observed-runs")); store.Save(second);
        var restored = store.Load(Path.Combine(store.Root, second.Id + ".json"));
        check(restored.ObservedRun == second.ObservedRun && ObservedRunGroups.Build([restored]).Single().Members.Any(m => m.Deaths == 1),
            "Saved run context and member deaths survive full fight serialization and reload");
        var invalid = second.Copy(); invalid.Id = Guid.NewGuid().ToString("N"); invalid.ObservedRun = run with { State = 99 }; store.Save(invalid);
        bool rejected = false;
        try { store.Load(Path.Combine(store.Root, invalid.Id + ".json")); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "Imported run metadata rejects unsupported states before entering archive aggregation");
        var otherServer = second.Copy(); otherServer.Id = Guid.NewGuid().ToString("N"); otherServer.Actors[1] = otherServer.Actors[1] with { Server = 9999 };
        check(ObservedRunGroups.Build([first, otherServer]).Length == 2,
            "Identical run timestamps and names on different servers cannot merge their histories");
        first.MixedRunContexts = true; second.Training = new(1000, 31000, 30, "", "");
        check(ObservedRunGroups.Build([first, second]).Length == 0, "Mixed run contexts and training attempts are excluded from dungeon aggregation");
    }
}
