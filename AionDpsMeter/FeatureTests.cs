using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class FeatureTests
{
    internal static Fight Sample(int index = 0)
    {
        var f = new Fight { Label = "Synthetic comparison encounter", LocalPlayer = 1, Created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(index) };
        for (int i = 1; i <= 10; i++) f.Actors[i] = new(i, i == 1 ? "Example cleric" : $"Member {i}", true, true, 15500, i == 1 ? "Cleric" : "Ranger", 2301);
        f.Actors[100] = new(100, "Test target", false, false, 0, "Unknown", 0) { NpcId = 2000001 };
        f.Damage.Add(new() { ActorId = 1, TargetId = 100, SkillCode = 17010001, Timestamp = 1000, Damage = 1000 + index * 20, IsCritical = index % 2 == 0 });
        f.Damage.Add(new() { ActorId = 1, TargetId = 100, SkillCode = 17020001, Timestamp = 31000, Damage = 1000 + index * 10 });
        f.Healing.Add(new() { ActorId = 1, TargetId = 1, SkillCode = 17100001, Timestamp = 3000, Damage = 250, IsHeal = true });
        f.Healing.Add(new() { ActorId = 1, TargetId = 2, SkillCode = 17100001, Timestamp = 6000, Damage = 750, IsHeal = true });
        f.Healing.Add(new() { ActorId = 2, TargetId = 1, SkillCode = 18010001, Timestamp = 6000, Damage = 100, IsHeal = true });
        FightMetadata.SetRoster(f, new(Enumerable.Range(1, 10).ToArray(), "Synthetic fixture"));
        FightMetadata.SetBuild(f, new(1, [new(17010000, 10, [1, 3]), new(17020000, 9, [2]), new(17030000, 7, [])], true, "Synthetic fixture") {
            Equipment = new() { ["Weapon"] = index % 2 == 0 ? "Example staff" : "Example mace" }
        });
        return f;
    }
    public static void Run(Action<bool, string> check, string temp)
    {
        var f = Sample(); var totals = Analysis.Totals(f); var local = totals.Single(r => r.Id == 1);
        check(totals.Count(r => r.Role == "Party") == 10 && totals.Single(r => r.Id == 10).Damage == 0,
            "Full party totals include every zero-damage member and heal-only members");
        var solo = f.Copy(); FightMetadata.SetRoster(solo, new([], "Explicit solo"));
        check(MeterPresentation.Build(solo, Analysis.Totals(solo)).Rows.Select(r => r.Id).SequenceEqual(new[] { 1 }) && !solo.Actors[2].Party,
            "Explicit solo roster excludes nearby named players and clears previous membership");
        f.Actors[11] = new(11, "Eleventh", true, false, 0, "Unknown", 2301);
        bool tooMany = false; try { FightMetadata.SetRoster(f, new(Enumerable.Range(2, 10).ToArray())); } catch (InvalidDataException) { tooMany = true; }
        check(tooMany && f.Actors.Values.Count(a => a.Party) == 10, "Ten-player limit includes the local character and rejects an invalid roster atomically");
        var healing = DetailedAnalysis.Healing(f, id => id.ToString());
        check(healing.Where(r => r.Healer == "Example cleric").Sum(r => r.Healing) == 1000 && healing.Single(r => r.Target == "Member 2").HealerShare == 75,
            "Healing groups retain healer, skill and target with correct shares and no double counting");
        var marks = Analysis.Timeline(f, [1, 2], true);
        check(marks.Count(e => e.Healing) == 3 && marks.Count(e => !e.Healing) == 2 && marks.Single(e => e.ActorId == 2).Healing,
            "Party timelines include healing-only characters and distinguish recovery from damage");
        var oldHealing = f.Copy(); oldHealing.Healing[0] = oldHealing.Healing[0] with { IsHeal = false };
        check(Analysis.Timeline(oldHealing, [1, 2], true).Count(e => e.Healing) == 3, "Timeline uses the healing collection even when a legacy event lacks its healing flag");
        var openers = DetailedAnalysis.Openers(f, id => id.ToString());
        check(openers.Where(r => r.ActorId == 1).Select(r => r.Seconds).SequenceEqual(new[] { 0d, 2d, 5d }) && openers.Single(r => r.ActorId == 2).Seconds == 0,
            "Openers use each character's first action, keep healing and exclude actions after fifteen seconds");
        var specs = DetailedAnalysis.Specs(f, id => id.ToString());
        check(specs.Single(s => s.Character == "Example cleric" && s.SkillCode == 17030000) is { Level: 7, RecordedEvents: 0 } &&
            specs.Single(s => s.Character == "Example cleric" && s.SkillCode == 17100000).Level == null,
            "Full specs include unused configured skills while observed-only skill levels remain unknown");
        var variantSpec = f.Copy(); variantSpec.Builds[1].Skills[0] = new(17010001, 10, [1, 3]);
        check(DetailedAnalysis.Specs(variantSpec, id => id.ToString()).Single(s => s.Character == "Example cleric" && DetailedAnalysis.Family(s.SkillCode) == 17010000) is { Level: 10, RecordedEvents: 1 },
            "Configured skill variants join their observed family without a duplicate unknown row");
        var store = new FightStore(Path.Combine(temp, "feature-fights")); store.Save(f); var loaded = store.Load(store.Files().Single());
        check(loaded.Builds[1].Skills[0].Choices.SequenceEqual(new[] { 1, 3 }) && loaded.Builds[1].Equipment["Weapon"] == "Example staff" && loaded.PartyKnown,
            "Per-character loadouts, equipment, roster and provenance survive saved-fight round trip");
        var copied = f.Copy(); copied.Builds[1].Skills[0].Choices[0] = 999;
        check(f.Builds[1].Skills[0].Choices[0] == 1, "Background snapshots do not share mutable loadout choices");
        var legacy = new Fight { LocalPlayer = 1, Actors = new(f.Actors), Specs = new() { [17010000] = new(17010000, 5, [2]) } };
        check(FightMetadata.BuildFor(legacy, 1)?.Skills.Single().Level == 5 && FightMetadata.BuildFor(legacy, 2) == null,
            "Legacy local-player specs remain readable without assigning them to party members");
        var oldJson = JsonSerializer.SerializeToNode(legacy)!; oldJson.AsObject().Remove("Builds"); oldJson.AsObject().Remove("PartyKnown"); oldJson.AsObject().Remove("PartySource");
        var oldPath = Path.Combine(temp, "version-one.json"); File.WriteAllText(oldPath, oldJson.ToJsonString());
        check(store.Load(oldPath).Builds.Count == 0, "Previous version-one archives load without the new optional fields");
        var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: f.Actors[1]));
        engine.Apply(new("build", Build: f.Builds[1])); var stale = engine.Snapshot(); engine.NewFight("Another");
        bool staleRejected = false; try { engine.SetMetadata(stale); } catch (InvalidOperationException) { staleRejected = true; }
        check(staleRejected && engine.Snapshot().Builds[1].Skills.Length == 3, "An editor cannot apply stale metadata across fight boundaries");
        engine.Apply(new("actor", Actor: f.Actors[1] with { Name = "Replacement" }));
        check(!engine.Snapshot().Builds.ContainsKey(1), "A reused player entity ID does not inherit the previous player's loadout");
        var eventPath = Path.Combine(temp, "loadout-events.jsonl");
        File.WriteAllLines(eventPath, [JsonSerializer.Serialize(new DecodedCombatEvent("actor", Actor: f.Actors[1])),
            JsonSerializer.Serialize(new DecodedCombatEvent("party", Party: new([1]))), JsonSerializer.Serialize(new DecodedCombatEvent("build", Build: f.Builds[1]))]);
        var imported = new CombatEngine(); foreach (var item in CombatEventFile.Read(eventPath)) imported.Apply(item);
        check(imported.Snapshot().Builds[1].Complete && imported.Snapshot().Actors[1].Party, "Normalized imports carry party and complete character loadouts");
        File.WriteAllText(eventPath, JsonSerializer.Serialize(new DecodedCombatEvent("build", Build: new(1, [new(42, -1, [])]))));
        bool invalid = false; try { CombatEventFile.Read(eventPath); } catch (InvalidDataException) { invalid = true; }
        check(invalid, "Malformed loadouts are rejected before importing any records");
        var entries = f.Actors.Values.Where(a => a.NpcId == 0).Select(a => new CharacterEditor.CharacterEntry {
            Id = a.Id, Character = a.Name, Player = a.Player, Party = a.Party, Class = a.Job, CP = a.Cp + 100, Server = a.Server
        }).ToArray();
        var edited = CharacterEditor.Apply(f, entries, [], [], true);
        check(edited.Actors[1] is { Cp: 15600, ProfileSource: "Entered locally" } && f.Actors[1].Cp == 15500 && edited.Builds.Count == 0,
            "Character editor saves explicit metadata without modifying the original fight or resurrecting removed loadouts");
        entries[0].CompleteLoadout = true;
        var unchanged = CharacterEditor.Apply(f, entries, f.Builds[1].Skills.Select(s => new CharacterEditor.SkillEntry { ActorId = 1, SkillId = s.Skill, Level = s.Level, Choices = string.Join(",", s.Choices) }),
            [new CharacterEditor.GearEntry { ActorId = 1, Slot = "Weapon", Item = "Example staff" }], true);
        check(unchanged.Builds[1].Source == "Synthetic fixture" && unchanged.PartySource == "Synthetic fixture", "Editing CP preserves the provenance of unchanged loadouts and party membership");

        var peers = Enumerable.Range(1, 100).Select(Sample).ToArray();
        check(Benchmarks.Peers(f, local, peers).Length == 100 && Benchmarks.Peers(f, local, peers.Concat(peers)).Length == 100,
            "CP comparison uses matching character observations and deduplicates repeated fight files");
        check(Benchmarks.Peers(f, totals.Single(r => r.Id == 10), peers).Length == 0 && Benchmarks.Trends(peers).All(r => r.Character != "Member 10"),
            "Idle roster entries do not fabricate performance observations");
        var report = Benchmarks.Report(f, peers).Single(r => r.Character == "Example cleric");
        check(report is { LocalSamples: 100, LocalPercentile: 0, TopFivePercentDPS: not null, MedianDPS: not null, TopQuarterDPS: not null },
            "Report card supplies percentile, median, top quarter and top five percent for sufficient samples");
        check(Benchmarks.Report(f, peers.Take(19)).Single(r => r.Character == "Example cleric").LocalPercentile == null &&
            Benchmarks.Differences(f, local, peers.Take(99)).All(r => r.Tier != "Top 5% (local)"), "Comparison sample thresholds never fabricate ranks or top-five-percent summaries");
        var mismatch = Sample(201); mismatch.Actors[1] = mismatch.Actors[1] with { Server = 2302 };
        var longFight = Sample(202); longFight.Damage[1] = longFight.Damage[1] with { Timestamp = 301000 };
        var band = Sample(203); band.Actors[1] = band.Actors[1] with { Cp = 16000 };
        var demo = Sample(204); demo.Demo = true;
        check(Benchmarks.Peers(f, local, [mismatch, longFight, band, demo, f]).Length == 0,
            "Different servers, duration bands, CP brackets, demo data and the current fight cannot enter a comparison");
        var unknown = f.Copy(); unknown.Actors[1] = unknown.Actors[1] with { Job = "Unknown" };
        check(Benchmarks.Peers(unknown, local, peers).Length == 0 && Benchmarks.Report(unknown, peers).First().Status.Contains("class"),
            "Missing class metadata produces an actionable report status instead of a false cohort");
        var cohort = Benchmarks.Cohort(f, local, peers, id => id.ToString());
        check(cohort.Where(r => r.Tier == "Top quarter (local)").Sum(r => r.MeanDamageShare) == 100 && cohort.All(r => r.UsagePercent <= 100),
            "Cohort skill shares conserve damage and are averaged across observations");
        var differences = Benchmarks.Differences(f, local, peers).Where(r => r.Tier == "Top 5% (local)").ToArray();
        check(differences.Length == 6 && differences.All(r => r.Samples == 5) && differences.First().Difference > 0,
            "Top-five-percent view compares six measured metrics with the selected character");
        check(Benchmarks.Specs(f, local, peers, id => id.ToString()).Any(r => r.Code == 17030000 && r.Level == 7 && r.UsagePercent == 100) &&
            Benchmarks.Equipment(f, local, peers).Any(r => r.Slot == "Weapon") && Benchmarks.Openers(f, local, peers, id => id.ToString()).All(r => r.UsagePercent == 100),
            "Cohort loadout, equipment and opener tables include known specs even when their skills were unused");
        foreach (var peer in peers) peer.Builds[1] = peer.Builds[1] with { Complete = false };
        check(Benchmarks.Specs(f, local, peers, id => id.ToString()).Length == 0, "Partial loadouts do not turn unknown choices into absent skills in spec usage rates");
        foreach (var peer in peers.Skip(1)) peer.Builds[1] = peer.Builds[1] with { Equipment = new() { ["Ring"] = "Example ring" } };
        var gearUsage = Benchmarks.Equipment(f, local, peers).Where(r => r.Slot == "Ring").ToArray();
        check(gearUsage.Length > 0 && gearUsage.All(r => r.UsagePercent == 100), "Equipment frequencies use observations with a known item in that slot");
        foreach (var peer in peers) peer.Builds.Clear();
        check(Benchmarks.Specs(f, local, peers, id => id.ToString()).Length == 0 && Benchmarks.Equipment(f, local, peers).Length == 0,
            "Missing peer loadouts stay unknown rather than becoming inferred spec or gear recommendations");
        check(Benchmarks.Trends(peers.Concat(peers)).Count(r => r.Character == "Example cleric") == 100,
            "Character trends retain server identity and deduplicate imported fights");
    }
}
