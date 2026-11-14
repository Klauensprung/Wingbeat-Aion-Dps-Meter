namespace AionDpsMeter.Local;

internal sealed record RadarPoint(string Key, string Name, double X, double Y, double Z, bool Enemy);
internal sealed record RadarFrame(CapturedPosition? Origin, RadarPoint[] Points);
internal static class RadarSpace
{
    internal static bool Fresh(CapturedPosition? p, long now) => p is { MapId: > 0, Timestamp: >= 0 } && p.Timestamp <= now && now - p.Timestamp <= 5000 &&
        double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);
    internal static (double Right, double Forward, double Up) Relative(RadarPoint point, CapturedPosition origin, double heading)
    {
        double dx = point.X - origin.X, dy = point.Y - origin.Y;
        return (dx * Math.Cos(heading) + dy * Math.Sin(heading), dx * Math.Sin(heading) - dy * Math.Cos(heading), point.Z - origin.Z);
    }
    internal static RadarFrame Build(Fight fight, SpatialEvidence spatial, CapturedPosition? origin, IEnumerable<WorldObjectReading> objects, int objectMap,
        IReadOnlyDictionary<int, string> materials, long now, bool showEnemies = true)
    {
        if (origin == null || !Fresh(origin, now)) return new(null, []);
        var points = new List<RadarPoint>();
        string? race = NearbyEnemies.EffectiveRace(fight.Actors.GetValueOrDefault(fight.LocalPlayer));
        if (showEnemies && origin!.MapId == spatial.MapId && race is "light" or "dark") {
            foreach (var entity in spatial.Entities) {
                var actor = fight.Actors.GetValueOrDefault(entity.Id);
                if (!entity.Visible || entity.Kind != "player" || actor is not { Player: true, OriginalEntityId: 0 } || actor.Id == fight.LocalPlayer || actor.Party ||
                    NearbyEnemies.EffectiveRace(actor) is not ("light" or "dark") || NearbyEnemies.EffectiveRace(actor) == race ||
                    entity.PositionAt < spatial.Boundary || entity.PositionAt > now || now - entity.PositionAt > 5000 ||
                    entity.X is not double x || entity.Y is not double y || entity.Z is not double z) continue;
                points.Add(new("p:" + actor.Id, actor.Name, x, y, z, true));
            }
        }
        if (origin.MapId == objectMap)
            foreach (var obj in objects)
                if (obj.CanInteract && materials.TryGetValue(obj.ObjectId, out var name)) points.Add(new("g:" + obj.EntityId, name, obj.X, obj.Y, obj.Z, false));
        return new(origin, points.Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z))
            .OrderBy(p => Math.Pow(p.X - origin.X, 2) + Math.Pow(p.Y - origin.Y, 2) + Math.Pow(p.Z - origin.Z, 2)).Take(128).ToArray());
    }
}
