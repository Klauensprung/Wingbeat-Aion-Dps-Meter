namespace AionDpsMeter.Local;

// Archive entries keep aggregates, never the event lists/JsonDocuments of every
// encounter. Full detail remains in the fight file and is loaded on selection.
internal sealed record ArchiveActorStats(Dictionary<int, (long Amount, int Hits)> Skills,
    int Hits, double OpeningDps, long DamageTaken, OpenerRow? First);

internal sealed record FightArchiveSummary(long Start, long End, bool HasEvents, bool HasLocalDamage,
    bool HasPvp, int Events, string Bosses, string[] Names, string[] PlayerNames, TotalRow[] Totals, Dictionary<int, ArchiveActorStats> Stats)
{
    internal bool HasLocalHealing { get; init; }
    internal int LootPickups { get; init; }
    internal LogBoss[] LogBosses { get; init; } = [];
    internal Dictionary<int, int> DeathCounts { get; init; } = new();
    internal static Fight Create(Fight source)
    {
        if (source.ArchiveSummary != null) return source;
        var damage = source.Damage.ToLookup(h => h.ActorId);
        var healing = source.Healing.ToLookup(h => h.ActorId);
        var taken = source.Damage.GroupBy(h => h.TargetId).ToDictionary(g => g.Key, g => g.Sum(h => (long)h.Damage));
        var totals = Analysis.Totals(source);
        var relevantActors = source.Deaths.Select(d => d.EntityId).Concat(source.Builds.Keys)
            .Concat(source.Actors.Values.Where(a => a.Party).Select(a => a.Id)).Append(source.LocalPlayer).ToHashSet();
        foreach (var hit in source.Damage) { relevantActors.Add(hit.ActorId); relevantActors.Add(hit.TargetId); }
        foreach (var heal in source.Healing) { relevantActors.Add(heal.ActorId); relevantActors.Add(heal.TargetId); }
        foreach (var defense in source.Defense) { relevantActors.Add(defense.ActorId); relevantActors.Add(defense.TargetId); }
        var searchableActors = source.Actors.Values.Where(a => relevantActors.Contains(a.Id)).ToArray();
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
        var castSummary = CastOutcomes.Build(source);
        var castActors = castSummary.Rows.Select(r => r.ActorId).ToHashSet();
        return new Fight {
            SavedCasts = castSummary,
            Telegraphs = new(source.Telegraphs), OmittedTelegraphs = source.OmittedTelegraphs,
            GatheredObjects = new(source.GatheredObjects), OmittedGathers = source.OmittedGathers, Version = source.Version, Id = source.Id, Label = source.Label, Created = source.Created, Demo = source.Demo,
            LocalPlayer = source.LocalPlayer, Training = source.Training, BossRecords = BossPersonalBest.Records(source), ObservedRun = source.ObservedRun, DungeonRun = source.DungeonRun, MixedRunContexts = source.MixedRunContexts,
            // The live identity dictionary includes everything seen in earlier
            // fights. Only participants need full identity metadata in history.
            Actors = source.Actors.Where(p => castActors.Contains(p.Key) || p.Value.Player && (p.Key == source.LocalPlayer || stats.ContainsKey(p.Key) || source.Builds.ContainsKey(p.Key)))
                .ToDictionary(p => p.Key, p => p.Value with { Gear = null, Collections = null, Daevanion = null, Wings = null, Titles = null, VisibleEquipment = null }),
            Builds = new(source.Builds), Specs = new(source.Specs),
            ArchiveSummary = new(source.Start, source.End, source.HasEvents, EncounterOverview.HasLocalDamage(source),
                PvpAnalysis.Build(source).HasOpponents, source.Damage.Count + source.Healing.Count, BossAnalysis.Summary(source),
                searchableActors.Select(a => a.Name).Distinct().ToArray(),
                searchableActors.Where(a => a.Player).Select(a => a.Name).Distinct().ToArray(), totals, stats) {
                    HasLocalHealing = source.LocalPlayer > 0 && source.Healing.Any(h => h.ActorId == source.LocalPlayer && h.Damage > 0),
                    LootPickups = SavedHistory.LootPickups(source),
                    LogBosses = EncounterLog.Bosses(source),
                    DeathCounts = source.Deaths.Where(d => source.Actors.GetValueOrDefault(d.EntityId)?.Player == true)
                        .GroupBy(d => d.EntityId).ToDictionary(g => g.Key, g => g.Count())
                }
        };
    }
}
