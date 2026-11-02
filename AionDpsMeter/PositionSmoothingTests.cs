using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class PositionSmoothingTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var smooth = new PositionSmoothing();
        var cases = new List<object>();
        EstimatedPosition? Send(long at, PositionMoments value, bool measured = false) {
            var output = smooth.Update(1010, at, value, measured);
            cases.Add(new { At = at, Value = value, Measured = measured, Output = output }); return output;
        }
        check(Send(1000, new(10000, 20000, null, 0, 0, 3000, .5)) == null,
            "Excessively uncertain initial particles do not publish a misleading center");
        var first = Send(1100, new(10000, 20000, null, 0, 0, 500, .5));
        var held = Send(1500, new(10100, 20100, 100, 200, 100, 300, .8));
        check(held?.X == first?.X && held?.Y == first?.Y && held?.Z == null && held?.Accuracy is > 300 and < 500,
            "Stationary jitter holds the displayed center while uncertainty changes gradually");
        var moving = Send(2500, new(14000, 23000, 100, 400, 200, 800, .2));
        check(moving?.X is > 10000 and < 14000 && moving.Y is > 20000 and < 23000 && moving.Z == 100,
            "Moving estimates blend velocity and limit coordinate correction instead of snapping");
        var broad = Send(3000, new(50000, 60000, 200, 1000, 1000, 3000, .1));
        check(broad?.X == moving?.X && broad?.Y == moving?.Y && broad?.Accuracy == 3000,
            "Broad uncertainty retains the last displayed center rather than jumping to an unreliable mean");
        var measured = Send(3100, new(17000, 27000, 120, 0, 0, 70, 1), true);
        check(measured is { X: 17000, Y: 27000, Z: 120, Accuracy: 70 }, "Direct measurements bypass output smoothing");
        Send(4100, new(19000, 29000, 180, 200, 300, 400, .1));
        Send(4200, new(19000, 29000, null, 200, 300, 400, .1));
        File.WriteAllText(Path.Combine(temp, "position-smoothing.json"), JsonSerializer.Serialize(cases));
        smooth.Clear(); check(smooth.Update(1110, 5000, new(0, 0, null, 0, 0, 3000, 0), false) == null,
            "Map reset clears the previous smoothed center");
    }
}
