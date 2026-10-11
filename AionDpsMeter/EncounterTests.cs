using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class EncounterTests
{
    static CombatEngine Engine(bool sustained = false)
    {
        var e = new CombatEngine();
        e.Apply(new("local-player", Actor: new(1, "You", true, true, 0, "Cleric", 1)));
        e.Apply(new("actor", Actor: new(10, "Boss", false, false, 0, "Unknown", 0) { NpcId = 100 }));
        Hit(e, 1000);
        if (sustained) Hit(e, 31000);
        return e;
    }
    static void Hit(CombatEngine e, long time, int target = 10, int actor = 1) =>
        e.Apply(new("damage", Damage: new() { Timestamp = time, ActorId = actor, TargetId = target, Damage = 100 }));
    static void Metadata(CombatEngine e, string kind, long time, object fields) =>
        e.Apply(new("network", Network: new(kind, time, "test", JsonSerializer.SerializeToElement(fields))));
    public static void Run(Action<bool, string> check, string temp)
    {
        var e = Engine(); var original = e.Snapshot();
        e.AdvanceTime(15999);
        check(e.Fight.Id == original.Id && e.Fight.Damage.Count == 1, "Idle timeout does not fire before fifteen seconds");
        e.AdvanceTime(16000);
        check(e.Fight.Id != original.Id && !e.Fight.HasEvents && original.Damage.Count == 1,
            "Idle timer clears the live meter without waiting for another packet or mutating old snapshots");
        e.Apply(new("heal", Damage: new() { ActorId = 1, TargetId = 1, Timestamp = 17000, Damage = 50 }));
        Hit(e, 18000);
        check(e.Fight.Id == original.Id && e.Fight.Damage.Count == 2 && e.Fight.Healing.Count == 1,
            "Same living target restores totals and includes healing recorded during the pause");
        var store = new FightStore(Path.Combine(temp, "recovery"));
        FightPersistence.Save(e, e.Snapshot(), store.Save);
        check(store.Files().Count() == 1 && store.Load(store.Files().Single()).Damage.Count == 2,
            "Queued idle save cannot overwrite the recovered fight with older totals");
        var healingPause = Engine(true); string healingId = healingPause.Fight.Id;
        healingPause.AdvanceTime(46000);
        healingPause.Apply(new("heal", Damage: new() { ActorId = 1, TargetId = 1, Timestamp = 50000, Damage = 50 }));
        healingPause.AdvanceTime(65000);
        check(!healingPause.Fight.HasEvents, "Healing-only pauses still reset the idle display");
        Hit(healingPause, 70000, 20);
        check(healingPause.Fight.Id == healingId && healingPause.Fight.Healing.Sum(x => x.Damage) == 50,
            "Recovery retains healing even after its own idle reset");
        var adds = Engine(true); string bossFight = adds.Fight.Id;
        adds.AdvanceTime(46000); Hit(adds, 50000, 20); Hit(adds, 51000, 21); Hit(adds, 52000);
        check(adds.Fight.Id == bossFight && adds.Fight.Damage.Count == 5 && adds.Fight.Damage.Sum(x => x.Damage) == 500,
            "Established boss encounter recovers through new adds and retains their damage when the boss returns");
        var hinted = Engine(); string hintedId = hinted.Fight.Id;
        Metadata(hinted, "boss-cast", 1200, new { CasterId = 10 });
        hinted.AdvanceTime(16000); Hit(hinted, 20000, 20);
        check(hinted.Fight.Id == hintedId, "Captured NPC activity allows short boss encounters to recover through adds");
        var incoming = Engine(true); string incomingId = incoming.Fight.Id;
        incoming.AdvanceTime(46000); Hit(incoming, 50000, 1, 20);
        check(incoming.Fight.Id == incomingId, "An add attacking the party can resume the same encounter");
        var trash = Engine(); string trashId = trash.Fight.Id;
        Hit(trash, 17000, 20);
        check(trash.Fight.Id != trashId && trash.Fight.Damage.Count == 1, "Unrelated target after an ordinary short pull starts a fresh fight");
        var active = Engine(); string activeId = active.Fight.Id;
        Hit(active, 2000, 20);
        check(active.Fight.Id == activeId && active.Fight.Damage.Count == 2, "Switching to adds during active combat never splits an encounter");
        foreach (bool duringPause in new[] { false, true }) {
            var dead = Engine(true); string deadId = dead.Fight.Id;
            if (duringPause) dead.AdvanceTime(46000);
            Metadata(dead, "entity-death", 47000, new { EntityId = 10 });
            Hit(dead, 50000);
            check(dead.Fight.Id != deadId, "Observed boss death prevents stale recovery " + (duringPause ? "during" : "before") + " the idle reset");
        }
        var replaced = Engine(true); string replacedId = replaced.Fight.Id;
        replaced.AdvanceTime(46000);
        replaced.Apply(new("actor", Actor: new(10, "Other NPC", false, false, 0, "Unknown", 0) { NpcId = 101 }));
        Hit(replaced, 50000);
        check(replaced.Fight.Id != replacedId, "Recycled entity IDs do not recover a different NPC's fight");
        var manual = Engine(true); string manualId = manual.Fight.Id;
        manual.AdvanceTime(46000); manual.EndFight("Next pull"); Hit(manual, 50000);
        check(manual.Fight.Id != manualId && manual.Fight.Damage.Count == 1, "Manual reset discards the recovery candidate");
        var scene = Engine(true); string sceneId = scene.Fight.Id;
        scene.AdvanceTime(46000); scene.Apply(new("local-player", Actor: new(2, "Other character", true, true, 0, "Cleric", 1))); Hit(scene, 50000);
        check(scene.Fight.Id != sceneId, "Character/session changes cannot recover an old encounter");
        var expired = Engine(true); string expiredId = expired.Fight.Id;
        Hit(expired, 400000);
        check(expired.Fight.Id != expiredId, "Recovery expires so an entity ID cannot join arbitrarily distant pulls");
        var delayedAdd = Engine(true); string delayedId = delayedAdd.Fight.Id;
        Hit(delayedAdd, 160000, 20);
        check(delayedAdd.Fight.Id != delayedId, "Unknown add recovery has a shorter two-minute phase window");
        var pvp = Engine(true); string pvpId = pvp.Fight.Id;
        pvp.Apply(new("actor", Actor: new(20, "Opponent", true, false, 0, "Cleric", 1))); Hit(pvp, 50000, 20);
        check(pvp.Fight.Id != pvpId, "A new PvP opponent is not treated as a boss add");
        var nearby = Engine(true); string nearbyId = nearby.Fight.Id;
        nearby.AdvanceTime(46000); Hit(nearby, 48000, 99, 98); Hit(nearby, 50000);
        check(nearby.Fight.Id == nearbyId, "Nearby unrelated combat cannot discard our party's recovery candidate");
        var quiet = Engine(); Hit(quiet, 14000, 99, 98); quiet.AdvanceTime(16000);
        check(!quiet.Fight.HasEvents, "Nearby unrelated combat cannot keep our idle meter active indefinitely");
        var clear = Engine(true); string clearId = clear.Fight.Id;
        Metadata(clear, "dungeon-result", 32000, new { Cleared = true }); Hit(clear, 50000);
        check(clear.Fight.Id != clearId, "A confirmed dungeon clear prevents recovery");
    }
}
