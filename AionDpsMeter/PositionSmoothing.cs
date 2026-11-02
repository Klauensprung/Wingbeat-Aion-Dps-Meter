namespace AionDpsMeter.Local;

internal sealed record PositionMoments(double X, double Y, double? Z, double Vx, double Vy, double Accuracy, double Still);

// Smooth presentation only; never feeds smoothed coordinates back into particles.
internal sealed class PositionSmoothing
{
    PositionMoments? previous;
    long at;
    internal void Clear() { previous = null; at = 0; }
    internal EstimatedPosition? Update(int map, long timestamp, PositionMoments value, bool measured)
    {
        if (previous != null && timestamp < at) return Result(map);
        if (measured) { previous = value; at = timestamp; return Result(map); }
        if (value.Accuracy > 2500) {
            if (previous != null) { previous = previous with { Vx = 0, Vy = 0, Accuracy = value.Accuracy }; at = timestamp; }
            return Result(map);
        }
        if (previous == null) { previous = value; at = timestamp; return Result(map); }
        double elapsed = timestamp - at, blend = 1 - Math.Exp(-elapsed / 2000);
        var old = previous;
        if (value.Still >= .6 && double.Hypot(value.X - old.X, value.Y - old.Y) <= Math.Max(value.Accuracy, 1000)) {
            previous = old with { Vx = 0, Vy = 0, Accuracy = old.Accuracy + (value.Accuracy - old.Accuracy) * blend };
        } else {
            double velocityBlend = 1 - Math.Exp(-elapsed / 1000);
            double vx = old.Vx + (value.Vx - old.Vx) * velocityBlend, vy = old.Vy + (value.Vy - old.Vy) * velocityBlend;
            double x = old.X + vx * elapsed / 1000, y = old.Y + vy * elapsed / 1000;
            double dx = (value.X - x) * blend, dy = (value.Y - y) * blend;
            double correction = Math.Min(1, 1200 * elapsed / 1000 / Math.Max(1, double.Hypot(dx, dy)));
            double? z = value.Z is double height && old.Z is double oldHeight ? oldHeight + (height - oldHeight) * blend : value.Z;
            previous = new(x + dx * correction, y + dy * correction, z, vx, vy,
                old.Accuracy + (value.Accuracy - old.Accuracy) * blend, value.Still);
        }
        at = timestamp; return Result(map);
    }
    EstimatedPosition? Result(int map) => previous is { } p ? new(map, p.X, p.Y, p.Z, at, p.Accuracy, false) : null;
}
