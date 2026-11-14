using System.Text.Json;

namespace AionDpsMeter.Local;

internal sealed record SpectrumPhase(double From, double To, int Phase);
internal sealed record SpectrumSeries(int Id, string Name, string Class, DamageBucket[] Buckets);
internal sealed record SpectrumData(double From, double To, SpectrumSeries[] Series, SpectrumPhase[] Phases);

internal sealed record SpectrumPoint(double From, double To, double Dps);

internal static class DamageSpectrum
{
    // Derive display samples only: raw damage buckets remain the source of totals.
    // A trailing window never pulls future burst damage into an earlier moment.
    internal static SpectrumPoint[] Trend(DamageBucket[] buckets, double seconds)
    {
        var points = new SpectrumPoint[buckets.Length];
        int left = 0; double sum = 0;
        for (int i = 0; i < buckets.Length; i++) {
            var current = buckets[i]; sum += current.Damage;
            double start = Math.Max(buckets[0].From, current.To - Math.Max(seconds, current.To - current.From));
            while (left < i && buckets[left].To <= start) sum -= buckets[left++].Damage;
            double amount = sum - Math.Max(0, start - buckets[left].From) * buckets[left].Dps;
            points[i] = new(start, current.To, Math.Max(0, amount) / Math.Max(.001, current.To - start));
        }
        return points;
    }

    internal static SpectrumPhase[] Phases(Fight fight, int? boss)
    {
        if (boss == null || fight.Actors.Values.Any(a => a.OriginalEntityId == boss)) return [];
        long start = fight.Start; double duration = fight.Seconds;
        var points = new List<(double Time, int Phase)>();
        foreach (var e in fight.Network.Where(e => e.Kind == "actor-phase").OrderBy(e => e.Timestamp)) {
            if (e.Fields.ValueKind != JsonValueKind.Object ||
                !e.Fields.TryGetProperty("EntityId", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out int entity) || entity != boss ||
                !e.Fields.TryGetProperty("Phase", out var p) || p.ValueKind != JsonValueKind.Number || !p.TryGetInt32(out int phase) || phase is < 0 or > 255) continue;
            double time = (e.Timestamp - start) / 1000d;
            if (time > duration || points.Count > 0 && points[^1].Phase == phase) continue;
            points.Add((Math.Max(0, time), phase));
        }
        return points.Select((p, i) => new SpectrumPhase(p.Time, i + 1 < points.Count ? points[i + 1].Time : duration, p.Phase))
            .Where(p => p.To > p.From).ToArray();
    }

    internal static SpectrumData Build(Fight fight, EncounterParticipant[] players, int? boss, double interval, double from = 0, double? to = null)
    {
        long start = fight.Start; double duration = fight.Seconds;
        double end = Math.Clamp(to ?? duration, .001, Math.Max(.001, duration));
        from = Math.Clamp(from, 0, end - .001);
        double width = Math.Max(Math.Max(.25, interval), (end - from) / 1200);
        int count = Math.Clamp((int)Math.Ceiling((end - from) / width), 1, 1200);
        var amounts = players.ToDictionary(p => p.Id, _ => new long[count]);
        foreach (var hit in fight.Damage) {
            if (hit.IsHeal || hit.Damage <= 0 || boss != null && hit.TargetId != boss || !amounts.TryGetValue(hit.ActorId, out var bins)) continue;
            double time = (hit.Timestamp - start) / 1000d;
            // Half-open ranges prevent double-counting when comparing adjacent phases.
            if (time < from || time > end || time == end && end < duration) continue;
            bins[Math.Min(count - 1, (int)((time - from) / width))] += hit.Damage;
        }
        return new(from, end, players.Select(p => new SpectrumSeries(p.Id, p.Participant, p.Class,
            amounts[p.Id].Select((n, i) => new DamageBucket(from + i * width, Math.Min(end, from + (i + 1) * width), n)).ToArray())).ToArray(), Phases(fight, boss));
    }
}
