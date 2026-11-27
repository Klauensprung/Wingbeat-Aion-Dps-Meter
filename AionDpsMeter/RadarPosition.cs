namespace AionDpsMeter.Local;

// Presentation state only. An uncertain estimate never becomes captured evidence.
internal sealed class RadarPosition
{
    long scene = long.MinValue, relocation = long.MinValue;
    int map;
    internal CapturedPosition? Current { get; private set; }
    internal bool Estimated { get; private set; }
    internal bool HasAltitude { get; private set; } = true;
    internal double Accuracy { get; private set; }
    internal void Clear() { Current=null; Estimated=false; HasAltitude=true; Accuracy=0; map=0; scene=relocation=long.MinValue; }
    internal void Update(CapturedPosition? captured, EstimatedPosition? estimate, int mapId, long boundary,
        long sceneId, long relocationId, long now)
    {
        if (map!=mapId || scene!=sceneId || relocation!=relocationId) Clear();
        map=mapId; scene=sceneId; relocation=relocationId;
        bool measured = captured is {MapId:>0} && captured.MapId==map && captured.Timestamp>=boundary && captured.Timestamp<=now &&
            double.IsFinite(captured.X) && double.IsFinite(captured.Y) && double.IsFinite(captured.Z);
        if (measured && RadarSpace.Fresh(captured,now)) {
            Current=captured; Estimated=false; HasAltitude=true; Accuracy=0; return;
        }
        if (map>0 && estimate is {Lost:false,Accuracy:>=0 and <=2500} && estimate.MapId==map &&
            estimate.Timestamp>=boundary && estimate.Timestamp<=now && now-estimate.Timestamp<=5000 &&
            (!measured || estimate.Timestamp>captured!.Timestamp) && (Current==null || estimate.Timestamp>=Current.Timestamp) &&
            double.IsFinite(estimate.X) && double.IsFinite(estimate.Y) && (estimate.Z==null || double.IsFinite(estimate.Z.Value))) {
            // A horizontal estimate cannot establish altitude. Retain measured altitude if available;
            // otherwise render a clearly labeled horizontal scan without above/below claims.
            double? altitude=estimate.Z ?? (measured ? captured!.Z : HasAltitude ? Current?.Z : null);
            Current=new(map,estimate.X,estimate.Y,altitude??0,estimate.Timestamp);
            Estimated=true; HasAltitude=altitude!=null; Accuracy=estimate.Accuracy; return;
        }
        if (Current==null && measured) { Current=captured; Estimated=false; HasAltitude=true; }
        // Missing/poor estimates hold the last scan; only scene/relocation changes clear it.
    }
}
