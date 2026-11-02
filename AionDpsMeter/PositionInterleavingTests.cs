using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class PositionInterleavingTests
{
    internal static void Run(Action<bool, string> check, string temp) {
        var rng = new VisibilitySceneTests.ComparisonRandom(); var estimator = new PositionEstimator(rng, rng.Normal);
        var scene = new SpatialEvidence(); var position = new LocalPositionTracker(); var steps = new List<object>();
        void Step(string kind, long at, object fields, params object[] reference) {
            var message = new NetworkEvent(kind, at, "test", JsonSerializer.SerializeToElement(fields));
            long boundary = scene.Boundary; scene.Apply(message, 1); if (boundary != scene.Boundary) estimator.Clear(scene.MapId);
            position.Apply(message, 1); estimator.BeginNetworkEvent(at);
            try {
                estimator.ApplyFlight(message, 1);
                if (position.Latest is { } measured) estimator.Observe(measured);
                if (scene.LastConstraint is { } proximity) estimator.Constrain(proximity);
                estimator.ApplyCast(message, 1, scene); estimator.ApplyVisibility(message, scene, position.Latest);
            } finally { estimator.EndNetworkEvent(at); }
            steps.Add(new { Reference = reference, Output = estimator.Current, Moments = estimator.Moments });
        }
        Step("local-zone", 1000, new { MapId = 1010 }, new { type = "map", at = 1000, map = 1010 });
        Step("local-position", 1000, new { X = 10000, Y = 20000, Z = 100 }, new { type = "position", at = 1000, entity = 1, x = 10000, y = 20000, z = 100 });
        Step("npc-spawn", 1100, new { EntityId = 100, NpcId = 2000001 }, new { type = "spawn", at = 1100, entity = 100, npc = 2000001 });
        Step("actor-position", 1100, new { ActorId = 100, X = 10400, Y = 20000, Z = 100, EntityKind = "npc" }, new { type = "position", at = 1100, entity = 100, x = 10400, y = 20000, z = 100, moving = false });
        Step("target", 1200, new { ActorId = 100, TargetId = 1 }, new { type = "target", at = 1200, entity = 100, target = 1 });
        Step("npc-talk", 1300, new { EntityId = 100, Talking = true }, new { type = "npc-talk", at = 1300, npc = 100, talking = true });
        Step("clock", 1500, new { }, new { type = "tick", at = 1500 });
        Step("resources", 1600, new { EntityId = 1, Values = new Dictionary<int, long> { [6] = 100 } }, new { type = "flight", at = 1600, fp = 100 });
        Step("resources", 1700, new { EntityId = 1, Values = new Dictionary<int, long> { [6] = 90 } }, new { type = "flight", at = 1700, fp = 90 });
        Step("local-position", 1800, new { X = 10100, Y = 20100, Z = 105 }, new { type = "position", at = 1800, entity = 1, x = 10100, y = 20100, z = 105 });
        Step("clock", 2100, new { }, new { type = "tick", at = 2100 });
        Step("skill-use", 2150, new SkillStartReading(1, 17010001, 1, 100, 0, 0, 0) { Aim = new(0, null, null, null) },
            new { type = "cast", at = 2150, entity = 1, target = 100 }, new { type = "facing", at = 2150, yaw = 0 });
        Step("clock", 2300, new { }, new { type = "tick", at = 2300 });
        Step("skill-use", 2350, new SkillStartReading(1, 17010001, 1, 100, 0, 0, 0),
            new { type = "cast", at = 2350, entity = 1, target = 100 });
        Step("actor-position", 2350, new { ActorId = 1, Direction = 15f }, new { type = "facing", at = 2350, yaw = 15 });
        Step("clock", 2400, new { }, new { type = "tick", at = 2400 });
        Step("actor-position", 2450, new { ActorId = 1, Direction = 30f }, new { type = "facing", at = 2450, yaw = 30 });
        Step("skill-use", 2450, new SkillStartReading(1, 17010001, 1, 100, 0, 0, 0),
            new { type = "cast", at = 2450, entity = 1, target = 100 });
        check(estimator.Moments is { Accuracy: > 0 } m && double.IsFinite(m.Accuracy) && position.Latest?.X == 10100,
            "Interleaved movement, NPC interaction, flight and cast evidence preserves finite inference and captured coordinates");
        File.WriteAllText(Path.Combine(temp, "position-interleaving.json"), JsonSerializer.Serialize(steps));
        var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: new(1, "Synthetic", true, false, 0, "Cleric", 1)));
        void Send(string kind, long timestamp, object fields) => engine.Apply(new("network", Network: new(kind, timestamp, "test", JsonSerializer.SerializeToElement(fields))));
        Send("local-zone", 1000, new { MapId = 1010 }); Send("local-position", 1000, new { X = 10000, Y = 20000, Z = 100 });
        var beforeTimer = engine.PositionPrediction.Current;
        engine.AdvanceTime(2000);
        check(ReferenceEquals(beforeTimer, engine.PositionPrediction.Current), "UI maintenance does not advance the network estimator or consume random motion");
        Send("local-position", 1500, new { X = 11000, Y = 20000, Z = 100 });
        check(engine.PositionPrediction.Current is { Timestamp: 1500, X: > 10970 and < 11030 },
            "A valid queued position older than the UI clock still updates the network estimator");
    }
}
