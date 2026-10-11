using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

public static class DisplayTests
{
    public static void Run(Action<bool, string> check, string temp)
    {
        string catalog = Path.Combine(temp, "skill-names.json");
        File.WriteAllText(catalog, """{"17040000":"Family A","17040120":"Exact variant","1220000":"NPC A","1225540":"NPC B","3000020":"Effect A"}""");
        var names = new SkillNames(catalog);
        check(names.Count == 5 && names.Resolve(17040120) is { Name: "Exact variant", Match: "exact" }, "Exact skill variant wins over family name");
        check(names.Resolve(17040230) is { Name: "Family A", MatchedCode: 17040000, Code: 17040230, Match: "family" }, "Player variants resolve without changing recorded IDs");
        check(names.Resolve(1225540).Name == "NPC B" && names.Resolve(3000020).Name == "Effect A", "NPC and effect IDs resolve exactly");
        check(names.Resolve(1220020).Match == "unknown" && names.Resolve(3000021).Match == "unknown" && names.GetSkillName(999) == "Unknown skill 999", "Unknown NPCs and effects are not rounded into unrelated skills");
        File.WriteAllText(catalog, "{}");
        check(names.GetSkillName(17040120) == "Exact variant", "Catalog is cached in memory; display lookup performs no file reads");
        check(new SkillNames(catalog).Count == 0 && new SkillNames(catalog).LoadError != null, "Invalid catalog degrades to numeric IDs with a diagnostic");
        foreach (var bad in new[] { "{}", "[]", "{\"1\":\"a\",\"01\":\"b\"}", "{\"-1\":\"bad\"}", "{\"1\":4}", "{\"1\":\"bad\\nname\"}" })
        {
            File.WriteAllText(catalog, bad);
            bool rejected = false; try { _ = SkillNames.Read(catalog); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "Malformed catalog rejected: " + bad);
        }
        check(new SkillNames(Path.Combine(temp, "absent.json")).Resolve(100).Match == "unknown", "Missing catalog does not prevent offline analysis");

        var engine = new CombatEngine(); long revision = -1;
        var initial = engine.SnapshotIfChanged(null, ref revision);
        check(ReferenceEquals(initial, engine.SnapshotIfChanged(initial, ref revision)), "Idle capture reuses UI snapshot");
        engine.RecordDamage(new DamageEvent { ActorId = 1, TargetId = 2, Timestamp = 1000, Damage = 5, SkillCode = 17040120 }, false);
        var damage = engine.SnapshotIfChanged(initial, ref revision);
        check(!ReferenceEquals(initial, damage) && initial.Damage.Count == 0 && damage.Damage.Count == 1, "Damage invalidates snapshot without changing previous view");
        engine.Apply(new("actor", Actor: new(1, "Named actor", true, true, 100, "Unknown", 1)));
        var actor = engine.SnapshotIfChanged(damage, ref revision);
        check(!ReferenceEquals(damage, actor) && actor.Name(1) == "Named actor" && damage.Name(1) != actor.Name(1), "Identity-only updates invalidate snapshot");
        engine.Apply(new("energy", Energy: new(1, 2, true, 1000)));
        var energy = engine.SnapshotIfChanged(actor, ref revision);
        check(!ReferenceEquals(actor, energy) && energy.Energy?.Daily == 1, "Energy-only updates invalidate snapshot");
        engine.Apply(new("network", Network: new("test", 1000, "0000", JsonSerializer.SerializeToElement(new { value = 1 }))));
        var network = engine.SnapshotIfChanged(energy, ref revision);
        check(!ReferenceEquals(energy, network) && network.Network.Count == 1, "Network-only updates invalidate snapshot");
        engine.RecordDamage(new DamageEvent { ActorId = 1, TargetId = 1, Timestamp = 2000, Damage = 2 }, true);
        var healing = engine.SnapshotIfChanged(network, ref revision);
        check(healing.Healing.Count == 1 && network.Healing.Count == 0, "Healing invalidates snapshot");
        engine.Apply(new("defense", Defense: new DefenseEvent { ActorId = 1, TargetId = 2, Timestamp = 3000, Outcome = DefenseOutcome.Block }));
        var defense = engine.SnapshotIfChanged(healing, ref revision);
        check(defense.Defense.Count == 1 && healing.Defense.Count == 0, "Defense invalidates snapshot");
        engine.SetLabel("Changed label");
        var label = engine.SnapshotIfChanged(defense, ref revision);
        check(label.Label == "Changed label" && defense.Label != label.Label, "Label change invalidates snapshot");
        engine.NewFight("Next");
        check(engine.SnapshotIfChanged(label, ref revision).Id != label.Id, "New fight invalidates snapshot");

        var f = SelfTest.Demo();
        var marks = Analysis.Timeline(f, [1, 2, 1]);
        check(marks.Length == 62 && marks.First().Seconds == 0 && marks.Last().Fraction == 1, "Timeline retains shared duration and distinct actor selection");
        check(marks.Select(m => (m.Seconds, m.Fraction)).SequenceEqual(new[] { 1, 2 }.SelectMany(id => f.Damage.Where(e => e.ActorId == id).Select(e => ((e.Timestamp - f.Start) / 1000d, (e.Timestamp - f.Start) / 1000d / f.Seconds)))), "Optimized timeline matches previous timestamp calculations");
        var large = new Fight();
        for (int i = 0; i < 5010; i++) large.Damage.Add(new DamageEvent { ActorId = 1, Timestamp = i, Damage = 1 });
        var capped = Analysis.Timeline(large, [1]);
        check(capped.Length == 5000 && capped.First().Seconds == .01 && Analysis.Timeline(new Fight(), [1]).Length == 0, "Timeline handles empty fights and caps marks per actor");
        var audit = JsonSerializer.SerializeToElement(SkillAudit.Report(damage, names));
        check(audit.GetProperty("ResolvedSkills").GetInt32() == 1 && audit.GetProperty("ResolvedEvents").GetInt32() == 1 && damage.Damage[0].SkillCode == 17040120 && damage.Healing.Count == 0, "Name diagnostics preserve raw events and never infer healing");
    }
}
