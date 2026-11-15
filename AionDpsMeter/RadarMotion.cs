namespace AionDpsMeter.Local;

// Presentation only: share the map's measured-interval prediction and correction
// smoothing. Never write predicted coordinates back into capture or enemy data.
internal sealed class RadarMotion
{
    readonly RouteDeadReckoning prediction = new();
    RouteRelocation relocation = new();
    internal bool Desynced => prediction.Desynced;
    internal void Clear() { prediction.Clear(); relocation = new(); }
    internal RadarFrame Present(RadarFrame frame,long now)
    {
        if(frame.Origin == null) { Clear(); return frame; }
        if(frame.Movement is not {} movement) return frame;
        if(relocation.Observe(movement) != null) prediction.Clear();
        var position = prediction.Frame(movement,frame.Origin.MapId,now);
        // Z stays at its last measured altitude, as in the gathering map.
        return frame with { Origin = position ?? frame.Origin };
    }
}
