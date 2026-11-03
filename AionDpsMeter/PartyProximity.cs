namespace AionDpsMeter.Local;

internal static class PartyProximity
{
    internal static string Describe(CapturedPosition? local, int map, SpatialEntity? member, long now)
    {
        bool Fresh(long time) => time >= 0 && time <= now && now - time <= 30000;
        if (local == null || local.MapId != map || !Fresh(local.Timestamp) || member is not { Visible: true, X: not null, Y: not null, Z: not null } || !Fresh(member.PositionAt))
            return "Distance unknown";
        double distance = Math.Sqrt(Math.Pow(member.X.Value - local.X, 2) + Math.Pow(member.Y.Value - local.Y, 2) + Math.Pow(member.Z.Value - local.Z, 2));
        if (!double.IsFinite(distance)) return "Distance unknown";
        long age = Math.Max(now - local.Timestamp, now - member.PositionAt) / 1000;
        return age <= 5 ? $"{distance:N0} units away" : $"Last: {distance:N0} units ({age}s ago)";
    }
}

public sealed partial class MainWindow
{
    Dictionary<int, string> PartyDistances(Fight displayed)
    {
        var result = new Dictionary<int, string>();
        if (capture == null) return result;
        lock (engine.Gate) {
            if (displayed.Id != engine.Fight.Id) return result;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (var member in engine.Fight.Actors.Values.Where(a => a.Player && a.Party && a.OriginalEntityId == 0 && a.Id != engine.Fight.LocalPlayer))
                result[member.Id] = PartyProximity.Describe(engine.PartyPosition.RelativeLatest, engine.Spatial.MapId, engine.Spatial.Find(member.Id), now);
        }
        return result;
    }
}
