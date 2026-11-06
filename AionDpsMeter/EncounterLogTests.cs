using AionDpsMeter.Models;
namespace AionDpsMeter.Local;
internal static class EncounterLogTests
{
    internal static Fight Example(int i){
        long start=new DateTimeOffset(2026,11,6,18,0,0,TimeSpan.Zero).AddDays(-(i/4)).AddMinutes(-(i%4)*18).ToUnixTimeMilliseconds();
        string boss=new[]{"Ultimate Berk","Krao","Kaila"}[i%3];
        var f=new Fight{Id="log-test-"+i,Label=boss,LocalPlayer=1,Created=DateTimeOffset.FromUnixTimeMilliseconds(start).UtcDateTime};
        f.Actors[1]=new Actor(1,"Noctyras",true,true,12000,"Cleric",1);
        f.Actors[10]=new Actor(10,boss,false,false,0,"Unknown",0){NpcId=990000+i%3,CapturedBoss=true};
        f.Damage.Add(new DamageEvent{ActorId=1,TargetId=10,Damage=1000000+i*75000,Timestamp=start,SkillCode=17100000});
        f.Damage.Add(new DamageEvent{ActorId=1,TargetId=10,Damage=2000000+i*90000,Timestamp=start+180000+i*1000,SkillCode=17100000});
        f.Healing.Add(new DamageEvent{ActorId=1,TargetId=1,Damage=700000+i*13000,Timestamp=start+90000,IsHeal=true});
        if(i%3!=1)f.Deaths.Add(new(10,start+180000+i*1000));
        if(i%4==1)f.Deaths.Add(new(1,start+120000));
        return f;
    }
    internal static void Run(Action<bool,string> check){
        var f=Example(0);var compact=FightArchiveSummary.Create(f);
        var raw=EncounterLog.Build([f],TimeZoneInfo.Utc).Single();var saved=EncounterLog.Build([compact],TimeZoneInfo.Utc).Single();
        check(raw.Damage==saved.Damage&&raw.Healing==saved.Healing&&raw.Deaths==saved.Deaths&&raw.Bosses.SequenceEqual(saved.Bosses),"Encounter log aggregates survive archive compaction without event lists");
        check(saved.Bosses.Single().Defeated&&saved.Bosses.Single().NpcId==990000,"Encounter log preserves boss identity and observed defeat in summaries");
        var noDeath=EncounterLog.Build([Example(1)],TimeZoneInfo.Utc).Single();
        check(!noDeath.Bosses.Single().Defeated&&noDeath.Outcome==Localization.T("Recorded attempt"),"Missing boss death is an attempt, never an inferred wipe");
        var duplicate=EncounterLog.Build([f,compact],TimeZoneInfo.Utc);
        check(duplicate.Length==1&&EncounterLog.Summarize(duplicate).Damage==3000000,"Duplicate saved fight IDs do not inflate daily statistics");
        f.Demo=true;check(EncounterLog.Build([f]).Length==0,"Demo fights are excluded from real daily statistics");f.Demo=false;
        var zone=TimeZoneInfo.CreateCustomTimeZone("test",TimeSpan.FromHours(9),"test","test");
        check(EncounterLog.Build([f],zone).Single().Date.Date==new DateTime(2026,11,7),"Encounter date grouping uses the local calendar day, not the UTC day");
        var stats=EncounterLog.Summarize([raw,noDeath]);
        check(stats.Attempts==2&&stats.Kills==1&&stats.Deaths==1&&stats.Damage==6165000,"Daily stats count local damage, local deaths and observed boss defeats separately");
        check(EncounterLog.BossKey(new(100,"Same name",false))!=EncounterLog.BossKey(new(101,"Same name",false)),"Boss groups preserve distinct NPC variants even when their names match");
        check(EncounterLog.Summarize([])==new EncounterLogStats(0,0,0,0,0,0,0,0),"Empty encounter history has stable zero statistics");
    }
}
