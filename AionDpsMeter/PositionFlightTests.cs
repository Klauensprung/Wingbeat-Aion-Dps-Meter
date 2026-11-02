using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class PositionFlightTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var random = new VisibilitySceneTests.ComparisonRandom();
        var estimator = new PositionEstimator(random, random.Normal); estimator.Clear(1010);
        NetworkEvent Resource(long at, int entity, object values) => new("resources", at, "test", JsonSerializer.SerializeToElement(new { EntityId = entity, Values = values }));
        var samples = new List<object>();
        void Step(long at, long? fp = null, bool position = false) {
            if (position) estimator.Observe(new(1010, 10000, 20000, 100, at));
            else if (fp != null) estimator.ApplyFlight(Resource(at, 1, new Dictionary<int, long> { [6] = fp.Value }), 1);
            else estimator.Advance(at);
            samples.Add(new { At = at, X = position ? (double?)10000 : null, Y = position ? (double?)20000 : null,
                Z = position ? (double?)100 : null, Fp = fp, Output = estimator.Current, Moments = estimator.Moments });
        }
        Step(1000, position: true); Step(1000, 100); Step(1500, 90); Step(2000); Step(3000); Step(3100);
        Step(3500, 80); Step(4000, 85); Step(5000); Step(5100); Step(7000);
        var prior = estimator.Moments;
        estimator.ApplyFlight(Resource(7100, 2, new Dictionary<int, long> { [6] = 0 }), 1);
        estimator.ApplyFlight(Resource(7100, 1, new Dictionary<int, long> { [13] = 100 }), 1);
        estimator.ApplyFlight(Resource(6000, 1, new Dictionary<int, long> { [6] = 0 }), 1);
        check(estimator.Moments == prior, "Other players, maximum-only flight updates and stale resources cannot alter the local flight motion state");
        check(estimator.Moments is { Accuracy: > 0, Z: not null } current && double.IsFinite(current.Z.Value), "Flight consumption and expiration preserve finite probabilistic height");
        File.WriteAllText(Path.Combine(temp, "position-flight.json"), JsonSerializer.Serialize(samples));
    }
}
