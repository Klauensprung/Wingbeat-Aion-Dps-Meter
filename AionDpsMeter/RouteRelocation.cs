namespace AionDpsMeter.Local;

// Only observed positions can refresh route stops; extrapolated movement never does.
internal sealed class RouteRelocation
{
    internal static bool Fresh(RouteMovement sample,long now)=>sample.Active&&sample.Position is {} p&&p.MapId>0&&
        double.IsFinite(p.X)&&double.IsFinite(p.Y)&&p.Timestamp<=now&&now-p.Timestamp<=30000;
    RouteMovement? previous;
    internal CapturedPosition? Observe(RouteMovement sample) {
        if(!sample.Active||sample.Position is not {} p||!double.IsFinite(p.X)||!double.IsFinite(p.Y))return null;
        var old=previous;
        if(old?.Position is {} last&&sample.Scene==old.Scene&&p.Timestamp<=last.Timestamp)return null;
        previous=sample;
        bool relocated=old?.Position is not {} prior||sample.Scene!=old.Scene||sample.Relocation!=old.Relocation||
            World(p.MapId)!=World(prior.MapId)||double.Hypot(p.X-prior.X,p.Y-prior.Y)>10000;
        return relocated?p:null;
    }
    internal static int World(int map)=>map switch {901=>1110,902=>1010,_=>map};
}
