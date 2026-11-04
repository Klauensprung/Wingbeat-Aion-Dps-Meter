using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class StaggerBreakTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var tracker = new StaggerBreakTracker();
        NetworkEvent Guard(long at, bool broken, int actor = 1) => new("guard", at, "test", JsonSerializer.SerializeToElement(new { ActorId = actor, State = broken ? 3 : 1 }));
        void Send(NetworkEvent e) => tracker.Apply(e, id => id == 1 ? 2001 : 0);
        Send(Guard(100, true)); var open = tracker.Snapshot(0, 1000).Rows;
        Send(Guard(200, true)); Send(Guard(400, false)); Send(Guard(500, true, 2));
        check(tracker.Snapshot(0, 1000).Rows is [{ StartedAt: 100, ObservedEnd: 400 }] && open is [{ ObservedEnd: null }],
            "Stagger transitions deduplicate repeated breaks, exclude non-bosses and preserve open snapshot isolation");
        Send(Guard(500, true)); Send(new("entity-removed", 600, "test", JsonSerializer.SerializeToElement(new { EntityId = 1 })));
        check(tracker.Snapshot(0, 1000).Rows.Last().ObservedEnd == 600, "Removal closes a captured boss break");
        File.WriteAllText(Path.Combine(temp, "stagger-reference-fixtures.json"), JsonSerializer.Serialize(new[] { new {
            name = "Local repeated/non-boss/recovery/removal sequence",
            events = new object[] {
                new { type = "stagger", at = 100, entity = 1, broken = true },
                new { type = "stagger", at = 200, entity = 1, broken = true },
                new { type = "stagger", at = 400, entity = 1, broken = false },
                new { type = "stagger", at = 500, entity = 2, broken = true },
                new { type = "stagger", at = 500, entity = 1, broken = true },
                new { type = "removed", at = 600, entity = 1 }
            }, expected = tracker.Snapshot(0, 1000).Rows.Select(r => new { at = r.StartedAt, npc = r.NpcId, until = r.ObservedEnd })
        } }));
        Send(Guard(700, true)); Send(new("local-zone", 800, "test", JsonSerializer.SerializeToElement(new { MapId = 1010 })));
        Send(Guard(900, false));
        check(tracker.Snapshot(0, 1000).Rows.Last().ObservedEnd == null, "Scene changes keep an uncertain break end unknown rather than attach another scene's recovery");
        for (int i = 0; i < 3000; i++) { Send(Guard(1000 + i * 2, true)); Send(Guard(1001 + i * 2, false)); }
        check(tracker.Count == StaggerBreakTracker.Capacity && tracker.Snapshot(0, 10000).Truncated,
            "Stagger interval storage remains bounded and explicitly reports omitted history");
        var engine = new CombatEngine(); int npc = BossCatalogue.Default.Bosses.First(b => b.FieldBoss || b.InstanceKeys.Length > 0).Variants[0].NpcId;
        engine.Apply(new("local-player", Actor: new(1, "Player", true, false, 0, "Cleric", 2301)));
        engine.Apply(new("actor", Actor: new(2, "Boss", false, false, 0, "Unknown", 0) { NpcId = npc }));
        void Hit(long at) => engine.RecordDamage(new() { ActorId = 1, TargetId = 2, SkillCode = 17010000, Timestamp = at, Damage = 100 }, false);
        Hit(1000); engine.Apply(new("network", Network: Guard(1100, true, 2))); Hit(1500);
        var snapshot = engine.Snapshot();
        check(snapshot.StaggerBreaks is [{ EntityId: 2, StartedAt: 1100, ObservedEnd: null }] &&
            BossEncounterAnalysis.Build(snapshot, _ => "Skill").Single().Moments.Any(m => m.Kind == "Stagger break" && m.Detail.Contains("Capped")),
            "Engine snapshots expose compact open break intervals with explicit capped boss-analysis duration");
        engine.NewFight("Manual reset"); Hit(1600); engine.Apply(new("network", Network: Guard(1700, true, 2))); Hit(1800);
        check(engine.Snapshot().StaggerBreaks is { Length: 0 }, "Manual DPS reset does not recount an already-broken boss as a new transition");
        var store = new FightStore(Path.Combine(temp, "stagger-summary")); store.Save(snapshot);
        var restored = store.Load(Path.Combine(store.Root, snapshot.Id + ".json"));
        check(restored.StaggerBreaks!.SequenceEqual(snapshot.StaggerBreaks!) &&
            BossEncounterAnalysis.Build(restored, _ => "Skill").Single().Moments.Any(m => m.Kind == "Stagger break"),
            "Saved stagger intervals and boss-analysis moments survive actual storage reload");
        engine = new CombatEngine();
        engine.Apply(new("local-player", Actor: new(1, "Player", true, false, 0, "Cleric", 2301)));
        engine.Apply(new("actor", Actor: new(2, "Boss", false, false, 0, "Unknown", 0) { NpcId = npc }));
        Hit(1000); engine.Apply(new("network", Network: Guard(1100, true, 2))); Hit(2000);
        var beforeIdle = engine.Snapshot(); engine.AdvanceTime(17000);
        engine.Apply(new("network", Network: Guard(18000, false, 2))); Hit(19000);
        var recovered = engine.Snapshot();
        check(recovered.Id == beforeIdle.Id && recovered.StaggerBreaks is [{ StartedAt: 1100, ObservedEnd: 18000 }] &&
            beforeIdle.StaggerBreaks is [{ ObservedEnd: null }],
            "Automatic encounter recovery retains one break and its observed end without mutating the idle snapshot");
        engine.Apply(new("actor", Actor: new(2, "Different NPC", false, false, 0, "Unknown", 0) { NpcId = npc + 1 }));
        var recycled = engine.Snapshot();
        check(recycled.StaggerBreaks is [{ EntityId: var archived }] && archived != 2 && recycled.Actors[archived].OriginalEntityId == 2,
            "Recycled NPC identities keep historical breaks attached to the archived boss");
        var invalid = snapshot.Copy(); invalid.Id = Guid.NewGuid().ToString("N");
        invalid.StaggerBreaks = [new(2, npc, 1100, 1000)]; store.Save(invalid);
        bool rejected = false;
        try { store.Load(Path.Combine(store.Root, invalid.Id + ".json")); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "Imported stagger summaries reject an observed end preceding the break start");
        engine = new CombatEngine();
        engine.Apply(new("local-player", Actor: new(1, "Player", true, false, 0, "Cleric", 2301)));
        engine.Apply(new("actor", Actor: new(2, "Uncatalogued NPC", false, false, 0, "Unknown", 0) { NpcId = int.MaxValue }));
        Hit(1000); engine.Apply(new("network", Network: Guard(1100, true, 2))); Hit(1200);
        var dynamicBoss = engine.Snapshot();
        check(dynamicBoss.Actors[2].CapturedBoss == true && dynamicBoss.StaggerBreaks is [{ StartedAt: 1100 }] &&
            EngagedBoss.Latest(dynamicBoss) == 2 && BossEncounterAnalysis.Build(dynamicBoss, _ => "Skill").Length == 1,
            "Guard data promotes an uncatalogued NPC on an unnamed map into live targeting and boss analysis");
        store.Save(dynamicBoss);
        var dynamicSaved = store.Load(Path.Combine(store.Root, dynamicBoss.Id + ".json"));
        check(dynamicSaved.Actors[2].CapturedBoss == true && BossEncounterAnalysis.Build(dynamicSaved, _ => "Skill").Length == 1 &&
            BossAnalysis.Summary(dynamicSaved).Contains("Uncatalogued NPC"),
            "Dynamic boss classification survives compact fight storage");
        dynamicSaved.Deaths.Add(new(2, 1250));
        BossTelegraphs.Apply(dynamicSaved, new("telegraph", 1150, "test", JsonSerializer.SerializeToElement(new TelegraphNotice(2, 17010000, 1, 500))));
        check(OverlayCombatFilter.Apply(dynamicSaved, true, 0).Damage.Count == 2 &&
            BossPersonalBest.Records(dynamicSaved) is [{ NpcId: int.MaxValue, Boss: "Uncatalogued NPC" }] && dynamicSaved.Telegraphs.Count == 1,
            "Guard-identified bosses participate in boss-only DPS, death-based records and compact telegraphs");
        dynamicSaved.Actors[2] = dynamicSaved.Actors[2] with { CapturedBoss = false };
        check(OverlayCombatFilter.Apply(dynamicSaved, true, 0).Damage.Count == 0 && BossPersonalBest.Records(dynamicSaved).Length == 0,
            "Explicit captured non-boss classification excludes DPS filters and personal records");
        engine.Apply(new("network", Network: new("actor-position", 1300, "test", JsonSerializer.SerializeToElement(new { ActorId = 2, VisibilityReason = 1 }))));
        check(engine.Snapshot().Actors[2].CapturedBoss == true, "Same-scene respawn preserves guard-driven boss identity");
        engine.Apply(new("network", Network: Guard(1400, true, 2))); Hit(1500);
        check(engine.Snapshot().StaggerBreaks is [{ StartedAt: 1100 }, { StartedAt: 1400 }],
            "Production ActorId spawn events reset active break correlation and allow a new break");
        using var mapStream = typeof(DungeonMaps).Assembly.GetManifestResourceStream("Wingbeat.DungeonMaps.json")!;
        var mapData = JsonSerializer.Deserialize<DungeonMapData>(mapStream)!;
        int namedMap = mapData.BossesByMap.First(p => p.Value.Length > 0).Key;
        engine.Apply(new("network", Network: new("local-zone", 1600, "test", JsonSerializer.SerializeToElement(new { MapId = namedMap }))));
        engine.Apply(new("local-player", Actor: new(1, "Player", true, false, 0, "Cleric", 2301)));
        engine.Apply(new("actor", Actor: new(3, "Other NPC", false, false, 0, "Unknown", 0) { NpcId = int.MaxValue }));
        engine.Apply(new("network", Network: Guard(1700, true, 3)));
        check(engine.Snapshot().Actors[3].CapturedBoss == false,
            "Named map guard data cannot promote an NPC absent from its boss list");
        engine.Apply(new("actor", Actor: new(2, "Uncatalogued NPC", false, false, 0, "Unknown", 0) { NpcId = int.MaxValue }));
        engine.Apply(new("network", Network: Guard(1800, true, 2)));
        check(engine.Snapshot().Actors[2].CapturedBoss == false && dynamicBoss.Actors[2].CapturedBoss == true,
            "Map changes discard prior guard promotion even when NPC template and entity ID are reused, preserving old snapshots");
        engine.NewFight("Scene test");
        Hit(1900); engine.Apply(new("network", Network: Guard(1500, true, 2))); Hit(2000);
        check(engine.Snapshot().Actors[2].CapturedBoss == false && engine.Snapshot().StaggerBreaks is { Length: 0 },
            "Delayed guard data from an earlier scene cannot restore boss promotion or create a new break");
    }
}
