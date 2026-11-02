using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class PositionEstimatorTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var estimator = new PositionEstimator(12345); estimator.Clear(1010);
        var concentrated = new PositionEstimator(6789);
        concentrated.Constrain(new(1010, 100, 10000, 20000, 1000, 1000, 500, false, "initial"));
        for (int i = 0; i < 100; i++)
            concentrated.Constrain(new(1010, 100, 10000, 20000, 1000, 1000, 100, false, "constraint-" + i));
        var concentratedMoments = concentrated.Moments!;
        File.WriteAllText(Path.Combine(temp, "position-concentration.json"), JsonSerializer.Serialize(concentratedMoments));
        check(concentrated.DistinctParticleLocations() == PositionEstimator.Capacity && concentratedMoments.Accuracy is > 10 and < 150 && concentratedMoments.Z == null && double.IsFinite(concentratedMoments.X),
            "Repeated strong proximity evidence retains horizontal particle diversity without inventing height");
        check(estimator.Current == null, "A position estimate requires measured coordinates or a located interaction source");
        var talk = new SpatialConstraint(1010, 100, 10000, 20000, 1100, 1200, 500, false, "npc-talk");
        estimator.Constrain(talk); var inferred = estimator.Current!;
        check(inferred is { Z: null, Lost: false } && double.Hypot(inferred.X - 10000, inferred.Y - 20000) < 100 && inferred.Accuracy is > 250 and < 450,
            "NPC proximity initializes an uncertain horizontal particle cloud without inventing player height");
        estimator.Constrain(talk);
        check(ReferenceEquals(inferred, estimator.Current), "Re-reading the same proximity evidence does not repeatedly tighten uncertainty");
        estimator.Observe(new(1010, 10100, 20100, 110, 1300)); var measured = estimator.Current!;
        check(measured.Z is double height && Math.Abs(height - 110) < 1e-9 && measured.Accuracy is > 40 and < 100 && measured.Accuracy < inferred.Accuracy && double.Hypot(measured.X - 10100, measured.Y - 20100) < 30,
            "Direct measurement refines the cloud with explicit height and nonzero measurement uncertainty");
        estimator.Constrain(talk with { Timestamp = 1250 }); estimator.Observe(new(1110, 0, 0, 0, 1400));
        check(ReferenceEquals(measured, estimator.Current), "Stale proximity and coordinates for another map cannot replace the current estimate");
        estimator.Advance(2300);
        check(estimator.Current is { Lost: false } propagated && propagated.Timestamp == 2300 && double.IsFinite(propagated.Accuracy),
            "Elapsed time propagates finite bounded-particle uncertainty without retaining event history");
        // Network-driven propagation uses the gap between updates, not time since measurement.
        for (long at = 3300; at <= 302300; at += 1000) estimator.Advance(at);
        check(estimator.Current?.Lost == false, "Regular network-time propagation does not trigger the reference five-minute gap limit");
        estimator.Advance(602301);
        check(estimator.Current?.Lost == true, "A gap exceeding five minutes marks the retained estimate lost without fabricating a new position timestamp");
        estimator.Constrain(talk with { Timestamp = 602400, PositionAt = 602400 });
        check(estimator.Current is { Lost: false, Z: null }, "New located interaction recovers a lost estimate without reusing stale height");
        estimator.Clear(1110); check(estimator.Current == null, "Explicit map boundary clears particle output");
        var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: new(1, "Synthetic", true, false, 0, "Cleric", 2301)));
        void Send(string kind, long at, object data) => engine.Apply(new("network", Network: new(kind, at, "test", JsonSerializer.SerializeToElement(data))));
        Send("local-zone", 1000, new { MapId = 1010 }); Send("npc-spawn", 1001, new { EntityId = 100, NpcId = 2000001, OwnerId = 0 });
        Send("actor-position", 1100, new MovementReading(100, 10000, 20000, 100, true, 0)); Send("npc-talk", 1200, new NpcTalkReading(100, true));
        check(engine.PositionPrediction.Current is { Z: null, Lost: false } && engine.Position.Latest == null && engine.Fight.LocalPosition == null,
            "Production ingestion exposes prediction separately and leaves captured fight coordinates unchanged");
        engine.NewFight("Reset"); check(engine.PositionPrediction.Current != null, "DPS reset retains current scene prediction");
        Send("local-zone", 1400, new { MapId = 1110 }); check(engine.PositionPrediction.Current == null, "Production map transition resets prediction");
        Send("actor-position", 1500, new MovementReading(100, 10000, 20000, 100, true, 0));
        var cast = new SkillStartReading(2, 17010001, 1, 100, 0, 0, 0) { Aim = new(0, null, null, null) };
        Send("skill-use", 1600, cast);
        check(engine.PositionPrediction.Current == null, "Another player's cast cannot locate the local player");
        cast = cast with { ActorId = 1 };
        Send("skill-use", 1600, cast);
        var bearing = engine.PositionPrediction.Current!;
        check(bearing is { X: > 7800 and < 8300, Y: > 19900 and < 20100, Z: null, Accuracy: > 700 and < 1100 },
            "Local facing toward a known target locates an uncertain region behind that target without borrowing its height");
        Send("skill-use", 1600, cast);
        check(ReferenceEquals(bearing, engine.PositionPrediction.Current), "Duplicate cast evidence cannot repeatedly tighten the directional estimate");
        engine.PositionPrediction.Advance(1700); var propagatedBearing = engine.PositionPrediction.Current;
        Send("skill-use", 1700, cast with { TargetId = 999 });
        check(ReferenceEquals(propagatedBearing, engine.PositionPrediction.Current), "An unlocated target adds no directional evidence beyond normal elapsed-time propagation");
        Send("skill-use", 1800, cast with { Aim = new(90, 12000, 22000, 150) });
        check(engine.PositionPrediction.Current is { Z: null, Lost: false } && engine.Position.Latest == null && engine.Fight.LocalPosition == null,
            "Explicit cast target location supplies a bearing without being mistaken for a player position or height");
        NetworkEvent FacingEvent(string kind, long time, object value) => new(kind, time, "test", JsonSerializer.SerializeToElement(value));
        var facingScene = new SpatialEvidence();
        facingScene.Apply(FacingEvent("local-zone", 1000, new { MapId = 1010 }), 1);
        facingScene.Apply(FacingEvent("actor-position", 1100, new MovementReading(100, 10000, 20000, 100, true, 0)), 1);
        var targetOnly = FacingEvent("skill-use", 1200, cast with { Aim = null });
        var facingOnly = FacingEvent("actor-position", 1200, new { ActorId = 1, Direction = 90f });
        var combined = new PositionEstimator(42);
        combined.ApplyCast(FacingEvent("skill-use", 1200, cast with { Aim = new(90, null, null, null) }), 1, facingScene);
        var castFirst = new PositionEstimator(42); castFirst.ApplyCast(targetOnly, 1, facingScene);
        check(castFirst.Moments == null, "A cast target alone cannot supply missing facing evidence");
        castFirst.ApplyCast(facingOnly, 1, facingScene);
        var facingFirst = new PositionEstimator(42); facingFirst.ApplyCast(facingOnly, 1, facingScene); facingFirst.ApplyCast(targetOnly, 1, facingScene);
        check(castFirst.Moments == combined.Moments && facingFirst.Moments == combined.Moments,
            "Standalone local facing pairs with a same-timestamp cast in either arrival order");
        var ignoredFacing = new PositionEstimator(42); ignoredFacing.ApplyCast(targetOnly, 1, facingScene);
        ignoredFacing.ApplyCast(FacingEvent("actor-position", 1200, new { ActorId = 2, Direction = 90f }), 1, facingScene);
        ignoredFacing.ApplyCast(FacingEvent("actor-position", 1201, new { ActorId = 1, Direction = 90f }), 1, facingScene);
        check(ignoredFacing.Moments == null, "Other actors and different timestamps cannot complete a local cast bearing");
        ignoredFacing.Clear(1010); ignoredFacing.ApplyCast(facingOnly, 1, facingScene);
        check(ignoredFacing.Moments == null, "Map reset discards pending cast targets before accepting facing");
        File.WriteAllText(Path.Combine(temp, "position-estimator-core.json"), JsonSerializer.Serialize(new {
            ParticleCapacity = PositionEstimator.Capacity, Inferred = inferred, Measured = measured, Bearing = bearing,
            Scope = "Independent core: measurements, interaction circles, cast bearings, resampling, motion and stale/lost handling. Visibility, standalone facing, flight, learned ranges and smoothing remain incomplete."
        }));
    }
}
