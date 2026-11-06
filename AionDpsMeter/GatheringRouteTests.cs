namespace AionDpsMeter.Local;

internal static class GatheringRouteTests
{
    internal static void Run(Action<bool,string> check){
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        RouteMovement sample=new(new(1010,20,30,0,now),false,1,true);
        check(RouteRelocation.Fresh(sample,now)&&!RouteRelocation.Fresh(sample with {Active=false},now)&&!RouteRelocation.Fresh(sample,now+30001)&&!RouteRelocation.Fresh(sample with {Position=null},now),"Gathering start requires a fresh captured location and active capture");
        var relocation=new RouteRelocation();relocation.Observe(sample);
        var teleported=sample with {Position=new(1010,90000,0,0,now+100),Relocation=1};
        var origin=relocation.Observe(teleported);
        var route=FarmingRoute.Build([new(1,10,0,0),new(2,90010,0,0)],origin!,1);
        check(origin!=null&&route.Single().NpcId==2,"Teleport route starts at nearest destination node, not old location");
        check(MapMarkers.Default.Groups.Any(g=>g.DatabaseType=="gatherable"&&ItemFarmLinks.Default.Items.Any(i=>i.Gatherables.Contains(g.DatabaseId))),"Material catalogue joins to gatherable map locations by ID");
    }
}
