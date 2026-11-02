namespace AionDpsMeter.Local;
internal static class RouteDeadReckoningTests
{
    internal static void Run(Action<bool,string> check) {
        var relocation=new RouteRelocation();
        var initial=new RouteMovement(new(1010,0,0,0,1000),true,1,true,1);
        check(relocation.Observe(initial)!=null,"Initial live position refreshes nearest route nodes");
        check(relocation.Observe(initial)==null,"Repeated position does not rebuild route every frame");
        var walk=initial with {Position=new(1010,200,0,0,2000),Stopped=false};
        check(relocation.Observe(walk)==null,"Ordinary walking does not reorder route stops");
        var teleport=walk with {Position=new(1010,8000,0,0,3000),Relocation=2};
        var destination=relocation.Observe(teleport);
        check(destination==teleport.Position,"Explicit teleport refreshes route even below jump threshold");
        var nodes=FarmingRoute.Build([new(1,100,0,0),new(1,8100,0,0),new(1,8200,0,0)],destination!,2);
        check(nodes.Length==2&&nodes.All(n=>n.X>=8100),"Teleport route selects nodes nearest destination rather than old route stops");
        check(relocation.Observe(teleport with {Position=new(1010,30000,0,0,4000)})!=null,"Large observed jumps also refresh nearest nodes");
        check(relocation.Observe(teleport with {Position=new(1110,100,0,0,5000),Scene=2})!=null,"New map observation refreshes destination route");
        check(relocation.Observe(teleport with {Position=new(1110,500,0,0,6000),Scene=2,Active=false})==null,"Stopped capture cannot refresh route");
        var automatic=new RouteDeadReckoning();
        long time=1000;double x=0;
        RouteMovement observation=new(new(1010,x,0,0,time),false,1,true);
        automatic.Frame(observation,1010,time);
        foreach(int interval in new[]{3000,2900,3100,12000,3000}){
            time+=interval;x+=100;
            observation=new(new(1010,x,0,0,time),false,1,true);
            automatic.Frame(observation,1010,time);
        }
        check(Math.Abs(automatic.ExpectedIntervalSeconds-3)<.001,"Measured median ignores an isolated delayed position update");
        var atLimit=automatic.Frame(observation,1010,time+3000);
        var overdue=automatic.Frame(observation,1010,time+10000);
        check(automatic.Desynced&&Math.Abs(atLimit!.X-overdue!.X)<.001&&automatic.Status.Contains("Desynced"),"Prediction freezes at the measured interval and clearly reports desync");
        time+=11000;observation=new(new(1010,x+100,0,0,time),false,1,true);
        automatic.Frame(observation,1010,time);
        check(!automatic.Desynced,"Fresh position resynchronizes overdue prediction");
        for(int i=0;i<9;i++){time+=1000;x+=100;observation=new(new(1010,x,0,0,time),false,1,true);automatic.Frame(observation,1010,time);}
        check(Math.Abs(automatic.ExpectedIntervalSeconds-1)<.001,"Rolling median adapts when position cadence changes");
        automatic.Frame(observation,1010,time+100);
        check(automatic.ExpectedIntervalSeconds==1,"Repeated render samples never enter interval statistics");
        automatic.Clear();check(automatic.ExpectedIntervalSeconds==3&&!automatic.Desynced,"Relocation clears cadence measurements and desync state");
        var predictor=new RouteDeadReckoning();
        CapturedPosition? Frame(RouteMovement s,int m,long t,double h=3)=>predictor.Frame(s,m,t,h);
        RouteMovement Sample(double x,double y,long time,bool stopped=false,long scene=1,bool active=true,int map=1010)
            =>new(new(map,x,y,0,time),stopped,scene,active);
        bool Near(double? value,double expected)=>value.HasValue&&Math.Abs(value.Value-expected)<.1;
        check(Frame(new(null,false,1,true),1010,1000)==null,"Route prediction waits for an anchor");
        Frame(Sample(0,0,1000),1010,1000);
        var moving=Sample(100,0,2000);
        Frame(moving,1010,2000);
        check(Near(Frame(moving,1010,4000)?.X,300),"Route advances between packets using the observed movement vector");
        check(Near(Frame(moving,1010,62000)?.X,400),"Explicit route prediction stops at its configured limit");
        check(Near(Frame(moving,1010,62000,2)?.X,300),"Optional prediction horizon holds at its limit");
        var turn=Sample(300,100,4000);
        var before=Frame(moving,1010,4000)!;
        check(Near(Frame(turn,1010,4000)?.X,before.X)&&Near(Frame(turn,1010,4000)?.Y,before.Y),"New route observations correct without an immediate jump");
        var turned=Frame(turn,1010,6000);
        check(Near(turned?.X,500)&&Near(turned?.Y,200),"Route follows the new movement vector after correction");
        var stop=Sample(500,200,6000,true);
        Frame(stop,1010,6000);
        check(Near(Frame(stop,1010,9000)?.X,500),"Explicit movement stop prevents further extrapolation");
        Frame(Sample(600,200,10000),1010,10000);
        var held=Frame(Sample(600,200,10000,active:false),1010,12000);
        check(Near(Frame(Sample(600,200,10000,active:false),1010,20000)?.X,held!.X)&&held.X>600,"Capture stop holds displayed position instead of snapping to old coordinates");
        check(Near(Frame(Sample(600,200,10000),1010,21000)?.X,held.X),"Resuming capture waits for a fresh observation");
        check(Near(Frame(Sample(900,200,22000),1010,22000)?.X,900),"Fresh observation reanchors after capture resumes");
        check(Frame(Sample(0,0,23000,map:1110),1010,23000)==null,"Different map invalidates route prediction");
        Frame(Sample(0,0,24000,map:902),1010,24000);
        check(Frame(Sample(0,0,24000,map:902),1010,25000)!=null,"Map aliases share route coordinates");
        check(Near(Frame(Sample(80000,0,26000),1010,26000)?.X,80000),"Teleport snaps to new anchor without predicting excessive velocity");
        check(Near(Frame(Sample(5,0,27000,scene:2),1010,27000)?.X,5),"Scene transitions discard previous velocity");
        check(Near(Frame(Sample(double.NaN,0,28000,scene:2),1010,28000)?.X,5),"Invalid positions cannot poison route drawing");
        check(Near(Frame(Sample(900,0,26000,scene:2),1010,28000)?.X,5),"Out of order observations cannot rewind route tracking");
    }
}
