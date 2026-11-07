namespace AionDpsMeter.Local;

internal static class PartyHealthTests
{
    internal static void Run(Action<bool,string> check) {
        check(PartyHealthEffects.ShouldBubble(new(1,"Player",70,"70%","Slow"))&&!PartyHealthEffects.ShouldBubble(new(1,"Player",70,"70%","")),"Bubbles mark any active debuff and stop when cleared");
        var low=new PartyHealthRow(1,"Player",24,"24%","");
        check(PartyHealthEffects.ShouldPulse(low)&&!PartyHealthEffects.ShouldPulse(low with{Percent=25})&&
            !PartyHealthEffects.ShouldPulse(low with{Percent=0})&&!PartyHealthEffects.ShouldPulse(low with{Percent=null})&&
            !PartyHealthEffects.ShouldPulse(low with{Range=PartyRange.Far}),"Health pulse is only for living, available players below 25 percent");
        check(PartyHealthEffects.IsPoison(new(1,12000101,"Localized name",null))&&
            !PartyHealthEffects.IsPoison(new(1,458,"Poison",null))&&!PartyHealthEffects.IsPoison(new(1,int.MaxValue,"Poison",null)),
            "Poison bubbles use known debuff identities, excluding buffs and unknown names");
        var origin = new CapturedPosition(1010, 0, 0, 0, 10000);
        var memberPosition = new SpatialEntity(2, 0, 0, 0, 10000, 3, 4, 12, 10000);
        check(PartyProximity.Describe(origin, 1010, memberPosition, 10000) == $"{13:N0} units away", "Party distance includes vertical separation for flight and different elevations");
        check(PartyProximity.Describe(origin, 1010, memberPosition, 16000) == $"Last: {13:N0} units (6s ago)",
            "Older party distances explicitly show their age instead of claiming live proximity");
        check(PartyProximity.Describe(origin, 1010, memberPosition, 41000) == "Distance unknown" &&
            PartyProximity.Describe(origin, 1110, memberPosition, 10000) == "Distance unknown" &&
            PartyProximity.Describe(origin, 1010, memberPosition with { Visible = false }, 10000) == "Distance unknown",
            "Party distance rejects stale, cross-map and despawned coordinates");
        check(PartyProximity.Range(origin,1010,memberPosition with {X=5000},10000)==PartyRange.Far,
            "Fresh party coordinates beyond 4000 units flag a distant member");
        check(PartyProximity.Range(origin,1010,memberPosition with {X=4000,Y=0,Z=0},10000)==PartyRange.Nearby,
            "Range boundary includes exactly 4000 units");
        check(PartyProximity.Range(origin,1010,memberPosition with {X=5000},16000)==PartyRange.Unknown&&PartyProximity.Range(origin,1110,memberPosition,10000)==PartyRange.Unknown,
            "Stale and cross-map positions never mark members too far");
        check(PartyProximity.Range(null,1010,memberPosition with {Visible=false},11000)==PartyRange.OutOfView&&PartyProximity.Range(null,1010,null,11000)==PartyRange.Unknown,
            "Explicit departure differs from missing position evidence");
        var fight=new Fight{LocalPlayer=1};
        for(int i=1;i<=6;i++)fight.Actors[i]=new(i,$"Member {i}",true,i<6,0,"Cleric",1);
        fight.Actors[7]=new(7,"Pet",false,true,0,"Unknown",0);
        var health=new Dictionary<int,HealthReading>{[1]=new(1000,1000,1000),[2]=new(250,1000,1000),[3]=new(249,1000,1000),[4]=new(0,1000),[5]=new(200,1000)};
        var recovery=new RecoveryPreview();
        recovery.Set(2,7,101,530,1000);recovery.Set(2,7,101,1060,2000);
        recovery.Set(2,8,102,200,2000);
        check(recovery.Snapshot(2000)[2]==1260,"Recovery replaces each effect's remaining amount and sums distinct effects");
        var recoveryRows=PartyHealthView.Build(fight,health,[],recovery.Snapshot(2000));
        check(recoveryRows.Single(r=>r.Entity==2).RecoveryPercent==75&&recoveryRows.Single(r=>r.Entity==1).RecoveryPercent==0,
            "Recovery preview is capped at missing health and absent at full HP");
        recovery.Apply(new("abnormal-remove",2100,"test",System.Text.Json.JsonSerializer.SerializeToElement(new {TargetId=2,Slot=7})));
        recovery.Set(2,7,101,999,2000);
        check(recovery.Snapshot(2100)[2]==200,"Removed regeneration cannot be restored by an older packet");
        recovery.Apply(new("abnormal-apply",2000,"test",System.Text.Json.JsonSerializer.SerializeToElement(new {TargetId=2,Slot=8,DurationMs=500})));
        check(recovery.Snapshot(2500).Count==0,"Recovery preview clears at confirmed buff expiration");
        recovery.Set(2,9,103,500,3000);
        check(recovery.Snapshot(8001).Count==0,"Missing updates cannot leave stale recovery on health bars");
        recovery.Set(2,10,104,500,9000);recovery.Set(2,10,104,0,9100);
        check(recovery.Snapshot(9100).Count==0,"Explicit zero remaining recovery clears the preview");
        var rows=PartyHealthView.Build(fight,health,[new(3,100,"Stun",2.1),new(3,101,"Poison",null)]);
        var poisonRows=PartyHealthView.Build(fight,health,[new(3,12000101,"Poison",12)]);
        check(poisonRows.Single(r=>r.Entity==3).Poisoned&&!poisonRows.Single(r=>r.Entity==2).Poisoned&&
            !PartyHealthView.Build(fight,health,[]).Single(r=>r.Entity==3).Poisoned,"Poison visual follows the affected player and clears with the debuff");
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
        unknown.Healing.Add(new(){ActorId=6,TargetId=1,Timestamp=1000,Damage=100});
        check(!PartyHealthView.HasRoster(unknown)&&PartyHealthView.Build(unknown,health,[]).Select(r=>r.Entity).SequenceEqual(new[]{1}),
            "Health mode excludes nearby damage dealers and healers while waiting for a roster");
        unknown.PartyKnown=true;
        check(PartyHealthView.Build(unknown,health,[]).Select(r=>r.Entity).SequenceEqual(new[]{1}),"Confirmed solo roster suppresses unconfirmed combat participants");
        unknown.Actors[2]=unknown.Actors[2] with {Party=true};
        check(PartyHealthView.Build(unknown,health,[]).Length==2,"Confirmed party member retains the same health row after roster arrival");
    }
}
