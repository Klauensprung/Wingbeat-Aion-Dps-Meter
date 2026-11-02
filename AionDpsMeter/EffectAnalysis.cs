using System.Text.Json;

namespace AionDpsMeter.Local;

internal sealed record EffectInterval(int TargetId, int CasterId, int EffectId, int SkillCode, long Start, long End, bool DurationKnown);
public sealed record EffectSummary(int TargetId, int CasterId, int EffectId, int SkillCode, long TrackedMs, int Segments, bool HasUnknownDuration);
internal sealed record EffectRow(string Recipient, string Source, int EffectId, string SourceSkill, double TrackedSeconds, double TrackedUptime,
    int TrackedSegments, bool HasUnknownDuration)
{
    public string EffectName => EffectCatalogue.Default.Find(EffectId)?.Name ?? $"Effect {EffectId}";
    public string EffectKind => EffectCatalogue.Default.Find(EffectId)?.Kind ?? "Unknown";
}
internal static class EffectAnalysis
{
    sealed record Active(int Target, int Caster, int Effect, int Skill, long Start, long? Expiry);
    static long? Number(JsonElement fields, string key) => fields.ValueKind == JsonValueKind.Object &&
        fields.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long n) ? n : null;
    internal static EffectInterval[] Intervals(Fight fight)
    {
        if (!fight.HasEvents) return [];
        var reused = fight.Actors.Values.Where(a => a.OriginalEntityId > 0).Select(a => a.OriginalEntityId).ToHashSet();
        var active = new Dictionary<(int Target, int Slot), Active>();
        var intervals = new List<EffectInterval>();
        void Close(Active effect, long stop) {
            long start = Math.Max(fight.Start, effect.Start), end = Math.Min(fight.End, Math.Min(stop, effect.Expiry ?? long.MaxValue));
            if (end > start) intervals.Add(new(effect.Target, effect.Caster, effect.Effect, effect.Skill, start, end, effect.Expiry != null));
        }
        foreach (var e in fight.Network.Where(e => e.Kind is "abnormal-apply" or "abnormal-refresh" or "abnormal-remove" or "entity-removed" or "local-zone").OrderBy(e => e.Timestamp)) {
            if (e.Timestamp >= 0 && e.Timestamp <= fight.End && e.Kind is "entity-removed" or "local-zone") {
                long? entity = Number(e.Fields, "EntityId");
                foreach (var removedKey in active.Keys.Where(k => e.Kind == "local-zone" || k.Target == entity).ToArray()) {
                    Close(active[removedKey], e.Timestamp); active.Remove(removedKey);
                }
                continue;
            }
            if (e.Timestamp < 0 || e.Timestamp > fight.End || Number(e.Fields, "TargetId") is not long target || target <= 0 || target > int.MaxValue ||
                reused.Contains((int)target) || Number(e.Fields, "Slot") is not long slot || slot < 0 || slot > int.MaxValue) continue;
            var key = ((int)target, (int)slot);
            if (e.Kind == "abnormal-remove") {
                if (active.Remove(key, out var removed)) Close(removed, e.Timestamp);
                continue;
            }
            if (Number(e.Fields, "AbnormalId") is not long id || id <= 0 || id > 1000000000 ||
                !EffectDuration.TryRead(e.Fields, out var duration, out bool untilRemoved)) continue;
            int caster = SupportAttribution.Captured(e) ??
                (Number(e.Fields, "CasterId") is > 0 and <= int.MaxValue and var c && !reused.Contains((int)c) ? (int)c : 0);
            int skill = Number(e.Fields, "SourceSkillId") is > 0 and <= int.MaxValue and var s ? (int)s : 0;
            if (active.Remove(key, out var previous)) Close(previous, e.Timestamp);
            active[key] = new((int)target, caster, (int)id, skill, e.Timestamp, duration is long ms ? EffectDuration.Add(e.Timestamp, ms) : null);
        }
        foreach (var value in active.Values) Close(value, fight.End);
        return intervals.ToArray();
    }
    internal static EffectSummary[] Summarize(Fight fight)
    {
        if (fight.SavedEffects != null) return fight.SavedEffects;
        return Intervals(fight).GroupBy(i => (i.TargetId, i.CasterId, i.EffectId, i.SkillCode)).Select(group => {
            long total = 0, start = -1, end = -1;
            foreach (var interval in group.OrderBy(i => i.Start)) {
                if (start < 0) { start = interval.Start; end = interval.End; }
                else if (interval.Start <= end) end = Math.Max(end, interval.End);
                else { total += end - start; start = interval.Start; end = interval.End; }
            }
            if (start >= 0) total += end - start;
            return new EffectSummary(group.Key.TargetId, group.Key.CasterId, group.Key.EffectId, group.Key.SkillCode, total,
                group.Count(), group.Any(i => !i.DurationKnown));
        }).ToArray();
    }
    internal static EffectRow[] Build(Fight fight, Func<int, string> skillName) => Summarize(fight).Select(s =>
        new EffectRow(fight.Name(s.TargetId), s.CasterId == 0 ? "Source not captured" : fight.Name(s.CasterId), s.EffectId,
            s.SkillCode == 0 ? "Skill not captured" : skillName(s.SkillCode), s.TrackedMs / 1000d,
            100d * s.TrackedMs / Math.Max(1, fight.End - fight.Start), s.Segments, s.HasUnknownDuration))
        .OrderByDescending(r => r.TrackedSeconds).ThenBy(r => r.EffectId).ToArray();
}
