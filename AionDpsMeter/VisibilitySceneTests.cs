using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class VisibilitySceneTests
{
    internal sealed class ComparisonRandom : Random
    {
        uint state = 12345;
        public override double NextDouble() { state = unchecked(state * 1664525 + 1013904223); return state / 4294967296d; }
        public override int Next(int maxValue) => (int)(NextDouble() * maxValue);
        internal double Normal() => Math.Sqrt(-2 * Math.Log(1 - NextDouble())) * Math.Cos(2 * Math.PI * NextDouble());
    }
    internal static void Run(Action<bool, string> check, string temp)
    {
        NetworkEvent Event(string kind, long at, object data) => new(kind, at, "test", JsonSerializer.SerializeToElement(data));
        var samples = new List<object>(); bool finite = true, bounded = true;
        foreach (int count in new[] { 1, 4, 60, 180 }) {
            var scene = new SpatialEvidence(); scene.Apply(Event("local-zone", 1000, new { MapId = 1010 }), 1);
            for (int i = 0; i < count; i++) {
                double angle = i * Math.PI * 2 / count;
                scene.Apply(Event("npc-spawn", 1100, new { EntityId = 100 + i, NpcId = 2000001 + i, OwnerId = 0 }), 1);
                scene.Apply(Event("actor-position", 1100, new { ActorId = 100 + i, X = 10000 + Math.Cos(angle) * 1000, Y = 20000 + Math.Sin(angle) * 1000, Z = 100, EntityKind = "npc" }), 1);
            }
            var random = new ComparisonRandom();
            var estimator = new PositionEstimator(random, random.Normal);
            estimator.ApplyVisibility(Event("clock", 5000, new { }), scene, null);
            var initial = estimator.Moments!;
            finite &= double.IsFinite(initial.Accuracy) && initial.Accuracy > 0 && initial.Z is double z && Math.Abs(z - 100) < 1e-6;
            var published = estimator.Current;
            var enter = Event("actor-position", 5000, new { ActorId = 100, X = 11000, Y = 20000, Z = 100, EntityKind = "npc", VisibilityReason = 1 });
            scene.Apply(enter, 1); estimator.ApplyVisibility(enter, scene, null);
            var entered = estimator.Moments;
            var leave = Event("entity-removed", 5000, new { EntityId = 100, Reason = 1 });
            scene.Apply(leave, 1); estimator.ApplyVisibility(leave, scene, null);
            var left = estimator.Moments;
            samples.Add(new { Count = count, Initial = published, Moments = initial, Entered = entered, Left = left });
            var unchanged = estimator.Moments;
            estimator.ApplyVisibility(Event("clock", 5100, new { }), scene, null);
            bounded &= estimator.Moments == unchanged;
            estimator.ApplyVisibility(Event("clock", 5500, new { }), scene, null);
            bounded &= estimator.Moments is { Accuracy: > 0 } next && double.IsFinite(next.Accuracy) && estimator.DistinctParticleLocations() == 400;
        }
        check(finite, "Sparse, clustered and dense scenes initialize finite uncertain particle clouds with inferred scene height");
        check(bounded, "Visibility proposals respect the 500ms scan interval and retain the fixed 400-particle bound");
        File.WriteAllText(Path.Combine(temp, "visibility-scenes.json"), JsonSerializer.Serialize(samples));
    }
}
