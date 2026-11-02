using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class DungeonSkillTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: new(1, "Synthetic", true, false, 0, "Cleric", 2301)));
        var events = new List<object> { new { type = "map", at = 1, map = 600002 } };
        engine.Apply(new("network", Network: new("local-zone", 1, "test", JsonSerializer.SerializeToElement(new { MapId = 600002 }))));
        engine.Apply(new("actor", Actor: new(2, "Summon", false, false, 0, "Unknown", 0) { OwnerId = 1 }));
        void Hit(long at, int actor, int skill, bool heal = false) {
            events.Add(new { type = heal ? "heal" : "damage", at, actor, skill });
            engine.RecordDamage(new() { Timestamp = at, ActorId = actor, TargetId = 99, SkillCode = skill, Damage = 1 }, heal);
        }
        Hit(2, 1, 17010001); var earlier = engine.Snapshot();
        Hit(3, 1, 17010099); Hit(4, 1, 17000001); Hit(5, 1, 17000002); Hit(6, 1, 17100005, true);
        Hit(7, 2, 17200006); Hit(8, 3, 17300007); Hit(9, 1, 20000000); Hit(10, 1, 10999999);
        check(engine.Fight.DungeonRun!.Skills.SequenceEqual(new[] { 17010000, 17000001, 17000002, 17100000, 17200000 }) && earlier.DungeonRun!.Skills.SequenceEqual(new[] { 17010000 }),
            "Dungeon skills group variants, preserve basic attacks, include local healing and owned summons, and exclude other actors and NPC skill ranges");
        engine.NewFight("Reset"); Hit(11, 1, 17400003); var final = engine.Snapshot();
        check(final.DungeonRun!.Id == earlier.DungeonRun!.Id && final.DungeonRun.Skills.Length == 6,
            "DPS reset preserves the cumulative unique local skills for the run");
        var store = new FightStore(Path.Combine(temp, "dungeon-skills")); store.Save(final);
        var restored = store.Load(Path.Combine(store.Root, final.Id + ".json"));
        check(ObservedRunGroups.Build([FightArchiveSummary.Create(earlier), FightArchiveSummary.Create(restored)]).Single().Skills.Length == 6,
            "Saved compact run skill lists use the latest cumulative snapshot rather than duplicating earlier skills");
        events.Add(new { type = "map", at = 12, map = 1010 });
        engine.Apply(new("network", Network: new("local-zone", 12, "test", JsonSerializer.SerializeToElement(new { MapId = 1010 }))));
        File.WriteAllText(Path.Combine(temp, "reference-dungeon-skills.json"), JsonSerializer.Serialize(new {
            IncludeSkills = true, SelfActors = new[] { 1, 2 }, Events = events, Runs = new[] { new {
                Map = final.DungeonRun.MapId, Entered = final.DungeonRun.EnteredAt, Left = 12,
                Used = final.DungeonRun.UsedItems, Received = final.DungeonRun.ReceivedItems, Skills = final.DungeonRun.Skills
            } }
        }));
        var bounded = new DungeonRunTracker(); bounded.Apply(new("local-zone", 1, "test", JsonSerializer.SerializeToElement(new { MapId = 600002 })));
        for (int i = 0; i < 260; i++) bounded.RecordSkill(i + 2, 17000000 + i);
        var latest = bounded.Current!; bounded.RecordSkill(1, 18000001);
        check(latest.Skills.Length == 256 && latest.OmittedSkillEvents == 4 && ReferenceEquals(latest, bounded.Current),
            "Dungeon skill retention bounds unique entries, counts omitted observations and rejects stale updates");
        bool invalid = false; try { DungeonRunTracker.Validate(latest with { Skills = [17000001, 17000001] }); } catch (InvalidDataException) { invalid = true; }
        check(invalid, "Imported dungeon skill summaries reject duplicate identities");
    }
}
