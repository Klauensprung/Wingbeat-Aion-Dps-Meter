namespace AionDpsMeter.Local;

internal sealed record HiddenCubeDirection(int EntityId, double Bearing, string Cardinal)
{
    internal bool LastKnown {get;init;}
    internal bool Estimated {get;init;}
}
internal static class HiddenCubes
{
    // Object templates observed in the installed Questlog cube detector.
    // Interaction permission is not presence: a detected cube can be out of reach.
    // Only live world-object observations are eligible, never static map pins.
    internal static bool IsCube(WorldObjectReading value) => value.ObjectId is
        1200036 or 1200037 or 1200039 or 1200040 or 1200041 or 1200667 or
        1201502 or 1201503 or 1201504 or 1201505 or 1300081 or 1300082 or
        1300754 or 1301199 or 1301200 or 1301242 or 1301243 or 1301244 or 1301245;
    internal static HiddenCubeDirection? Nearest(IEnumerable<WorldObjectReading> objects, int map,
        CapturedPosition? position, long now)
    {
        if (position == null || map <= 0 || map != position.MapId || now < position.Timestamp || now-position.Timestamp >= 60000) return null;
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y)) return null;
        var nearest = objects.Where(IsCube).Where(o=>float.IsFinite(o.X)&&float.IsFinite(o.Y))
            .OrderBy(o=>Math.Pow(o.X-position.X,2)+Math.Pow(o.Y-position.Y,2)).ThenBy(o=>o.EntityId).FirstOrDefault();
        if(nearest==null)return null;
        double bearing=(Math.Atan2(nearest.X-position.X,position.Y-nearest.Y)*180/Math.PI+360)%360;
        string[] directions=["N","NE","E","SE","S","SW","W","NW"];
        return new(nearest.EntityId,bearing,directions[(int)Math.Floor((bearing+22.5)/45)%8]) {LastKnown=now-position.Timestamp>5000};
    }

    internal static HiddenCubeDirection? Locate(IReadOnlyList<WorldObjectReading> objects, int map,
        CapturedPosition? captured, EstimatedPosition? estimate, long now)
    {
        var direct = Nearest(objects, map, captured, now);
        if (direct is { LastKnown: false }) return direct;
        // Estimates are presentation-only; never promote them to captured coordinates.
        if (estimate is { Lost: false, Accuracy: >= 0 and <= 2500 } && estimate.MapId == map &&
            estimate.Timestamp <= now && now-estimate.Timestamp <= 5000 &&
            (captured == null || estimate.Timestamp > captured.Timestamp) &&
            double.IsFinite(estimate.X) && double.IsFinite(estimate.Y)) {
            var point = new CapturedPosition(map, estimate.X, estimate.Y, 0, estimate.Timestamp);
            var candidate = Nearest(objects, map, point, now);
            if (candidate != null) {
                var cube = objects.First(o=>o.EntityId==candidate.EntityId);
                double distance = double.Hypot(cube.X-estimate.X,cube.Y-estimate.Y);
                // Avoid a confident-looking arrow when the cube is inside the uncertainty area.
                if (distance > estimate.Accuracy*2)
                    return candidate with { Estimated=true, LastKnown=false };
            }
        }
        return direct;
    }
}
