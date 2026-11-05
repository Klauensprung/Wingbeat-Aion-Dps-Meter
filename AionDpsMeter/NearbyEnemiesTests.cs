using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class NearbyEnemiesTests
{
    internal static void Run(Action<bool,string> check)
    {
        var fight=new Fight{LocalPlayer=1};var spatial=new SpatialEvidence();
        void Send(string kind,long at,object fields)=>spatial.Apply(new(kind,at,"test",JsonSerializer.SerializeToElement(fields)),1);
        Send("local-zone",1,new{MapId=1010});
        for(int i=1;i<=5;i++){
            fight.Actors[i]=new(i,"Player "+i,true,false,20000,"Cleric",1){Race=i<=2?"light":i==5?null:"dark"};
            Send("actor-position",10000,new{ActorId=i,X=3,Y=4,Z=12,EntityKind="player",VisibilityReason=1});
        }
        fight.Actors[4]=fight.Actors[4] with{Party=true};
        var origin=new CapturedPosition(1010,0,0,0,10000);
        var rows=NearbyEnemies.Build(fight,spatial,origin,10000);
        check(rows.Single().HeightDifference==12&&rows.Single().HeightArrow=="","Small height differences leave enemy arrows hidden");
        check(NearbyEnemies.Build(fight,spatial,origin with{Z=-300},10000).Single().HeightArrow=="\u2191"&&
            NearbyEnemies.Build(fight,spatial,origin with{Z=300},10000).Single().HeightArrow=="\u2193","Enemy arrows compare captured vertical position in both directions");
        check(NearbyEnemies.Build(fight,spatial,origin,16000).Single().HeightDifference==null&&
            NearbyEnemies.Build(fight,spatial,origin with{MapId=1110},10000).Single().HeightArrow=="","Stale or cross-map positions cannot produce height arrows");
        fight.Actors[4]=fight.Actors[4] with{Party=false,Name=fight.Actors[3].Name};
        Send("actor-position",10000,new{ActorId=4,X=300,Y=400,Z=12,EntityKind="player",VisibilityReason=1});
        fight.LocalTarget=new(4,10000);
        check(NearbyEnemies.Build(fight,spatial,origin,10000) is [{Id:4,Targeted:true},{Id:3,Targeted:false}],"Current enemy target sorts first by entity ID even when farther away and names match");
        fight.LocalTarget=new(0,10000);
        check(NearbyEnemies.Build(fight,spatial,origin,10000) is [{Id:3,Targeted:false},{Id:4,Targeted:false}],"Clearing target restores distance order and removes highlight");
        fight.LocalTarget=new(2,10000);
        check(NearbyEnemies.Build(fight,spatial,origin,10000).All(e=>!e.Targeted),"Friendly targets cannot highlight an enemy row");
        fight.LocalTarget=new(4,0);
        check(NearbyEnemies.Build(fight,spatial,origin,10000).All(e=>!e.Targeted),"Previous-zone target evidence does not pin an enemy");
        fight.LocalTarget=null;fight.Actors[4]=fight.Actors[4] with{Party=true};
        fight.Network.Add(new("hp",10000,"test",JsonSerializer.SerializeToElement(new{EntityId=3,CurrentHp=250,MaximumHp=1000})));
        check(NearbyEnemies.Build(fight,spatial,origin,10000) is [{HealthPercent:25,LocalCp:20000}],"Enemy rows project captured HP percentage and local CP for the existing comparison colors");
        fight.Network.Add(new("entity-revived",10001,"test",JsonSerializer.SerializeToElement(new{EntityId=3})));
        check(NearbyEnemies.Build(fight,spatial,origin,10001) is [{HealthPercent:null}],"Enemy revival without captured HP does not fabricate a health bar");
        check(rows is [{Id:3,Cp:20000,Distance:13}],"Nearby enemies exclude self, allies, party and unknown factions, preserving CP and 3D distance");
        check(NearbyEnemies.Build(fight,spatial,origin,16000) is [{Distance:null}],"Stale coordinates retain enemy identity without claiming a live distance");
        check(NearbyEnemies.Build(fight,spatial,origin with{MapId=1110},10000) is [{Distance:null}],"Nearby enemy distance rejects coordinates from another map");
        fight.Damage.Add(new(){ActorId=2,TargetId=3,Damage=100,Timestamp=10002});
        check(NearbyEnemies.Build(fight,spatial,origin,10002) is [{Tagged:false}],"Other players' damage does not tag an enemy for you");
        fight.Damage.Add(new(){ActorId=1,TargetId=3,Damage=100,Timestamp=10003});
        check(NearbyEnemies.Build(fight,spatial,origin,10003) is [{Tagged:true}],"Your damage tags a player without requiring a killing blow");
        fight.Deaths.Add(new(3,10004));
        check(NearbyEnemies.Build(fight,spatial,origin,10004) is [{Tagged:false}],"A death clears the previous life's tag");
        fight.Damage.Add(new(){ActorId=1,TargetId=3,Damage=100,Timestamp=10005});
        check(NearbyEnemies.Build(fight,spatial,origin,10005) is [{Tagged:true}],"New damage can tag an enemy again");
        fight.Network.Add(new("entity-revived",10006,"test",JsonSerializer.SerializeToElement(new{EntityId=3})));
        check(NearbyEnemies.Build(fight,spatial,origin,10006) is [{Tagged:false}],"Revival clears the earlier tag even if death was missed");
        fight.Actors[1]=fight.Actors[1] with{Race=null};
        check(NearbyEnemies.Build(fight,spatial,origin,10000).Length==0,"Unknown local faction does not classify everyone as hostile");
        fight.Actors[1]=fight.Actors[1] with{Race="light"};
        fight.Actors[1]=fight.Actors[1] with{Race="dark",OverrideRace=2};
        fight.Actors[2]=fight.Actors[2] with{Race="dark",OverrideRace=1};
        check(NearbyEnemies.Build(fight,spatial,origin,10000) is [{Id:2}],"Abyss override identifies opposing teams despite matching base factions");
        fight.Actors[2]=fight.Actors[2] with{OverrideRace=0};
        check(NearbyEnemies.Build(fight,spatial,origin,10000).Length==0,"Explicit zero override restores the base faction");
        check(NearbyEnemies.EffectiveRace(fight.Actors[2] with{OverrideRace=3})==null,"Unknown override is not guessed to be an enemy");
        fight.Actors[1]=fight.Actors[1] with{Race="light",OverrideRace=0};
        fight.Actors[2]=fight.Actors[2] with{Race="light"};
        Send("entity-removed",11000,new{EntityId=3,Reason=1});
        check(NearbyEnemies.Build(fight,spatial,origin,11000).Length==0,"Departed enemy players leave the nearby list");
        Send("local-zone",12000,new{MapId=1110});
        check(NearbyEnemies.Build(fight,spatial,origin,12000).Length==0,"Teleport clears the old nearby enemy list");
    }
}
