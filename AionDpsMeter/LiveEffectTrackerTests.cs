using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class LiveEffectTrackerTests
{
    internal static void Run(Action<bool, string> check)
    {
        var fight = FeatureTests.Sample(); var tracker = new LiveEffectTracker();
        NetworkEvent Apply(long time, int target, int slot, long duration, string kind = "abnormal-apply") => new(kind, time, "test",
            JsonSerializer.SerializeToElement(new { TargetId = target, Slot = slot, DurationMs = duration, AbnormalId = 10, SourceSkillId = 17010001, CasterId = 2 }));
        fight.Network.Add(Apply(10000, 1, 1, 5000)); fight.Network.Add(Apply(10000, 2, 2, 5000));
        tracker.Observe(fight, 12000, 1000);
        check(tracker.Active(1000) is [{ Slot: 1, RemainingSeconds: 3 }] && tracker.Active(4000).Length == 0,
            "Live effects track only the local recipient, subtract packet age and expire on a monotonic clock");
        tracker.Observe(fight.Copy(), 13000, 2000);
        check(tracker.Active(2000).Single().RemainingSeconds == 2, "Repeated snapshots do not restart live effect countdowns");
        fight.Network.Add(Apply(13000, 1, 1, 6000, "abnormal-refresh")); tracker.Observe(fight, 13000, 2000);
        check(tracker.Active(2000).Single().RemainingSeconds == 6, "Effect refresh replaces the slot's captured duration");
        var rollover = new Fight { LocalPlayer = 1, Actors = new(fight.Actors) };
        tracker.Observe(rollover, 14000, 3000);
        check(tracker.Active(3000).Single().RemainingSeconds == 5, "Fight rollover preserves still-active effects");
        rollover.Network.Add(new("abnormal-remove", 14000, "test", JsonSerializer.SerializeToElement(new { TargetId = 1, Slot = 1 })));
        rollover.Network.Add(Apply(13000, 1, 1, 6000)); rollover.Network.Add(Apply(14000, 1, 1, 6000));
        tracker.Observe(rollover, 14000, 3000);
        check(tracker.Active(3000).Length == 0, "Captured removal wins against stale and same-timestamp effect replay");
        rollover.Network.Add(Apply(15000, 1, 3, uint.MaxValue)); tracker.Observe(rollover, 15000, 4000);
        check(tracker.Active(100000).Single().RemainingSeconds == null, "Unknown effect duration remains explicit rather than inventing an expiry");
        rollover.Actors[1] = rollover.Actors[1] with { Name = "Different character" }; rollover.Network.Clear();
        tracker.Observe(rollover, 16000, 5000);
        check(tracker.Active(5000).Length == 0, "Character changes clear previous live effects");
        check(!new Preferences().CooldownShowEffects && JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(new Preferences { CooldownShowEffects = true }))!.CooldownShowEffects,
            "Live effect display is optional and its preference persists");
        var persistentFight = FeatureTests.Sample(); var persistentTracker = new LiveEffectTracker();
        persistentFight.Network.Add(new("abnormal-apply", 1000, "synthetic", JsonSerializer.SerializeToElement(new {
            TargetId = persistentFight.LocalPlayer, Slot = 1, AbnormalId = 10, DurationMs = uint.MaxValue, UntilRemoved = true })));
        persistentTracker.Observe(persistentFight, 2000, 1000);
        check(persistentTracker.Active(100000) is [{ UntilRemoved: true, RemainingSeconds: null }],
            "A captured persistent effect remains active with an explicit until-removed state rather than a fabricated countdown");
        persistentFight.Network.Add(new("abnormal-remove", 3000, "synthetic", JsonSerializer.SerializeToElement(new { TargetId = persistentFight.LocalPlayer, Slot = 1 })));
        persistentTracker.Observe(persistentFight, 3000, 2000);
        check(persistentTracker.Active(2000).Length == 0, "An explicit removal still ends a persistent effect immediately");
        var leaving = FeatureTests.Sample(); leaving.Network.Clear(); var leavingTracker = new LiveEffectTracker();
        leaving.Network.Add(Apply(1000, 1, 1, 60000));
        leaving.Network.Add(new("entity-removed", 2000, "test", JsonSerializer.SerializeToElement(new { EntityId = 1 })));
        leaving.Network.Add(Apply(1500, 1, 2, 60000));
        leavingTracker.Observe(leaving, 3000, 3000);
        check(leavingTracker.Active(3000).Length == 0, "Local removal clears active effects and rejects stale updates even for previously unseen slots");
        leaving.Network.Add(Apply(4000, 1, 3, 60000));
        leaving.Network.Add(new("entity-removed", 3500, "test", JsonSerializer.SerializeToElement(new { EntityId = 1 })));
        leavingTracker.Observe(leaving, 5000, 5000);
        check(leavingTracker.Active(5000) is [{ Slot: 3 }], "A delayed disappearance preserves newer local effects");
        var entering = FeatureTests.Sample(); entering.Network.Clear(); var enteringTracker = new LiveEffectTracker();
        entering.Network.AddRange([Apply(2000, 1, 1, 60000),
            new("local-zone", 2000, "33 36", JsonSerializer.SerializeToElement(new { MapId = 1010 })),
            Apply(2000, 1, 2, 60000), Apply(1999, 1, 3, 60000)]);
        enteringTracker.Observe(entering, 3000, 3000);
        check(enteringTracker.Active(3000) is [{ Slot: 2, RemainingSeconds: 59 }],
            "Scene entry preserves following same-timestamp spawn effects while clearing preceding and older effects");
        check(BuffTracking.Rows([10], enteringTracker.Active(3000), true) is [{ Time: "59s", Missing: false }],
            "Tracked buff uptime uses effect lifetime independently of cooldown display lead");
        check(BuffTracking.Rows([10], enteringTracker.Active(63000), true) is [{ Time: "Check buff", Missing: true }] &&
            BuffTracking.Rows([10], [], false) is [{ Time: "Start capture", Missing: false }],
            "Expired buffs remain visible for rebuff checks while stopped capture does not claim missing buffs");
        check(BuffTracking.Rows([10], [new(1,10,0,1,5),new(2,10,0,2,20)], true).Single().Time == "20s" &&
            BuffTracking.Rows([10], [new(1,10,0,1,null,true)], true).Single().Time == "Active",
            "Duplicate buff sources use the longest active duration and indefinite effects have no invented countdown");
        var presets = new Preferences { CooldownBuffs = [10] };
        var owner = new Actor(1,"Buff owner",true,true,0,"Cleric",1) { PersistentId=10 };
        CooldownCharacterPresets.Activate(presets,owner);
        CooldownCharacterPresets.Activate(presets,owner with { PersistentId=11 });
        check(presets.CooldownBuffs.Length == 0,"New character does not inherit another character's tracked buffs");
        presets = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(presets))!;
        CooldownCharacterPresets.Activate(presets,owner);
        check(presets.CooldownBuffs.SequenceEqual(new[]{10}),"Character buff selections survive save, restart and character switching");
    }
}
