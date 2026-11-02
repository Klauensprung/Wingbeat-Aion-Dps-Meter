using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class PositionMotionTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var random = new VisibilitySceneTests.ComparisonRandom();
        var estimator = new PositionEstimator(random, random.Normal); estimator.Clear(1010);
        var samples = new List<object>();
        void Step(long timestamp, double? x = null, double? y = null, double? z = null) {
            if (x != null) estimator.Observe(new(1010, x.Value, y!.Value, z!.Value, timestamp));
            else estimator.Advance(timestamp);
            samples.Add(new { At = timestamp, X = x, Y = y, Z = z, Output = estimator.Current, Moments = estimator.Moments });
        }
        Step(1000, 10000, 20000, 100);
        check(estimator.Moments is { Still: > .4 and < .6 }, "A first measured position preserves uncertainty about whether the player is stationary");
        Step(1100); Step(1500, 10400, 20200, 110);
        check(estimator.Moments is { Still: > .05 and < .2, Vx: > 700 and < 900, Vy: > 300 and < 500 },
            "Successive moving positions initialize velocity with spread and predominantly moving particles");
        Step(1600); Step(2500); Step(4500);
        Step(8500, 12000, 23000, 120);
        check(estimator.Moments is { Still: > .4 and < .6, Vx: 0, Vy: 0 }, "Positions more than five seconds apart do not infer an unsupported velocity");
        Step(9000, 12000, 23000, 120);
        check(estimator.Moments is { Still: > .999 }, "Repeated unchanged measured positions initialize stationary particles");
        Step(10500); Step(15000);
        check(estimator.Moments is { Accuracy: > 0 } moments && double.IsFinite(moments.Accuracy), "Multi-step propagation keeps finite uncertainty");
        Step(315001);
        check(estimator.Current is { Lost: true, Timestamp: 15000 } && estimator.Moments == null,
            "A long network gap invalidates particles while retaining the last actual output timestamp");
        Step(315100, 13000, 24000, 130);
        check(estimator.Current is { Lost: false, Timestamp: 315100 }, "A new measured position recovers after a network gap");
        File.WriteAllText(Path.Combine(temp, "position-motion.json"), JsonSerializer.Serialize(samples));
    }
}
