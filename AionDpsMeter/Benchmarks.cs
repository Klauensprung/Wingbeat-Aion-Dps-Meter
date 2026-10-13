namespace AionDpsMeter.Local;

public sealed record TrendRow(DateTime Date, string Encounter, string Character, string Job, int Server, int CP, double Seconds, double DPS, double HPS, string FightId);
public sealed record ReportRow(string Character, string Encounter, string CPBand, int LocalSamples, double DPS, double? LocalPercentile, double? MedianDPS, double? TopQuarterDPS, double? TopFivePercentDPS, string Status);
public sealed record CohortRow(string Tier, string Skill, int Code, int Observations, double UsagePercent, double MeanDamageShare, double YourDamageShare, double MeanEventsPerMinute, double YourEventsPerMinute);
public sealed record DifferenceRow(string Tier, string Metric, double Yours, double PeerMedian, double TopMean, double Difference, string Unit, int Samples);
public sealed record CohortSpecRow(string Tier, string Skill, int Code, int Level, string Choices, int Observations, int KnownLoadouts, double UsagePercent);
public sealed record CohortOpenerRow(string Tier, string FirstSkill, string FirstEvent, int Observations, double UsagePercent, string YourFirstSkill);
public sealed record CohortEquipmentRow(string Tier, string Slot, string Item, int Observations, int KnownLoadouts, double UsagePercent);

public static class Benchmarks
{
    // Results describe local fight observations, never a population leaderboard.
    public static string Encounter(Fight f) => f.Label.Trim();
    public static string Band(int cp) => cp <= 0 ? "Unknown" : $"{cp / 1000 * 1000:N0}–{cp / 1000 * 1000 + 999:N0}";
    static bool Known(string job) => !string.IsNullOrWhiteSpace(job) && job is not ("Unknown" or "None" or "0");
    public static TrendRow[] Trends(IEnumerable<Fight> fights) => fights.Where(f => !f.Demo && f.HasEvents).DistinctBy(f => f.Id)
        .SelectMany(f => Analysis.Totals(f).Where(r => (r.Damage > 0 || r.Healing > 0) && f.Actors.GetValueOrDefault(r.Id)?.Player == true)
        .Select(r => new TrendRow(f.Created.ToLocalTime(), EncounterOverview.Title(f).Trim(), r.Character, f.Actors[r.Id].Job, f.Actors[r.Id].Server,
            r.CP, Math.Round(f.Seconds, 1), r.DPS, r.HPS, f.Id))).OrderBy(r => r.Date).ToArray();
    public static string Missing(Fight fight, TotalRow actor)
    {
        var identity = fight.Actors.GetValueOrDefault(actor.Id);
        return fight.Demo ? "Demo excluded" : actor.Damage == 0 && actor.Healing == 0 ? "No damage or healing recorded for this character" : actor.CP <= 0 ? "Set Combat Power in character details" : identity == null || !Known(identity.Job) ? "Set class in character details" :
            identity.Server <= 0 ? "Set server in character details" : Encounter(fight).Equals("Local fight", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(Encounter(fight)) ? "Set a specific encounter label" : "";
    }
    public static (Fight Fight, TotalRow Row)[] Peers(Fight current, TotalRow actor, IEnumerable<Fight> archive, IReadOnlyDictionary<string, TotalRow[]>? totals = null)
    {
        if (Missing(current, actor).Length > 0) return [];
        var identity = current.Actors[actor.Id]; double seconds = current.Seconds;
        return archive.Where(f => !f.Demo && f.HasEvents && f.Id != current.Id && Encounter(f).Equals(Encounter(current), StringComparison.OrdinalIgnoreCase))
            .DistinctBy(f => f.Id).Where(f => f.Seconds >= seconds * .8 && f.Seconds <= seconds * 1.25)
            .SelectMany(f => (totals?.GetValueOrDefault(f.Id) ?? Analysis.Totals(f)).Where(r => (r.Damage > 0 || r.Healing > 0) && r.CP > 0 && r.CP / 1000 == actor.CP / 1000 &&
                f.Actors.GetValueOrDefault(r.Id) is { Player: true } a && a.Job.Equals(identity.Job, StringComparison.OrdinalIgnoreCase) && a.Server == identity.Server)
            .Select(r => (Fight: f, Row: r))).OrderByDescending(p => p.Row.DPS).ToArray();
    }
    public static double Median(IEnumerable<double> values)
    {
        var ordered = values.Order().ToArray();
        return ordered.Length == 0 ? 0 : (ordered[(ordered.Length - 1) / 2] + ordered[ordered.Length / 2]) / 2;
    }
    public static ReportRow[] Report(Fight f, IEnumerable<Fight> archive)
    {
        double seconds = f.Seconds;
        var saved = archive.Where(a => !a.Demo && a.HasEvents && a.Id != f.Id && Encounter(a).Equals(Encounter(f), StringComparison.OrdinalIgnoreCase))
            .DistinctBy(a => a.Id).Where(a => a.Seconds >= seconds * .8 && a.Seconds <= seconds * 1.25).ToArray();
        var totals = saved.ToDictionary(a => a.Id, Analysis.Totals);
        return Analysis.Totals(f).Where(r => f.Actors.GetValueOrDefault(r.Id)?.Player == true).Select(r => {
            var peers = Peers(f, r, saved, totals);
            double? percentile = peers.Length < 20 ? null : Math.Round(100d * (peers.Count(p => p.Row.DPS < r.DPS) + .5 * peers.Count(p => p.Row.DPS == r.DPS)) / peers.Length, 1);
            double? top = peers.Length < 100 ? null : Math.Round(peers.Take((int)Math.Ceiling(peers.Length * .05)).Average(p => p.Row.DPS), 1);
            string missing = Missing(f, r);
            string status = missing.Length > 0 ? missing : peers.Length < 20 ? $"{peers.Length}/20 matching observations; top 5% needs 100" : "Local samples · duration within −20%/+25% · DPS ranked";
            return new ReportRow(r.Character, Encounter(f), Band(r.CP), peers.Length, r.DPS, percentile,
                peers.Length < 20 ? null : Median(peers.Select(p => p.Row.DPS)), peers.Length < 20 ? null : Math.Round(peers.Take((int)Math.Ceiling(peers.Length * .25)).Average(p => p.Row.DPS), 1), top, status);
        }).ToArray();
    }
    static IEnumerable<(string Name, (Fight Fight, TotalRow Row)[] Observations)> Tiers((Fight Fight, TotalRow Row)[] peers)
    {
        if (peers.Length >= 20) yield return ("Top quarter (local)", peers.Take((int)Math.Ceiling(peers.Length * .25)).ToArray());
        if (peers.Length >= 100) yield return ("Top 5% (local)", peers.Take((int)Math.Ceiling(peers.Length * .05)).ToArray());
    }
    static Dictionary<int, (long Amount, int Hits)> Skills(Fight f, int actor) => f.Damage.Where(e => e.ActorId == actor)
        .GroupBy(e => DetailedAnalysis.Family(e.SkillCode)).ToDictionary(g => g.Key, g => (g.Sum(e => (long)e.Damage), g.Count()));
    public static CohortRow[] Cohort(Fight f, TotalRow actor, IEnumerable<Fight> archive, Func<int, string> name, (Fight Fight, TotalRow Row)[]? matched = null)
    {
        var rows = new List<CohortRow>();
        var peers = matched ?? Peers(f, actor, archive);
        var ownSkills = Skills(f, actor.Id); double seconds = f.Seconds;
        var peerSkills = peers.ToDictionary(p => (p.Fight.Id, p.Row.Id), p => Skills(p.Fight, p.Row.Id));
        var durations = peers.Select(p => p.Fight).DistinctBy(p => p.Id).ToDictionary(p => p.Id, p => p.Seconds);
        foreach (var tier in Tiers(peers))
        foreach (int code in tier.Observations.SelectMany(p => peerSkills[(p.Fight.Id, p.Row.Id)].Keys).Distinct())
        {
            var samples = tier.Observations.Select(p => (p, Skill: peerSkills[(p.Fight.Id, p.Row.Id)].GetValueOrDefault(code))).ToArray();
            var yours = ownSkills.GetValueOrDefault(code);
            rows.Add(new(tier.Name, name(code), code, samples.Count(p => p.Skill.Hits > 0), Math.Round(100d * samples.Count(p => p.Skill.Hits > 0) / samples.Length, 1),
                Math.Round(samples.Average(p => p.p.Row.Damage == 0 ? 0 : 100d * p.Skill.Amount / p.p.Row.Damage), 1),
                actor.Damage == 0 ? 0 : Math.Round(100d * yours.Amount / actor.Damage, 1),
                Math.Round(samples.Average(p => 60d * p.Skill.Hits / durations[p.p.Fight.Id]), 1), Math.Round(60d * yours.Hits / seconds, 1)));
        }
        return rows.OrderBy(r => r.Tier).ThenByDescending(r => r.MeanDamageShare).ToArray();
    }
    public static DifferenceRow[] Differences(Fight f, TotalRow actor, IEnumerable<Fight> archive, (Fight Fight, TotalRow Row)[]? matched = null)
    {
        var peers = matched ?? Peers(f, actor, archive); var rows = new List<DifferenceRow>();
        double Opener(Fight fight, TotalRow row) {
            var hits = fight.Damage.Where(e => e.ActorId == row.Id).ToArray();
            if (hits.Length == 0) return 0;
            long first = hits.Select(e => e.Timestamp).Concat(fight.Healing.Where(e => e.ActorId == row.Id).Select(e => e.Timestamp)).Min();
            return hits.Where(e => e.Timestamp - first < 15000).Sum(e => (long)e.Damage) / Math.Min(15, fight.Seconds);
        }
        var metrics = new (string Name, string Unit, Func<Fight, TotalRow, double> Value)[] {
            ("Damage per second", "DPS", (_, r) => r.DPS), ("Healing per second", "HPS", (_, r) => r.HPS),
            ("Critical damage events", "%", (_, r) => r.CritPercent),
            ("Damage events per minute", "events/min", (fight, r) => 60d * fight.Damage.Count(e => e.ActorId == r.Id) / fight.Seconds),
            ("Opening damage rate (first 15s)", "DPS", Opener),
            ("Damage received per second", "damage/s", (fight, r) => fight.Damage.Where(e => e.TargetId == r.Id).Sum(e => (long)e.Damage) / fight.Seconds)
        };
        foreach (var tier in Tiers(peers))
        foreach (var metric in metrics)
        {
            double yours = metric.Value(f, actor), top = tier.Observations.Average(p => metric.Value(p.Fight, p.Row));
            rows.Add(new(tier.Name, metric.Name, Math.Round(yours, 1), Math.Round(Median(peers.Select(p => metric.Value(p.Fight, p.Row))), 1),
                Math.Round(top, 1), Math.Round(top - yours, 1), metric.Unit, tier.Observations.Length));
        }
        return rows.ToArray();
    }
    public static CohortSpecRow[] Specs(Fight f, TotalRow actor, IEnumerable<Fight> archive, Func<int, string> name, (Fight Fight, TotalRow Row)[]? matched = null)
        => Tiers(matched ?? Peers(f, actor, archive)).SelectMany(tier => {
            var builds = tier.Observations.Select(p => FightMetadata.BuildFor(p.Fight, p.Row.Id)).Where(b => b?.Complete == true).ToArray();
            return builds.SelectMany(b => b!.Skills.Select(s => (s.Skill, s.Level, Choices: string.Join(", ", s.Choices))))
                .GroupBy(s => s).Select(g => new CohortSpecRow(tier.Name, name(g.Key.Skill), g.Key.Skill, g.Key.Level, g.Key.Choices, g.Count(), builds.Length, Math.Round(100d * g.Count() / builds.Length, 1)));
        }).OrderBy(r => r.Tier).ThenByDescending(r => r.Observations).ToArray();
    public static CohortEquipmentRow[] Equipment(Fight f, TotalRow actor, IEnumerable<Fight> archive, (Fight Fight, TotalRow Row)[]? matched = null)
        => Tiers(matched ?? Peers(f, actor, archive)).SelectMany(tier => {
            var builds = tier.Observations.Select(p => FightMetadata.BuildFor(p.Fight, p.Row.Id)).Where(b => b?.Equipment.Count > 0).ToArray();
            return builds.SelectMany(b => b!.Equipment).GroupBy(e => (e.Key, e.Value)).Select(g =>
                new CohortEquipmentRow(tier.Name, g.Key.Key, g.Key.Value, g.Count(), builds.Count(b => b!.Equipment.ContainsKey(g.Key.Key)),
                    Math.Round(100d * g.Count() / builds.Count(b => b!.Equipment.ContainsKey(g.Key.Key)), 1)));
        }).ToArray();
    public static CohortOpenerRow[] Openers(Fight f, TotalRow actor, IEnumerable<Fight> archive, Func<int, string> name, (Fight Fight, TotalRow Row)[]? matched = null)
    {
        OpenerRow? First(Fight fight, int id) => DetailedAnalysis.Openers(fight, name).FirstOrDefault(r => r.ActorId == id);
        var own = First(f, actor.Id);
        return Tiers(matched ?? Peers(f, actor, archive)).SelectMany(tier => tier.Observations.Select(p => First(p.Fight, p.Row.Id)).Where(r => r != null)
            .GroupBy(r => (Code: DetailedAnalysis.Family(r!.SkillCode), r.Event)).Select(g =>
                new CohortOpenerRow(tier.Name, name(g.Key.Code), g.Key.Event, g.Count(), Math.Round(100d * g.Count() / tier.Observations.Length, 1), own?.Skill ?? "None"))).ToArray();
    }
}
