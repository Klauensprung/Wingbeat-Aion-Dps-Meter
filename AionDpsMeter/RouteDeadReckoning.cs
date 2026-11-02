namespace AionDpsMeter.Local;
internal sealed record RouteMovement(CapturedPosition? Position,bool Stopped,long Scene,bool Active,long Relocation=0);

// View-only dead reckoning. Does not alter captured positions or complete route stops.
internal sealed class RouteDeadReckoning
{
    CapturedPosition? anchor, held;
    long scene=long.MinValue,correctedAt;
    double vx,vy,offsetX,offsetY;
    bool active;
    readonly Queue<double> intervals=new();
    internal double ExpectedIntervalSeconds {get;private set;}=3;
    internal bool Desynced {get;private set;}
    double Horizon(double requested)=>double.IsFinite(requested)&&requested>0?requested:ExpectedIntervalSeconds;
    internal string Status {get;private set;}="Waiting for position";
    static int World(int map)=>map switch {901=>1110,902=>1010,_=>map};
    internal void Clear(){anchor=held=null;vx=vy=offsetX=offsetY=0;scene=long.MinValue;active=false;intervals.Clear();ExpectedIntervalSeconds=3;Desynced=false;}
    internal CapturedPosition? Frame(RouteMovement sample,int map,long now,double horizonSeconds=0) {
        Desynced=false;
        if(sample.Scene!=scene){Clear();scene=sample.Scene;}
        if(sample.Position is {} p&&World(p.MapId)!=World(map)){Clear();scene=sample.Scene;Status="Different map; route held";return null;}
        if(!sample.Active){
            if(active)held=Project(now,Horizon(horizonSeconds));
            vx=vy=offsetX=offsetY=0;active=false;
            Status="Capture stopped; route held";return held;
        }
        var incoming=sample.Position;
        if(incoming!=null&&double.IsFinite(incoming.X)&&double.IsFinite(incoming.Y)&&incoming.Timestamp<=now&&(anchor==null||incoming.Timestamp>anchor.Timestamp)) {
            var before=Project(now,Horizon(horizonSeconds));
            double dt=anchor==null?0:(incoming.Timestamp-anchor.Timestamp)/1000d;
            double dx=anchor==null?0:incoming.X-anchor.X,dy=anchor==null?0:incoming.Y-anchor.Y;
            bool continuous=active&&dt>=.02&&double.Hypot(dx,dy)<=10000&&double.Hypot(dx,dy)/dt<=5000;
            if(continuous){
                intervals.Enqueue(dt);if(intervals.Count>9)intervals.Dequeue();
                var sorted=intervals.Order().ToArray();int middle=sorted.Length/2;
                ExpectedIntervalSeconds=sorted.Length%2==0?(sorted[middle-1]+sorted[middle])/2:sorted[middle];
            }
            vx=continuous&&!sample.Stopped?dx/dt:0;vy=continuous&&!sample.Stopped?dy/dt:0;
            anchor=incoming;held=null;active=true;offsetX=offsetY=0;
            var next=Project(now,Horizon(horizonSeconds))!;
            if(continuous&&before!=null&&double.Hypot(before.X-next.X,before.Y-next.Y)<=5000){offsetX=before.X-next.X;offsetY=before.Y-next.Y;}
            correctedAt=now;
        }
        if(!active&&held!=null)return held;
        if(anchor==null){Status="Waiting for position; route stays visible";return null;}
        double age=Math.Max(0,now-anchor.Timestamp)/1000d;
        Desynced=age>=Horizon(horizonSeconds);
        Status=Desynced?$"Desynced - position overdue ({age:0.0}s; expected {Horizon(horizonSeconds):0.0}s)":vx==0&&vy==0?"Last position; waiting for movement":$"Predicted from movement ({age:0.0}s since update)";
        return Project(now,Horizon(horizonSeconds));
    }
    CapturedPosition? Project(long now,double horizon) {
        if(anchor==null)return null;
        double seconds=Math.Min(Math.Max(0,now-anchor.Timestamp)/1000d,horizon);
        double decay=Math.Exp(-Math.Max(0,Math.Min(now,anchor.Timestamp+horizon*1000)-correctedAt)/100d);
        return anchor with {X=anchor.X+vx*seconds+offsetX*decay,Y=anchor.Y+vy*seconds+offsetY*decay};
    }
}
