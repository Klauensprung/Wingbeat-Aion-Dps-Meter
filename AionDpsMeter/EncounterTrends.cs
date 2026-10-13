namespace AionDpsMeter.Local;

internal sealed record EncounterTrendRow(string Encounter, int Attempts, double Average, double Latest, double Best, DateTime LastFought, string FightId);

internal static class EncounterTrends
{
    // Server is metadata, not a separate history: follow a named character and class.
    internal static TrendRow[] ForCharacter(IEnumerable<TrendRow> rows, string character, string job) => rows
        .Where(r => r.Character.Equals(character, StringComparison.OrdinalIgnoreCase) && r.Job.Equals(job, StringComparison.OrdinalIgnoreCase))
        .DistinctBy(r => r.FightId).OrderBy(r => r.Date).ThenBy(r => r.FightId).ToArray();

    internal static TrendRow[] ForEncounter(IEnumerable<TrendRow> rows, string encounter) => rows
        .Where(r => r.Encounter.Equals(encounter, StringComparison.OrdinalIgnoreCase)).OrderBy(r => r.Date).ThenBy(r => r.FightId).ToArray();

    // Each saved attempt has equal weight. DPS/HPS already use that fight's duration;
    // pooling damage and duration would give long attempts more influence on the mean.
    internal static EncounterTrendRow[] Summarize(IEnumerable<TrendRow> characterRows, bool healing) => characterRows
        .GroupBy(r => r.Encounter, StringComparer.OrdinalIgnoreCase).Select(group => {
            var attempts = group.DistinctBy(r => r.FightId).OrderBy(r => r.Date).ThenBy(r => r.FightId).ToArray();
            double Rate(TrendRow row) => healing ? row.HPS : row.DPS;
            var latest = attempts[^1];
            return new EncounterTrendRow(group.Key, attempts.Length, attempts.Average(Rate), Rate(latest), attempts.Max(Rate), latest.Date, latest.FightId);
        }).OrderByDescending(r => r.LastFought).ThenBy(r => r.Encounter, StringComparer.OrdinalIgnoreCase).ToArray();
}
