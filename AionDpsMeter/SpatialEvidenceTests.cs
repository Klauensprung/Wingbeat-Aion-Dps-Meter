using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class SpatialEvidenceTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: new(1, "Local", true, false, 0, "Cleric", 2301)));
        void Send(string kind, long at, object fields) => engine.Apply(new("network", Network: new(kind, at, "test", JsonSerializer.SerializeToElement(fields))));
        Send("local-zone", 1, new { MapId = 1010 });
        Send("npc-spawn", 2, new { EntityId = 100, NpcId = 2000001, OwnerId = 0 });
        Send("npc-talk", 3, new NpcTalkReading(100, true));
        check(engine.Spatial.LastConstraint == null, "NPC interaction without known coordinates cannot invent spatial evidence");
        Send("actor-position", 4, new MovementReading(100, 10000, 20000, 100, true, 0));
        Send("npc-talk", 5, new NpcTalkReading(100, false));
        check(engine.Spatial.LastConstraint == null, "NPC conversation finish cannot initialize a proximity constraint");
        Send("npc-talk", 6, new NpcTalkReading(100, true));
        check(engine.Spatial.LastConstraint is { Radius: 500, X: 10000, Y: 20000, PositionAt: 4, Timestamp: 6, Moving: false } && engine.Position.Latest == null && engine.Fight.LocalPosition == null,
            "NPC proximity retains source coordinates and uncertainty radius separately from captured player position");
        var earlier = engine.Spatial.LastConstraint; engine.NewFight("Reset");
        check(engine.Spatial.LastConstraint == earlier && engine.Spatial.Count == 1,
            "Manual DPS reset preserves bounded current-scene evidence without adding fight history");
        Send("gathered", 7, new { EntityId = 100 });
        check(engine.Spatial.LastConstraint is { Radius: 700, Source: "gathered" }, "Gathering supplies its distinct proximity radius");
        Send("target", 8, new { ActorId = 100, TargetId = 1 }); var targeted = engine.Spatial.LastConstraint;
        Send("target", 9, new { ActorId = 100, TargetId = 1 });
        check(targeted is { Radius: 2000 } && ReferenceEquals(targeted, engine.Spatial.LastConstraint),
            "NPC target evidence applies only when its target changes to the local player");
        engine.RecordDamage(new() { Timestamp = 10, ActorId = 1, TargetId = 100, SkillCode = 17010000, Damage = 1 }, false);
        check(engine.Spatial.LastConstraint is { Radius: 3500, Source: "damage" }, "Direct combat supplies reach evidence without replacing measured coordinates");
        Send("entity-removed", 11, new { EntityId = 100 }); Send("npc-talk", 12, new NpcTalkReading(100, true));
        Send("actor-position", 10, new MovementReading(100, 1, 2, 3, true, 0));
        check(engine.Spatial.LastConstraint == null && engine.Spatial.Find(100) is { Visible: false, X: 10000 },
            "Removal invalidates constraints and retains a bounded tombstone that rejects stale movement");
        Send("npc-spawn", 13, new { EntityId = 100, NpcId = 2000002, OwnerId = 1 }); Send("npc-talk", 14, new NpcTalkReading(100, true));
        check(engine.Spatial.LastConstraint == null && engine.Spatial.Find(100)?.X == null,
            "Reused entity identity cannot inherit the prior NPC position");
        Send("actor-position", 15, new MovementReading(100, 300, 400, 100, false, 0));
        check(engine.Spatial.LastConstraint is { Radius: 1500, Moving: true, Source: "owned-summon" },
            "Owned summon movement supplies a timestamped moving proximity source");
        Send("local-zone", 16, new { MapId = 1110 });
        check(engine.Spatial.Count == 0 && engine.Spatial.LastConstraint == null && engine.Spatial.MapId == 1110,
            "Map boundaries clear entity positions and pending spatial constraints");
        for (int i = 1; i <= 4100; i++) Send("npc-spawn", 16 + i, new { EntityId = 10000 + i, NpcId = 2000001, OwnerId = 0 });
        check(engine.Spatial.Count == 4096 && engine.Spatial.Find(10001) == null && engine.Spatial.Find(14100) != null,
            "Scene state evicts oldest entity records at a fixed 4096-entry bound");
        engine.Apply(new("local-player", Actor: new(1, "Other", true, false, 0, "Cleric", 2301)));
        check(engine.Spatial.Count == 0 && engine.Spatial.LastConstraint == null, "Character switches clear prior scene evidence");
        Send("local-zone", 10000, new { MapId = 1010 });
        Send("actor-position", 11000, new { ActorId = 100, X = 100, Y = 200, Z = 3, VisibilityReason = 1, EntityKind = "npc" });
        check(engine.Spatial.Find(100)?.EnteredViewAt == null, "Map-entry warmup does not teach a visibility boundary from initial spawns");
        bool reasons = true;
        for (int reason = 0; reason <= 16; reason++) {
            long time = 15000 + reason * 10;
            Send("actor-position", time, new { ActorId = 100, X = 100, Y = 200, Z = 3, VisibilityReason = reason, EntityKind = "npc" });
            reasons &= (engine.Spatial.Find(100)?.EnteredViewAt == time) == (reason <= 1);
            Send("entity-removed", time + 1, new { EntityId = 100, Reason = reason });
            reasons &= (engine.Spatial.Find(100)?.LeftViewAt == time + 1) == (reason <= 1);
        }
        check(reasons, "Only normal and map-entry reason codes contribute view-edge timestamps; death, teleport and despawn do not");
        Send("actor-position", 16000, new { ActorId = 100, X = 0, Y = 0, Z = 0, VisibilityReason = 1, EntityKind = "player" });
        check(engine.Spatial.Find(100) is { EnteredViewAt: null, LeftViewAt: null, Kind: "player" },
            "Default zero spawn coordinates cannot create a visibility boundary and reappearance clears departure evidence");
        var visibilityStates = new List<SpatialEntity>();
        Send("actor-position", 17000, new { ActorId = 100, X = 100, Y = 200, Z = 3, VisibilityReason = 1, EntityKind = "npc" });
        Send("entity-removed", 17001, new { EntityId = 100, Reason = 1 });
        Send("actor-position", 17002, new MovementReading(100, 120, 220, 3, false, 0));
        visibilityStates.Add(engine.Spatial.Find(100)!);
        check(engine.Spatial.Find(100) is { Visible: false, X: 120, LeftViewAt: 17001, DepartedAtEdge: true },
            "Movement updates a departed entity's coordinates without inventing a visibility entry");
        Send("displacement", 17003, new { ActorId = 100, X = 130, Y = 230, Z = 3 });
        visibilityStates.Add(engine.Spatial.Find(100)!);
        check(engine.Spatial.Find(100) is { Visible: false, X: 130, LeftViewAt: 17001 },
            "Displacement preserves the departure boundary until an explicit spawn position arrives");
        Send("actor-position", 17004, new { ActorId = 100, X = 140, Y = 240, Z = 3, VisibilityReason = 1, EntityKind = "npc" });
        visibilityStates.Add(engine.Spatial.Find(100)!);
        check(engine.Spatial.Find(100) is { Visible: true, DepartedAtEdge: false, LeftViewAt: null, EnteredViewAt: 17004 },
            "Explicit reappearance clears the departed visibility state");
        Send("entity-removed", 17005, new { EntityId = 100, Reason = 2 });
        Send("actor-position", 17006, new MovementReading(100, 150, 250, 3, true, 0));
        visibilityStates.Add(engine.Spatial.Find(100)!);
        check(engine.Spatial.Find(100) is { Visible: true, DepartedAtEdge: false, LeftViewAt: null },
            "Movement after a non-edge removal creates fresh visible scene evidence");
        System.IO.File.WriteAllText(System.IO.Path.Combine(temp, "spatial-visibility.json"), JsonSerializer.Serialize(visibilityStates));
    }
}
