using System.Text.Json;
namespace AionDpsMeter.Local;
internal static class RadarSpaceTests
{
    internal static void Run(Action<bool,string> check)
    {
        var origin = new CapturedPosition(1010,100,200,300,10000);
        var east = new RadarPoint("e","Enemy",1100,200,600,true);
        var north = RadarSpace.Relative(east,origin,0);
        var forward = RadarSpace.Relative(east,origin,Math.PI/2);
        check(north == (1000d,0d,300d) && Math.Abs(forward.Right)<.001 && Math.Abs(forward.Forward-1000)<.001 && forward.Up==300,
            "3D radar rotates horizontal direction without changing world altitude");
        check(!RadarSpace.Fresh(origin,16000) && !RadarSpace.Fresh(origin,9999) && !RadarSpace.Fresh(origin with{Z=double.NaN},10000),
            "3D radar rejects stale, future and invalid origin coordinates");
        MapMarkerGroup[] groups=[new(0,1010,"Ore","","",1,"gatherable",10,77),new(1,1110,"Ore","","",1,"gatherable",10,77),new(2,1010,"Plant","","",1,"gatherable",20,88),new(3,1010,"NPC","","",1,"npc",30,0)];
        var options=new Preferences();
        check(RadarTracking.Materials(options,[new("gatherable",10)],groups,[]).Keys.SequenceEqual(new[]{77}),"Radar tracking follows farming selections and deduplicates world catalogue entries");
        options.RadarMaterialMode="All";
        check(RadarTracking.Materials(options,[],groups,[]).Count==2,"All-material radar includes only catalogue-identified gathering object types");
        options.RadarMaterialMode="Custom";options.RadarMaterialObjects=[88];
        check(RadarTracking.Materials(options,[new("gatherable",10)],groups,[]).Keys.SequenceEqual(new[]{88}),"Custom radar material selection is independent of farming targets");
        options.RadarShowMaterials=false;
        check(RadarTracking.Materials(options,[],groups,[]).Count==0&&options.RadarMaterialObjects.SequenceEqual(new[]{88}),"Disabling materials retains the custom selection");
        var fight=new Fight{LocalPlayer=1};var spatial=new SpatialEvidence();
        void Send(string kind,long at,object fields)=>spatial.Apply(new(kind,at,"test",JsonSerializer.SerializeToElement(fields)),1);
        Send("local-zone",1,new{MapId=1010});
        for(int i=1;i<=4;i++){
            fight.Actors[i]=new(i,"Player "+i,true,i==4,0,"Cleric",1){Race=i<=2?"light":"dark"};
            Send("actor-position",10000,new{ActorId=i,X=200,Y=300,Z=400,EntityKind="player",VisibilityReason=1});
        }
        WorldObjectReading[] objects=[new(20,77,500,600,700,0,true,0,0,0,0),new(21,88,0,0,0,0,true,0,0,0,0)];
        var materials=new Dictionary<int,string>{{77,"Ore"}};
        var frame=RadarSpace.Build(fight,spatial,origin,objects,1010,materials,10000);
        check(RadarSpace.Build(fight,spatial,origin,objects,1010,materials,10000,false).Points.All(p=>!p.Enemy),"Radar enemy toggle excludes player markers without disabling materials");
        check(RadarSpace.Build(fight,spatial,origin,[],1010,materials,10000).Points.All(p=>p.Enemy),"Catalogue matches alone never create theoretical gathering spawn markers");
        check(RadarSpace.Build(fight,spatial,origin,objects.Select(o=>o with{CanInteract=false}),1010,materials,10000).Points.All(p=>p.Enemy),"Radar excludes captured materials that cannot be interacted with");
        var tracker = new WorldObjectTracker();
        tracker.Apply(new("local-zone",1,"test",JsonSerializer.SerializeToElement(new{MapId=1010})));
        tracker.Apply(new("world-object",9000,"test",JsonSerializer.SerializeToElement(objects[0])));
        tracker.Apply(new("world-object-removed",10000,"test",JsonSerializer.SerializeToElement(new WorldObjectRemoval(20,0,0,0))));
        check(RadarSpace.Build(fight,spatial,origin,tracker.Snapshot(),tracker.MapId,materials,10000).Points.All(p=>p.Enemy),"Removed gathering objects disappear from radar despite remaining in the material catalogue");
        check(frame.Points.Length==2 && frame.Points.Single(p=>p.Enemy).Key=="p:3" && frame.Points.Single(p=>!p.Enemy).Z==700,
            "3D radar includes only hostile players and selected interactable materials with real height");
        check(RadarSpace.Build(fight,spatial,origin with{MapId=1110},objects,1010,materials,10000).Points.Length==0,
            "3D radar never mixes positions from different maps");
        check(RadarSpace.Build(fight,spatial,origin,objects,1010,materials,60000) is { Origin:not null, Points.Length:2 },
            "Packet gaps retain the last player position and observed enemy/material scan");
        Send("entity-removed",61000,new{EntityId=3});
        check(RadarSpace.Build(fight,spatial,origin,objects,1010,materials,61000).Points.All(p=>!p.Enemy),
            "Explicit enemy removal still clears a retained radar marker");
        var motion=new RadarMotion();
        RadarFrame Sample(long at,double x,bool stopped=false,long scene=1,long relocation=1) {
            var p=origin with {Timestamp=at,X=x};
            return new(p,frame.Points){Movement=new(p,stopped,scene,true,relocation)};
        }
        motion.Present(Sample(10000,100),10000);
        motion.Present(Sample(16000,700),16000);
        var predicted=motion.Present(Sample(16000,700),19000);
        check(predicted.Origin is {X:>990 and <1010} && predicted.Points.Length==2 && !motion.Desynced,
            "Radar predicts across a six-second measured network interval without losing the scan");
        var frozen=motion.Present(Sample(16000,700),23000);
        var held=motion.Present(Sample(16000,700),90000);
        check(motion.Desynced && frozen.Origin==held.Origin && held.Origin is {X:>1290 and <1310},
            "Overdue radar prediction freezes at the median interval and retains its last scan");
        var resumed=motion.Present(Sample(24000,1500),24000);
        check(resumed.Origin is {X:>1290 and <1310},"A late packet smoothly corrects the held radar position instead of snapping");
        motion.Present(Sample(26000,1600,true),26000);
        var stopped=motion.Present(Sample(26000,1600,true),27000);
        check(stopped.Origin is {X:>1599 and <1601},"An explicit stop disables radar movement prediction");
        var teleport=motion.Present(Sample(28000,5000,true,relocation:2),28000);
        check(teleport.Origin?.X==5000,"Same-map teleport resets radar prediction immediately");
        motion.Present(new(null,[]),29000);
        check(motion.Present(Sample(30000,8000,scene:2),30000).Origin?.X==8000,
            "Scene changes discard previous radar motion");
        fight.Actors[1]=fight.Actors[1] with{Race=null};
        check(RadarSpace.Build(fight,spatial,origin,objects,1010,materials,10000).Points.All(p=>!p.Enemy),
            "3D radar does not classify unknown factions as enemies");
        check(RadarSpace.Build(fight,spatial,origin,Enumerable.Range(1,500).Select(i=>objects[0] with{EntityId=i,X=i}),1010,materials,10000).Points.Length==128,
            "3D radar bounds retained render markers to 128");
    }
}
