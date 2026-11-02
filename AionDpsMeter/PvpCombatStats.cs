using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class PvpCombatStats
{
    internal static long PeakFiveSeconds(IEnumerable<DamageEvent> source)
    {
        var hits = source.Where(h => h.Damage > 0 && h.Timestamp >= 0).OrderBy(h => h.Timestamp).ToArray();
        long sum = 0, peak = 0; int left = 0;
        for (int right = 0; right < hits.Length; right++) {
            sum += hits[right].Damage;
            while (hits[right].Timestamp - hits[left].Timestamp >= 5000) sum -= hits[left++].Damage;
            peak = Math.Max(peak, sum);
        }
        return peak;
    }
    internal static Dictionary<int, int> Kills(Fight fight, IReadOnlySet<int> participants)
    {
        var reused = fight.Actors.Values.Where(a => a.OriginalEntityId > 0).Select(a => a.OriginalEntityId).ToHashSet();
        bool Friendly(int id) => id == fight.LocalPlayer || fight.Actors.GetValueOrDefault(id)?.Party == true;
        static int Id(JsonElement fields, string key) => fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty(key, out var value) &&
            value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int id) && id > 0 ? id : 0;
        var deaths = fight.Deaths.GroupBy(d => d.EntityId).ToDictionary(g => g.Key, g => g.ToArray());
        var candidates = new Dictionary<(int Victim, long Time), HashSet<int>>();
        foreach (var e in fight.Network.Where(e => e.Kind == "entity-death" && e.Timestamp >= 0)) {
            int victim = Id(e.Fields, "EntityId"), killer = Id(e.Fields, "KillerId");
            if (!participants.Contains(victim) || !participants.Contains(killer) || killer == victim ||
                reused.Contains(victim) || reused.Contains(killer) || Friendly(victim) == Friendly(killer) || !deaths.TryGetValue(victim, out var observed)) continue;
            var match = observed.Where(d => Math.Abs(d.Timestamp - e.Timestamp) <= 1000).OrderBy(d => Math.Abs(d.Timestamp - e.Timestamp)).FirstOrDefault();
            if (match == null) continue;
            var key = (victim, match.Timestamp);
            if (!candidates.TryGetValue(key, out var killers)) candidates[key] = killers = new();
            killers.Add(killer);
        }
        // Conflicting packet attribution stays unknown rather than awarding two kills.
        return candidates.Values.Where(k => k.Count == 1).Select(k => k.Single()).GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
    }
}
