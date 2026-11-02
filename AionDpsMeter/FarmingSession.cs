namespace AionDpsMeter.Local;
internal sealed record FarmingSessionSummary(long? KillsSince,long? SoulsSince,decimal Kills,decimal KillsPerHour,decimal Souls,bool HasOmissions);
internal static class FarmingSession
{
    internal static FarmingSessionSummary Build(FarmingCounterSnapshot? kills,FarmingCounterSnapshot? souls,long now) {
        decimal count=kills?.Counts.Values.Sum(n=>(decimal)n)??0;
        decimal hours=kills==null?1:Math.Max(1m/60,((decimal)now-kills.Since)/3600000);
        return new(kills?.Since,souls?.Since,count,decimal.Floor(count/hours+.5m),souls?.Counts.Values.Sum(n=>(decimal)n)??0,
            kills?.Omitted>0||souls?.Omitted>0);
    }
}
