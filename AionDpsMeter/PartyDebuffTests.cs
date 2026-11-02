using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class PartyDebuffTests
{
    internal static void Run(Action<bool, string> check)
    {
        var fight = new Fight { LocalPlayer = 1 };
        for (int i = 1; i <= 4; i++) fight.Actors[i] = new(i, "Player " + i, true, i <= 3, 0, "Cleric", 1);
        NetworkEvent Apply(long time, int target, int slot = 1, long duration = 60000, int caster = 99, int effect = 100) => new("abnormal-apply", time, "synthetic",
            JsonSerializer.SerializeToElement(new { TargetId = target, Slot = slot, AbnormalId = effect, DurationMs = duration, CasterId = caster }));
        NetworkEvent Remove(long time, int target, int slot = 1) => new("abnormal-remove", time, "synthetic", JsonSerializer.SerializeToElement(new { TargetId = target, Slot = slot }));
        var tracker = new PartyDebuffTracker();
        fight.Network.AddRange([Apply(1000, 1), Apply(1000, 1, 2), Apply(1000, 2), Apply(1000, 4)]);
        tracker.Observe(fight, 1000, 1000);
        check(tracker.Active(1000).Length==2&&tracker.Active(1000).All(e=>e.Seconds==60),"Party health debuffs deduplicate slots per member and include remaining time");
        check(tracker.Active(61000).Length==0,"Expired debuffs disappear from party health rows without a removal packet");
        check(tracker.Poll(1000, 3, true).Length == 0, "Duplicate slots and nearby non-party players cannot satisfy the party-debuff threshold");
        fight.Network.Add(Apply(2000, 3)); tracker.Observe(fight, 2000, 2000);
        check(tracker.Poll(2000, 3, true) is [{ EffectId: 100, Name: "Stun", Members: 3 }] && tracker.Poll(2001, 3, true).Length == 0,
            "A shared party debuff alerts once when three distinct members cross the threshold");
        tracker.Observe(fight.Copy(), 3000, 3000);
        check(tracker.Poll(3000, 3, true).Length == 0, "Repeated snapshots do not repeat a party-debuff notice");
        fight.Network.Add(Remove(4000, 3)); fight.Network.Add(Apply(3000, 3)); tracker.Observe(fight, 4000, 4000);
        check(tracker.Active(4000).All(e=>e.Target!=3),"Dispel/removal immediately clears the affected member's debuff row");
        check(tracker.Poll(4000, 3, true).Length == 0, "Removal and stale replay reduce the affected-member count without re-alerting");
        fight.Network.Add(Apply(13000, 3)); tracker.Observe(fight, 13000, 13000);
        check(tracker.Poll(13000, 3, true).Length == 1, "A later new application can re-arm a cleared party-debuff threshold");
        fight.Actors[3] = fight.Actors[3] with { Party = false }; tracker.Observe(fight, 14000, 14000);
        check(tracker.Poll(14000, 3, true).Length == 0, "Members who leave the known party stop contributing to debuff alerts");
        var friendly = new PartyDebuffTracker(); var friendlyFight = fight.Copy(); friendlyFight.Network = [Apply(1000, 1, caster: 2), Apply(1000, 2, caster: 1)];
        friendly.Observe(friendlyFight, 1000, 1000);
        check(friendly.Poll(1000, 2, true).Length == 0, "Known friendly casts are excluded from party-debuff alerts");
        var shortLived = new PartyDebuffTracker(); friendlyFight.Network = [Apply(1000, 1, duration: 1000), Apply(1000, 2, duration: 1000)];
        shortLived.Observe(friendlyFight, 1500, 5000);
        check(shortLived.Poll(5500, 2, true).Length == 0, "Packet-age-adjusted debuffs expire before a late polling tick");
        fight.Actors[1] = fight.Actors[1] with { Name = "New character" }; fight.Network.Clear(); tracker.Observe(fight, 15000, 15000);
        check(tracker.RetainedSlots == 0 && tracker.Poll(15000, 2, true).Length == 0, "Character changes clear debuff state and pending alert thresholds");
        var bounded = new PartyDebuffTracker(); friendlyFight.Network = Enumerable.Range(1, 4200).Select(i => Apply(1000 + i, 1, i)).ToList();
        bounded.Observe(friendlyFight, 6000, 6000);
        check(bounded.RetainedSlots == 4096, "Party-debuff tracking caps retained slots even under pathological unique-slot traffic");
        var removalFight = new Fight { LocalPlayer = 1, Actors = new(friendlyFight.Actors) };
        var removalTracker = new PartyDebuffTracker();
        removalFight.Network.AddRange([Apply(1000, 1), Apply(1000, 2),
            new("entity-removed", 2000, "test", JsonSerializer.SerializeToElement(new { EntityId = 2 })), Apply(1500, 2, 9)]);
        removalTracker.Observe(removalFight, 3000, 3000);
        check(removalTracker.Poll(3000, 2, true).Length == 0 && removalTracker.RetainedSlots == 1,
            "Removed party members stop contributing debuffs and stale new-slot packets cannot resurrect them");
        removalFight.Network.Add(Apply(4000, 2)); removalTracker.Observe(removalFight, 4000, 4000);
        check(removalTracker.Poll(4000, 2, true).Length == 1, "Fresh effects after reappearance can contribute to party alerts again");
        removalFight.Network.Add(new("local-zone", 5000, "test", JsonSerializer.SerializeToElement(new { MapId = 1010 })));
        removalTracker.Observe(removalFight, 5000, 5000);
        check(removalTracker.Poll(5000, 2, true).Length == 0 && removalTracker.RetainedSlots == 0,
            "A map transition clears party-debuff state from the previous scene");
        var entering = new Fight { LocalPlayer = 1, Actors = new(friendlyFight.Actors) };
        entering.Network.AddRange([Apply(2000, 1, 9),
            new("local-zone", 2000, "33 36", JsonSerializer.SerializeToElement(new { MapId = 1010 })),
            Apply(2000, 1), Apply(2000, 2), Apply(1999, 1, 10)]);
        var enteringTracker = new PartyDebuffTracker(); enteringTracker.Observe(entering, 3000, 3000);
        check(enteringTracker.RetainedSlots == 2 && enteringTracker.Poll(3000, 2, true) is [{ Members: 2 }],
            "Scene entry accepts following same-timestamp party effects and rejects old-scene entries");
    }
}
