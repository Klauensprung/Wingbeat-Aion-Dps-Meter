namespace AionDpsMeter.Local;

internal static class PartyHealthTests
{
    internal static void Run(Action<bool,string> check) {
        var fight=new Fight{LocalPlayer=1};
        for(int i=1;i<=6;i++)fight.Actors[i]=new(i,$"Member {i}",true,i<6,0,"Cleric",1);
        fight.Actors[7]=new(7,"Pet",false,true,0,"Unknown",0);
        var health=new Dictionary<int,HealthReading>{[1]=new(1000,1000,1000),[2]=new(250,1000,1000),[3]=new(249,1000,1000),[4]=new(0,1000),[5]=new(200,1000)};
        var rows=PartyHealthView.Build(fight,health,[new(3,100,"Stun",2.1),new(3,101,"Poison",null)]);
        check(rows.Length==5&&rows[0].Entity==1,"Party health includes idle members and local player but excludes nearby outsiders and pets");
        check(rows[1] is {Percent:25,Critical:true}&&rows[2] is {Percent:24.9,Critical:true},"Party health is critical at 25 percent and below");
        check(rows[3] is {Percent:0,Health:"Dead",Critical:true}&&rows[4] is {Percent:null,Health:"HP unknown",Critical:false},"Dead and unknown maximum HP remain distinct");
        check(rows[2].Debuffs.Contains("Stun 3s")&&rows[2].Debuffs.Contains("Poison")&&rows[0].Debuffs=="","Debuffs are attached to their affected member and do not invent unknown durations");
        fight.Actors[3]=fight.Actors[3] with {Party=false};
        check(PartyHealthView.Build(fight,health,[]).All(r=>r.Entity!=3),"Departed party members disappear from health mode");
        var unknown=new Fight{LocalPlayer=1};
        unknown.Actors[1]=fight.Actors[1];unknown.Actors[2]=fight.Actors[2] with {Party=false};
        unknown.Actors[6]=fight.Actors[6];
        unknown.Damage.Add(new(){ActorId=2,TargetId=99,Timestamp=1000,Damage=100});
        check(!PartyHealthView.HasRoster(unknown)&&PartyHealthView.Build(unknown,health,[]).Select(r=>r.Entity).SequenceEqual(new[]{1,2}),
            "Health mode includes DPS participants before the roster arrives, without including every nearby player");
        unknown.PartyKnown=true;
        check(PartyHealthView.Build(unknown,health,[]).Select(r=>r.Entity).SequenceEqual(new[]{1}),"Confirmed solo roster suppresses unconfirmed combat participants");
        unknown.Actors[2]=unknown.Actors[2] with {Party=true};
        check(PartyHealthView.Build(unknown,health,[]).Length==2,"Confirmed party member retains the same health row after roster arrival");
    }
}
