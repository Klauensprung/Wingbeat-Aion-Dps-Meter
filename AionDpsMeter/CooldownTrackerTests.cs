using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class CooldownTrackerTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var tracker = new CooldownTracker();
        var fight = new Fight { LocalPlayer = 1 };
        fight.Actors[1] = new(1, "Example", true, true, 0, "Cleric", 2301);
        NetworkEvent Cooldown(long time, int ms) => new("skill-cooldown", time, "47 38", JsonSerializer.SerializeToElement(new { SkillCode = 17100000, RemainingMs = ms }));
        var compactEngine = new CombatEngine(); compactEngine.Apply(new("local-player", Actor: fight.Actors[1]));
        compactEngine.Apply(new("network", Network: Cooldown(1000, 10000)));
        var earlyCompact = compactEngine.Snapshot();
        compactEngine.Apply(new("network", Network: Cooldown(2000, 5000)));
        var compactObserver = new CooldownTracker(); compactObserver.Observe(compactEngine.Snapshot(), 2000, 100);
        var compactNames = new SkillNames();
        check(compactObserver.Remaining(17100000, 100) == 5 && compactObserver.ReductionSeconds(17100000, 100, compactNames) == 4 &&
            compactEngine.Fight.Network.Count == 0 && earlyCompact.LiveCooldowns!.Remaining(17100000, 1000) == 10,
            "Compact cooldown snapshots retain reductions and independent earlier state without raw cooldown events");
        compactObserver.Observe(compactEngine.Snapshot(), 2000, 1100);
        check(compactObserver.Remaining(17100000, 1100) == 4, "Repeated compact snapshots do not restart monotonic cooldown deadlines");
        compactEngine.NewFight("Manual reset");
        var afterReset = new CooldownTracker(); afterReset.Observe(compactEngine.Snapshot(), 3000, 200);
        check(afterReset.Remaining(17100000, 200) == 4, "Manual fight reset preserves compact session cooldowns for a newly opened overlay");
        for (int i = 0; i < 10000; i++) {
            compactEngine.Apply(new("network", Network: new("skill-use", 4000 + i, "test", JsonSerializer.SerializeToElement(new { ActorId = 1, SkillCode = 17100000, Sequence = i % 256 }))));
            compactEngine.Apply(new("network", Network: Cooldown(4000 + i, 5000)));
        }
        check(compactEngine.Fight.Network.Count == 0 && compactEngine.Snapshot().LiveCooldowns!.RetainedCount == 2,
            "Twenty thousand skill-use and cooldown events retain only one timer and one cast timestamp");
        compactEngine.Apply(new("local-player", Actor: fight.Actors[1]));
        check(compactEngine.Snapshot().LiveCooldowns!.RetainedCount == 0, "Character re-entry clears prior compact cooldown state");
        var legacySequence = new Fight { LocalPlayer = 1 }; legacySequence.Actors[1] = fight.Actors[1];
        var legacyObserver = new CooldownTracker(); var compactSequenceObserver = new CooldownTracker();
        int[] variantsToCompare = [17100000, 17100120, 17100121]; bool sameSequence = true;
        for (int i = 0; i < 120; i++) {
            long at = 20000 + i / 2 * 100;
            int skill = variantsToCompare[i % variantsToCompare.Length];
            var e = i % 4 == 0
                ? new NetworkEvent("skill-use", at, "test", JsonSerializer.SerializeToElement(new { ActorId = 1, SkillCode = skill }))
                : new NetworkEvent("skill-cooldown", at, "test", JsonSerializer.SerializeToElement(new { SkillCode = skill, RemainingMs = i % 7 == 0 ? 0 : 10000 - i * 25 }));
            legacySequence.Network.Add(e); compactEngine.Apply(new("network", Network: e));
            legacyObserver.Observe(legacySequence, at, at - 19000);
            compactSequenceObserver.Observe(compactEngine.Snapshot(), at, at - 19000);
            foreach (int id in variantsToCompare) sameSequence &= legacyObserver.Reading(id, at - 19000, compactNames, null) == compactSequenceObserver.Reading(id, at - 19000, compactNames, null) &&
                legacyObserver.ReductionSeconds(id, at - 19000, compactNames) == compactSequenceObserver.ReductionSeconds(id, at - 19000, compactNames);
        }
        check(sameSequence, "Compact live cooldowns match legacy history at every mixed cast, tier, reset and same-timestamp checkpoint");
        var crowded = new CooldownTracker();
        for (int id = 1; id <= 5000; id++) {
            crowded.Capture(new("skill-use", id, "test", JsonSerializer.SerializeToElement(new { ActorId = 1, SkillCode = id })), 1);
            crowded.Capture(new("skill-cooldown", id, "test", JsonSerializer.SerializeToElement(new { SkillCode = id, RemainingMs = 60000 })), 1);
        }
        var crowdedSnapshot = crowded.Copy();
        var crowdedFight = new Fight { LocalPlayer = 1, LiveCooldowns = crowdedSnapshot }; crowdedFight.Actors[1] = fight.Actors[1];
        var crowdedObserver = new CooldownTracker(); crowdedObserver.Observe(crowdedFight, 5000, 100);
        check(crowded.RetainedCount == 8192 && crowdedObserver.RetainedCount == 8192 && crowdedObserver.Remaining(1, 100) == null &&
            crowdedObserver.Remaining(5000, 100) == 60,
            "Distinct cooldown and cast dictionaries remain capped and an observer receives the newest retained skills");
        crowded.Reset();
        check(crowded.RetainedCount == 0 && crowdedSnapshot.RetainedCount == 8192,
            "Resetting bounded live cooldown state leaves an earlier detached snapshot intact");
        check(tracker.Remaining(17100000, 1000) == null, "Unobserved cooldown is unknown, not ready");
        fight.Network.Add(Cooldown(10000, 5000)); tracker.Observe(fight, 11000, 1000);
        check(tracker.Remaining(17100000, 1000) == 4 && tracker.Remaining(17100000, 2500) == 2.5 && tracker.Remaining(17100000, 6000) == 0,
            "Cooldown countdown subtracts packet age and expires using a monotonic clock");
        tracker.Observe(fight.Copy(), 12000, 2000);
        check(tracker.Remaining(17100000, 2000) == 3, "Repeated snapshots cannot restart a cooldown");
        var next = new Fight { LocalPlayer = 1, Actors = new(fight.Actors) };
        tracker.Observe(next, 12000, 2000);
        check(tracker.Remaining(17100000, 2000) == 3, "Fight rollover retains active cooldowns");
        next.Network.Add(Cooldown(10000, 5000)); tracker.Observe(next, 12000, 2000);
        check(tracker.Remaining(17100000, 2000) == 3, "Recovered fight events do not extend cooldowns");
        next.Network.Add(Cooldown(12000, 2000)); tracker.Observe(next, 12000, 2000);
        check(tracker.Remaining(17100000, 2000) == 2, "New server cooldown values correct the countdown");
        next.Network.Add(Cooldown(12000, 0)); tracker.Observe(next, 12000, 2000);
        check(tracker.Remaining(17100000, 2000) == 0, "A server reset clears the cooldown, including events sharing a timestamp");
        next.Network.Add(Cooldown(11000, 5000)); next.Network.Add(Cooldown(13000, 5000));
        next.Network.Add(new("skill-cooldown", 12000, "47 38", JsonSerializer.SerializeToElement(new { SkillCode = "bad", RemainingMs = 5000 })));
        tracker.Observe(next, 12000, 2000);
        check(tracker.Remaining(17100000, 2000) == 0, "Out-of-order, future and malformed cooldowns cannot replace valid state");
        next.Actors[1] = next.Actors[1] with { Name = "Other" }; next.Network.Clear(); tracker.Observe(next, 13000, 3000);
        check(tracker.Remaining(17100000, 3000) == null, "Changing characters clears cooldown state");
        next.Network.Add(Cooldown(14000, 2000)); tracker.Observe(next, 14000, 4000); tracker.Reset();
        check(tracker.ObservedSkills.Length == 0, "Starting a new capture clears previous cooldown readings");
        string catalog = System.IO.Path.Combine(temp, "cooldown-name-groups.json");
        System.IO.File.WriteAllText(catalog, """{"17100000":"Healing Light","17100120":"Healing Light","17200000":"Other skill"}""");
        var names = new SkillNames(catalog);
        var shortened = new CooldownTracker(); var reductionFight = new Fight { LocalPlayer = 1 };
        reductionFight.Network.Add(Cooldown(10000, 30000)); shortened.Observe(reductionFight, 10000, 1000);
        reductionFight.Network.Add(Cooldown(15000, 25000)); shortened.Observe(reductionFight, 17000, 8000);
        check(shortened.ReductionSeconds(17100000, 8000, names) == null,
            "Natural countdown and delayed packet delivery do not create cooldown-reduction markers");
        reductionFight.Network.Add(Cooldown(18000, 10000)); shortened.Observe(reductionFight, 20000, 11000);
        check(shortened.ReductionSeconds(17100000, 11000, names) == 12 && shortened.Remaining(17100000, 11000, names) == 8,
            "A shorter server deadline reports the measured reduction without changing the age-corrected timer");
        shortened.Observe(reductionFight.Copy(), 21000, 12000);
        check(shortened.ReductionSeconds(17100000, 12000, names) == 12 && shortened.ReductionSeconds(17100000, 19000, names) == null,
            "Repeated snapshots preserve reduction evidence and the marker expires with its timer");
        reductionFight.Network.Add(new("skill-use", 19000, "02 38", JsonSerializer.SerializeToElement(new { ActorId = 1, SkillCode = 17100120 })));
        shortened.Observe(reductionFight, 21000, 12000);
        check(shortened.ReductionSeconds(17100000, 12000, names) == null,
            "A newer cast in the same skill family suppresses an ambiguous reduction marker");
        shortened.Reset();
        check(shortened.ReductionSeconds(17100000, 12000, names) == null, "Capture reset clears all cooldown-reduction evidence");
        var grouped = CooldownSkillChoice.Group(new[] { 17100000, 17100120, 17100999, 17200000, 99, 100 }, names);
        check(grouped.Length == 4 && grouped[0].Id == 17100000, "Cooldown selection merges equal names and family variants while keeping distinct unknown IDs separate");
        var variants = new Fight();
        void Variant(int id, long time, int ms) => variants.Network.Add(new("skill-cooldown", time, "47 38", JsonSerializer.SerializeToElement(new { SkillCode = id, RemainingMs = ms })));
        Variant(17100120, 20000, 5000); tracker.Observe(variants, 20000, 5000);
        check(tracker.Remaining(17100000, 5000, names) == 5 && tracker.Remaining(17200000, 5000, names) == null,
            "A named cooldown follows reports from another ID without affecting other skills");
        Variant(17100999, 21000, 2000); tracker.Observe(variants, 21000, 6000);
        check(tracker.Remaining(17100000, 6000, names) == 2, "New family-variant cooldown reports replace older variant readings");
        Variant(17100000, 21000, 0); tracker.Observe(variants, 21000, 6000);
        check(tracker.Remaining(17100120, 6000, names) == 0, "A ready/reset report from any matching ID clears the grouped cooldown");
        tracker.Reset();
        var castFight = new Fight { LocalPlayer = 1 };
        void Cast(int actor, long time) => castFight.Network.Add(new("skill-use", time, "02 38", JsonSerializer.SerializeToElement(new { ActorId = actor, SkillCode = 17100120, Sequence = 1 })));
        var duration = new Dictionary<int, double> { [17100000] = 30 };
        Cast(2, 30000); tracker.Observe(castFight, 30000, 10000);
        check(tracker.Reading(17100000, 10000, names, duration).Seconds == null, "Other players and summons cannot start a local cooldown estimate");
        Cast(1, 30000); tracker.Observe(castFight, 31000, 11000);
        check(tracker.Reading(17100000, 11000, names, duration) == (29d, true), "Estimates start from a matching local cast and account for packet age");
        castFight.Damage.Add(new() { ActorId = 1, SkillCode = 17100120, Timestamp = 32000, Damage = 100 }); tracker.Observe(castFight, 32000, 12000);
        check(tracker.Reading(17100000, 12000, names, duration) == (28d, true), "Repeated damage cannot restart a cast estimate");
        castFight.Network.Add(Cooldown(33000, 5000)); tracker.Observe(castFight, 33000, 13000);
        check(tracker.Reading(17100000, 13000, names, duration) == (5d, false), "Server cooldown reports override cast estimates");
        Cast(1, 40000); tracker.Observe(castFight, 40000, 20000);
        check(tracker.Reading(17100000, 20000, names, duration) == (30d, true), "A later cast starts an estimate after old server data");
        check(CooldownDurations.Seconds(17150000, names, null) == 30 && CooldownDurations.Seconds(17060000, names, null) == 45,
            "Documented base estimates remain separate from captured cooldown values");
        CheckChargeSkills(check, temp);
        var styled = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(new Preferences { CooldownStyle = "Round", CooldownReadyGlow = false }))!;
        check(styled.CooldownStyle == "Round" && !styled.CooldownReadyGlow && JsonSerializer.Deserialize<Preferences>("{}")!.CooldownStyle == "Square" &&
            CooldownPresentation.Style("unsupported") == "Square" && CooldownPresentation.Style("Bars") == "Bars",
            "Cooldown layouts and glow persist, with safe square defaults for older or unknown preferences");
        check(CooldownPresentation.RemainingFraction(15, 30) == .5 && CooldownPresentation.RemainingFraction(null, 30) == 0 &&
            CooldownPresentation.RemainingFraction(0, 30) == 0 && CooldownPresentation.RemainingFraction(90, 30) == 1 &&
            CooldownPresentation.RemainingFraction(10, 0) == 0 && CooldownPresentation.RemainingFraction(double.NaN, 30) == 0,
            "Cooldown bars show bounded remaining progress and never manufacture progress for unknown readings");
        var preferences = new Preferences { CooldownSkills = [17100000, 17010000], CooldownLocked = true, CooldownShowReady = false, CooldownOneSecondEarly = false, CooldownLeft = 20, CooldownTop = 40 };
        var loaded = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(preferences))!;
        check(loaded.CooldownSkills.SequenceEqual(preferences.CooldownSkills) && loaded.CooldownLocked && !loaded.CooldownShowReady && loaded.CooldownLeft == 20 &&
            JsonSerializer.Deserialize<Preferences>("{}")!.CooldownSkills.Length == 0, "Cooldown choices, position and options persist; old settings remain compatible");
        check(new Preferences().CooldownOneSecondEarly && JsonSerializer.Deserialize<Preferences>("{}")!.CooldownOneSecondEarly && !loaded.CooldownOneSecondEarly,
            "One-second display advance defaults on for new and existing settings, and opting out survives restart");
        var characterSettings = new Preferences { CooldownSkills = [17100000], CooldownFallbackSeconds = new() { [17100000] = 30 } };
        var cleric = new Actor(1, "First", true, true, 0, "Cleric", 1001) { PersistentId = 1234567890123 };
        var chanter = new Actor(2, "Second", true, true, 0, "Chanter", 1001) { PersistentId = 1234567890124 };
        check(!CooldownCharacterPresets.Activate(characterSettings, cleric with { PersistentId = 0 }) && characterSettings.CooldownCharacterKey == null,
            "Cooldown presets wait for confirmed persistent identity rather than transient entity IDs");
        check(CooldownCharacterPresets.Activate(characterSettings, cleric) && characterSettings.CooldownSkills.SequenceEqual(new[] { 17100000 }),
            "First character inherits legacy cooldown selection");
        check(CooldownCharacterPresets.Activate(characterSettings, chanter) && characterSettings.CooldownSkills.Length == 0 && characterSettings.CooldownFallbackSeconds.Count == 0,
            "Another character starts with independent skills and duration estimates");
        characterSettings.CooldownSkills = [18030000]; characterSettings.CooldownFallbackSeconds[18030000] = 45;
        CooldownCharacterPresets.Save(characterSettings);
        characterSettings = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(characterSettings))!;
        check(CooldownCharacterPresets.Activate(characterSettings, cleric with { Id = 900 }) && characterSettings.CooldownSkills.SequenceEqual(new[] { 17100000 }) && characterSettings.CooldownFallbackSeconds[17100000] == 30,
            "Character cooldown preset survives restart and changed live entity ID");
        characterSettings.CooldownSkills = []; characterSettings.CooldownFallbackSeconds.Clear();
        CooldownCharacterPresets.Activate(characterSettings, chanter);
        check(characterSettings.CooldownSkills.SequenceEqual(new[] { 18030000 }) && characterSettings.CooldownFallbackSeconds[18030000] == 45,
            "Switching characters restores saved selections without shared collection mutations");
        CooldownCharacterPresets.Activate(characterSettings, cleric);
        check(characterSettings.CooldownSkills.Length == 0 && characterSettings.CooldownFallbackSeconds.Count == 0,
            "An intentionally empty character preset remains empty");
        check(CooldownCharacterPresets.Key(cleric) != CooldownCharacterPresets.Key(cleric with { Server = 1002 }) &&
            !CooldownCharacterPresets.Activate(characterSettings, cleric with { Id = 999 }),
            "Character presets distinguish servers and do not reload on ordinary live updates");
    }

    static void CheckChargeSkills(Action<bool, string> check, string temp)
    {
        var names = new SkillNames(System.IO.Path.Combine(temp, "no-local-overrides.json"), true);
        (int Base, int[] Tiers)[] charged = [
            (11100000, [11100001, 11100002]), (11360000, [11360031]),
            (12090000, [12090001, 12090002, 12090003]), (12430000, [12430031]),
            (14010000, [14010001, 14010002, 14010003]), (15060000, [15060001, 15060002, 15060003]),
            (16240000, [16240011, 16240012, 16240013]), (16300000, [16300041, 16300042, 16300043]),
            (17060000, [17060001, 17060002, 17060003, 17060340, 17060341, 17060342, 17060343]),
            (17120000, [17120001]), (18090000, [18090021, 18090022, 18090023]),
            (19150000, [19150001, 19150002, 19150003]), (19160000, [19160001, 19160002, 19160003]),
            (19190000, [19190001, 19190002, 19190003]), (19200000, [19200001, 19200002, 19200003])
        ];
        foreach (var (parent, tiers) in charged) {
            var group = CooldownSkillChoice.Group(tiers.Reverse().Append(parent), names);
            check(group.Length == 1 && group[0].Id == parent && group[0].Name == names.GetSkillName(parent),
                $"Charge tiers collapse to one base choice: {names.GetSkillName(parent)}");
            var tracker = new CooldownTracker(); var fight = new Fight { LocalPlayer = 1 };
            long time = 10000; bool reportsMatch = true;
            foreach (int id in tiers) {
                fight.Network.Add(new("skill-cooldown", time, "47 38", JsonSerializer.SerializeToElement(new { SkillCode = id, RemainingMs = 10000 })));
                tracker.Observe(fight, time + 2000, time + 30000);
                reportsMatch &= tracker.Reading(parent, time + 30000, names, null) == (8d, false);
                time += 3000;
            }
            fight.Network.Add(new("skill-cooldown", time, "47 38", JsonSerializer.SerializeToElement(new { SkillCode = parent, RemainingMs = 0 })));
            tracker.Observe(fight, time, time + 28000);
            check(reportsMatch && tiers.All(id => tracker.Remaining(id, time + 28000, names) == 0),
                $"Charge reports share packet-age correction and ready resets: {names.GetSkillName(parent)}");
            tracker.Reset(); fight.Network.Clear();
            var overrides = new Dictionary<int, double> { [tiers[0]] = 44 };
            fight.Network.Add(new("skill-use", 10000, "02 38", JsonSerializer.SerializeToElement(new { ActorId = 1, SkillCode = tiers[^1], Sequence = 1 })));
            tracker.Observe(fight, 12000, 30000);
            check(tracker.Reading(parent, 30000, names, overrides) == (42d, true) &&
                tiers.All(id => tracker.Reading(id, 30000, names, overrides) == (42d, true)),
                $"Charge casts share saved tier durations and subtract two seconds of decode delay: {names.GetSkillName(parent)}");
        }
        check(CooldownSkillChoice.Group([17060000, 18030000, 1601800, 1601810, 15060000, 15060026, 19150003, 19160003, 99, 100], names).Length == 10,
            "Similar names, NPCs, secondary effects, stance families and unknown IDs keep separate cooldowns");
        check(CooldownSkillChoice.Search([17060343], names, "Bolt").Select(c => c.Id).SequenceEqual(new[] { 17060000, 18030000 }) &&
            CooldownSkillChoice.Search([], names, "Bolt MAX") is [{ Id: 17060000 }] &&
            CooldownSkillChoice.Search([], names, "17060343") is [{ Id: 17060000 }],
            "Searching a base name, charge alias or tier ID returns the grouped skill");
        check(names.GetSkillName(17060343) == "Bolt MAX" && names.Resolve(17060343).MatchedCode == 17060343,
            "Cooldown grouping preserves exact skill names and IDs for combat analysis");
        var durations = new Dictionary<int, double> { [17060343] = 44, [17060001] = 42, [17060000] = 40, [18030000] = 12 };
        check(CooldownDurations.Seconds(17060343, names, durations) == 40 && CooldownDurations.Seconds(17060000, names, durations) == 40,
            "Conflicting old tier durations consistently prefer the base override");
        CooldownDurations.Set(17060002, 0, names, durations);
        check(durations.Count == 2 && durations[18030000] == 12 && durations[17060000] == 0 &&
            CooldownDurations.Seconds(17060343, names, durations) == 0 && CooldownDurations.Seconds(17060343, names, null) == 45 &&
            CooldownDurations.Seconds(1601800, names, null) == 0,
            "Editing a tier replaces only its family overrides, and zero disables every tier estimate");
        string path = System.IO.Path.Combine(temp, "charge-aliases.json");
        System.IO.File.WriteAllText(path, """{"17060000":"Test","17060001":"Test 1","17060002":"Test - Lv. 2","17060003":"Test Lvl.3","17060004":"Test MAX","17060005":"Test Crush","17060006":"Test 2 hits","17060007":"Test Maximum","17500001":"Orphan Level 1","17500002":"Orphan Level 2"}""");
        var custom = new SkillNames(path);
        check(CooldownSkillChoice.Group([17060004, 17060003, 17060002, 17060001, 17060000], custom) is [{ Id: 17060000, Name: "Test" }] &&
            CooldownSkillChoice.Group([17060000, 17060005, 17060006, 17060007, 17500001, 17500002], custom).Length == 6,
            "Charge grouping accepts numeric and level suffixes only with a matching catalogue parent");
        check(CooldownSkillChoice.Resolve(17060003, names).Name == "Bolt" && CooldownSkillChoice.Resolve(17060003, custom).Name == "Test",
            "Importing a different name catalogue cannot reuse stale cooldown associations");
        var later = new CooldownTracker(); var casts = new Fight { LocalPlayer = 1 };
        void Cast(long timestamp, int id, int sequence) => casts.Network.Add(new("skill-use", timestamp, "02 38", JsonSerializer.SerializeToElement(new { ActorId = 1, SkillCode = id, Sequence = sequence })));
        Cast(10000, 17060340, 1); later.Observe(casts, 12000, 30000);
        Cast(56854, 17060343, 2); later.Observe(casts, 58854, 76854);
        check(later.Reading(17060000, 76854, names, null) == (43d, true), "A later MAX cast restarts the shared Bolt timer from its packet timestamp");
        later.Observe(casts.Copy(), 60854, 78854);
        check(later.Reading(17060002, 78854, names, null) == (41d, true), "Refreshing the same charge cast cannot restart its timer");
    }
}
