namespace AionDpsMeter.Local;

internal sealed record HiddenCubeDirection(int EntityId, double Bearing, string Cardinal);
internal static class HiddenCubes
{
    // Verified live spawn templates: github.com/dr89mh/aion2-cube-radar/config/cubes.json.
    // Static map pins are possible spawn sites, not evidence that a cube is present.
    internal static bool IsCube(WorldObjectReading value) => value.ObjectId is 1200039 or 1200041 && value.CanInteract;
    internal static HiddenCubeDirection? Nearest(IEnumerable<WorldObjectReading> objects, int map,
        CapturedPosition? position, long now)
    {
        if (position == null || map <= 0 || map != position.MapId || now < position.Timestamp || now-position.Timestamp > 5000) return null;
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y)) return null;
        var nearest = objects.Where(IsCube).Where(o=>float.IsFinite(o.X)&&float.IsFinite(o.Y))
            .OrderBy(o=>Math.Pow(o.X-position.X,2)+Math.Pow(o.Y-position.Y,2)).ThenBy(o=>o.EntityId).FirstOrDefault();
        if(nearest==null)return null;
        double bearing=(Math.Atan2(nearest.X-position.X,position.Y-nearest.Y)*180/Math.PI+360)%360;
        string[] directions=["N","NE","E","SE","S","SW","W","NW"];
        return new(nearest.EntityId,bearing,directions[(int)Math.Floor((bearing+22.5)/45)%8]);
    }
}
