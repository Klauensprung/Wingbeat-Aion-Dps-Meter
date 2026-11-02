namespace AionDpsMeter.Local;

// Bounded nearest-neighbour tour with open-path 2-opt improvement. Coordinates only, no navigation mesh.
internal static class FarmingRoute
{
    internal static PetSpawnPoint[] Build(IEnumerable<PetSpawnPoint> source, CapturedPosition start, int limit = 64)
    {
        static double D(double x, double y, PetSpawnPoint b) => Math.Sqrt((x-b.X)*(x-b.X)+(y-b.Y)*(y-b.Y));
        var remaining = source.Where(p=>double.IsFinite(p.X)&&double.IsFinite(p.Y))
            .DistinctBy(p=>(p.X,p.Y)).OrderBy(p=>D(start.X,start.Y,p)).ThenBy(p=>p.NpcId).Take(Math.Clamp(limit,1,64)).ToList();
        var route = new List<PetSpawnPoint>(); double x=start.X,y=start.Y;
        while(remaining.Count>0) {
            var next=remaining.MinBy(p=>D(x,y,p))!;route.Add(next);remaining.Remove(next);x=next.X;y=next.Y;
        }
        for(int pass=0;pass<8;pass++) {
            bool changed=false;
            for(int i=0;i<route.Count-1;i++) for(int j=i+1;j<route.Count;j++) {
                double ax=i==0?start.X:route[i-1].X, ay=i==0?start.Y:route[i-1].Y;
                double before=D(ax,ay,route[i]),after=D(ax,ay,route[j]);
                if(j+1<route.Count){before+=D(route[j].X,route[j].Y,route[j+1]);after+=D(route[i].X,route[i].Y,route[j+1]);}
                if(after+0.01<before){route.Reverse(i,j-i+1);changed=true;}
            }
            if(!changed)break;
        }
        return route.ToArray();
    }
}
