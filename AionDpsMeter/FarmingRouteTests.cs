namespace AionDpsMeter.Local;
internal static class FarmingRouteTests
{
    internal static void Run(Action<bool,string> check) {
        var start=new CapturedPosition(1010,0,0,0,0);
        var points=new[]{new PetSpawnPoint(1,30,0,0),new PetSpawnPoint(2,10,0,0),new PetSpawnPoint(3,20,0,0)};
        var route=FarmingRoute.Build(points,start);
        check(route.Select(p=>p.X).SequenceEqual(new double[]{10,20,30}),"Farming route orders collinear stops from the player without backtracking");
        check(FarmingRoute.Build(points.Concat(points),start).Length==3,"Farming route merges shared resource coordinates");
        check(FarmingRoute.Build(Enumerable.Range(1,200).Select(i=>new PetSpawnPoint(i,i,0,0)),start).Length==64,"Farming route bounds planning to 64 nearest locations");
        check(FarmingRoute.Build([],start).Length==0,"Empty farming sources produce no route");
    }
}
