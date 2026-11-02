namespace AionDpsMeter.Local;

internal sealed record BossRecord(int EntityId, int NpcId, string Boss, string Character, int Server, string Class,
    long Damage, double Seconds, long DeathTimestamp)
{
    internal double Dps => Damage / Seconds;
}
internal sealed record BossImprovement(BossRecord Current, double PreviousDps);
internal static class BossPersonalBest
{
    internal static BossRecord[] Records(Fight fight)
    {
        if (fight.Demo || fight.Training != null) return [];
        if (fight.ArchiveSummary != null) return fight.BossRecords;
        var local = fight.Actors.GetValueOrDefault(fight.LocalPlayer);
        if (local is not { Player: true, Server: > 0 } || string.IsNullOrWhiteSpace(local.Name) || local.Name.StartsWith("Entity ")) return [];
        var deaths = fight.Deaths.GroupBy(d => d.EntityId).ToDictionary(g => g.Key, g => g.Min(d => d.Timestamp));
        var bosses = fight.Actors.Values.Where(a => deaths.ContainsKey(a.Id) && CapturedBoss.IsBoss(a)).ToArray();
        if (bosses.Length == 0) return [];
        var bossIds = bosses.Select(b => b.Id).ToHashSet();
        var involved = fight.Damage.Where(h => h.Damage > 0 && !h.IsHeal && (bossIds.Contains(h.ActorId) || bossIds.Contains(h.TargetId)))
            .SelectMany(h => new[] { (Id: h.ActorId, Hit: h), (Id: h.TargetId, Hit: h) }).ToLookup(p => p.Id, p => p.Hit);
        var records = new List<BossRecord>();
        foreach (var boss in bosses) {
            if (!deaths.TryGetValue(boss.Id, out long death)) continue;
            var hits = involved[boss.Id].Where(h => h.Timestamp <= death).ToArray();
            long damage = hits.Where(h => h.ActorId == fight.LocalPlayer && h.TargetId == boss.Id).Sum(h => (long)h.Damage);
            if (damage <= 0) continue;
            double seconds = Math.Max(1, (death - hits.Min(h => h.Timestamp)) / 1000d);
            records.Add(new(boss.Id, boss.NpcId, BossCatalogue.Default.Find(boss.NpcId)?.Name ?? fight.Name(boss.Id), local.Name, local.Server, local.Job, damage, seconds, death));
        }
        return records.ToArray();
    }
    internal static BossImprovement[] Improvements(Fight current, IEnumerable<Fight> history)
    {
        var previous = history.Where(f => f.Id != current.Id).DistinctBy(f => f.Id).SelectMany(Records).ToArray();
        return Records(current).Select(record => {
            // Exact NPC template keeps difficulty variants separate. Location and
            // difficulty are not inferred from a shared catalogue boss name.
            var comparable = previous.Where(p => p.NpcId == record.NpcId && p.Server == record.Server && p.Class == record.Class &&
                string.Equals(p.Character, record.Character, StringComparison.OrdinalIgnoreCase)).ToArray();
            double best = comparable.Select(p => p.Dps).DefaultIfEmpty(0).Max();
            return comparable.Length > 0 && record.Dps > best ? new BossImprovement(record, best) : null;
        }).OfType<BossImprovement>().ToArray();
    }
}
