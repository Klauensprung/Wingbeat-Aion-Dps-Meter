namespace AionDpsMeter.Local;

internal sealed record LogBoss(int NpcId,string Name,bool Defeated);
internal sealed record EncounterLogEntry(string Id,DateTime Date,string Title,string Character,string Activity,
    double Seconds,long Damage,long Healing,double Dps,double Hps,int Deaths,int Loot,LogBoss[] Bosses)
{
    public string When => Date.ToString("HH:mm");
    public string Day => Date.ToString("ddd, dd MMM");
    public string Duration => EncounterLog.Duration(Seconds);
    public string DamageRate => MeterPresentation.Number(Dps);
    public string HealingRate => MeterPresentation.Number(Hps);
    public string Outcome => Localization.T(Bosses.Length==0?Activity:Bosses.All(b=>b.Defeated)?"Boss defeated":"Recorded attempt");
}
internal sealed record EncounterLogStats(int Attempts,int Kills,double Seconds,long Damage,long Healing,int Deaths,int Loot,double BestDps);
internal static class EncounterLog
{
    internal static string Duration(double seconds) => seconds>=3600?$"{(int)(seconds/3600)}h {(int)(seconds%3600/60):00}m":$"{(int)(seconds/60)}m {(int)(seconds%60):00}s";
    internal static LogBoss[] Bosses(Fight fight){
        if(fight.ArchiveSummary is {} summary)return summary.LogBosses;
        var involved=fight.Damage.SelectMany(h=>new[]{h.ActorId,h.TargetId})
            .Concat(fight.Defense.SelectMany(h=>new[]{h.ActorId,h.TargetId})).ToHashSet();
        var deaths=fight.Deaths.Select(d=>d.EntityId).ToHashSet();
        return fight.Actors.Values.Where(a=>involved.Contains(a.Id)&&CapturedBoss.IsBoss(a))
            .Select(a=>new LogBoss(a.NpcId,BossCatalogue.Default.Find(a.NpcId)?.Name??a.Name,deaths.Contains(a.Id))).ToArray();
    }
    internal static EncounterLogEntry[] Build(IEnumerable<Fight> archive,TimeZoneInfo? zone=null){
        zone??=TimeZoneInfo.Local;
        return archive.Where(f=>!f.Demo&&SavedHistory.Includes(f)).DistinctBy(f=>f.Id).Select(f=>{
            var local=Analysis.Totals(f).FirstOrDefault(t=>t.Id==f.LocalPlayer);
            var utc=f.Start>0?DateTimeOffset.FromUnixTimeMilliseconds(f.Start).UtcDateTime:DateTime.SpecifyKind(f.Created,DateTimeKind.Utc);
            int deaths=f.ArchiveSummary?.DeathCounts.GetValueOrDefault(f.LocalPlayer)??f.Deaths.Count(d=>d.EntityId==f.LocalPlayer);
            var bosses=Bosses(f);
            string title=bosses.Length>0?string.Join(", ",bosses.Select(b=>b.Name).Distinct()):EncounterOverview.Title(f);
            return new EncounterLogEntry(f.Id,TimeZoneInfo.ConvertTimeFromUtc(utc,zone),title,
                f.Actors.GetValueOrDefault(f.LocalPlayer)?.Name??Localization.T("Unknown character"),SavedHistory.Activity(f),
                f.HasEvents?f.Seconds:0,local?.Damage??0,local?.Healing??0,local?.DPS??0,local?.HPS??0,deaths,SavedHistory.LootPickups(f),bosses);
        }).OrderByDescending(e=>e.Date).ThenBy(e=>e.Id).ToArray();
    }
    internal static EncounterLogStats Summarize(IEnumerable<EncounterLogEntry> source){
        var rows=source.DistinctBy(e=>e.Id).ToArray();
        return new(rows.Length,rows.Sum(r=>r.Bosses.Count(b=>b.Defeated)),rows.Sum(r=>r.Seconds),rows.Sum(r=>r.Damage),
            rows.Sum(r=>r.Healing),rows.Sum(r=>r.Deaths),rows.Sum(r=>r.Loot),rows.Select(r=>r.Dps).DefaultIfEmpty(0).Max());
    }
    internal static string BossKey(LogBoss boss)=>boss.NpcId>0?"npc:"+boss.NpcId:"name:"+boss.Name.ToUpperInvariant();
}
