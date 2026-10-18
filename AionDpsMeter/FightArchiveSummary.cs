namespace AionDpsMeter.Local;

// Archive entries keep aggregates, never the event lists/JsonDocuments of every
// encounter. Full detail remains in the fight file and is loaded on selection.
internal sealed record ArchiveActorStats(Dictionary<int, (long Amount, int Hits)> Skills,
    int Hits, double OpeningDps, long DamageTaken, OpenerRow? First);

internal sealed record FightArchiveSummary(long Start, long End, bool HasEvents, bool HasLocalDamage,
    bool HasPvp, int Events, string Bosses, string[] Names, string[] PlayerNames, TotalRow[] Totals, Dictionary<int, ArchiveActorStats> Stats)
{
    internal static Fight Create(Fight source)
    {
        if (source.ArchiveSummary != null) return source;
        var damage = source.Damage.ToLookup(h => h.ActorId);
        var healing = source.Healing.ToLookup(h => h.ActorId);
        var taken = source.Damage.GroupBy(h => h.TargetId).ToDictionary(g => g.Key, g => g.Sum(h => (long)h.Damage));
        var totals = Analysis.Totals(source);
        var stats = new Dictionary<int, ArchiveActorStats>();
        foreach (var row in totals.Where(r => source.Actors.GetValueOrDefault(r.Id)?.Player == true))
        {
            var hits = damage[row.Id];
            var first = hits.Select(h => (Hit: h, Healing: false)).Concat(healing[row.Id].Select(h => (Hit: h, Healing: true)))
                .OrderBy(h => h.Hit.Timestamp).FirstOrDefault();
            OpenerRow? opener = first.Hit == null ? null : new(row.Id, source.Name(row.Id), 1, 0,
                first.Healing ? "Heal" : "Damage", "", first.Hit.SkillCode, source.Name(first.Hit.TargetId), first.Hit.Damage, first.Hit.IsCritical);
            stats[row.Id] = new(hits.GroupBy(h => DetailedAnalysis.Family(h.SkillCode))
                .ToDictionary(g => g.Key, g => (g.Sum(h => (long)h.Damage), g.Count())), hits.Count(),
                first.Hit == null ? 0 : hits.Where(h => h.Timestamp - first.Hit.Timestamp < 15000).Sum(h => (long)h.Damage) / Math.Min(15, source.Seconds),
                taken.GetValueOrDefault(row.Id), opener);
        }
        return new Fight {
            Version = source.Version, Id = source.Id, Label = source.Label, Created = source.Created, Demo = source.Demo,
            LocalPlayer = source.LocalPlayer,
            // The live identity dictionary includes everything seen in earlier
            // fights. Only participants need full identity metadata in history.
            Actors = source.Actors.Where(p => p.Value.Player && (stats.ContainsKey(p.Key) || source.Builds.ContainsKey(p.Key))).ToDictionary(),
            Builds = new(source.Builds), Specs = new(source.Specs),
            ArchiveSummary = new(source.Start, source.End, source.HasEvents, EncounterOverview.HasLocalDamage(source),
                PvpAnalysis.Build(source).HasOpponents, source.Damage.Count + source.Healing.Count, BossAnalysis.Summary(source),
                source.Actors.Values.Select(a => a.Name).Distinct().ToArray(),
                source.Actors.Values.Where(a => a.Player).Select(a => a.Name).Distinct().ToArray(), totals, stats)
        };
    }
}
