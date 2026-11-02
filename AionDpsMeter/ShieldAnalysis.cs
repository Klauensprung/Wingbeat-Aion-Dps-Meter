using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

public sealed record ShieldSummary(int TargetId, int CasterId, int SkillCode, int EffectId, long Added, long Consumed, int Updates);
internal sealed record ShieldRow(string Recipient, string Source, string Skill, long Added, long Consumed, int Updates);
internal static class ShieldAnalysis
{
    sealed record State(long Remaining, int Caster, int Skill, int Effect);
    static long? Number(JsonElement f, string key) => f.ValueKind == JsonValueKind.Object && f.TryGetProperty(key, out var v) &&
        v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) ? n : null;
    internal static void Validate(ShieldSummary[] rows) {
        if (rows.Length > 16384 || rows.Any(r => r == null || r.TargetId <= 0 || r.CasterId < 0 || r.SkillCode < 0 || r.EffectId < 0 ||
            r.Added < 0 || r.Consumed < 0 || r.Updates <= 0) || rows.Select(r => (r.TargetId, r.CasterId, r.SkillCode, r.EffectId)).Distinct().Count() != rows.Length)
            throw new InvalidDataException("Invalid shield summary.");
    }
    internal static ShieldSummary[] Summarize(Fight fight)
    {
        if (fight.SavedShields != null) return fight.SavedShields;
        if (!fight.HasEvents) return [];
        var states = new Dictionary<(int Target, int Uid), State>();
        var rows = new Dictionary<(int Target, int Caster, int Skill, int Effect), ShieldSummary>();
        var reused = fight.Actors.Values.Where(a => a.OriginalEntityId > 0).Select(a => a.OriginalEntityId).ToHashSet();
        foreach (var e in fight.Network.OrderBy(e => e.Timestamp)) {
            if (e.Timestamp < 0 || e.Timestamp > fight.End) continue;
            if (e.Kind == "local-zone") { states.Clear(); continue; }
            if (e.Kind is "entity-death" or "entity-removed" && Number(e.Fields, "EntityId") is > 0 and <= int.MaxValue and var dead) {
                foreach (var deadKey in states.Keys.Where(k => k.Target == (int)dead).ToArray()) states.Remove(deadKey);
                continue;
            }
            if (e.Kind is not ("barrier" or "abnormal-remove")) continue;
            if (Number(e.Fields, "TargetId") is not (> 0 and <= int.MaxValue) ||
                Number(e.Fields, e.Kind == "barrier" ? "Uid" : "Slot") is not (>= 0 and <= int.MaxValue)) continue;
            int target = (int)Number(e.Fields, "TargetId")!, uid = (int)Number(e.Fields, e.Kind == "barrier" ? "Uid" : "Slot")!;
            var key = (target, uid);
            if (e.Kind == "abnormal-remove") { states.Remove(key); continue; }
            if (reused.Contains(target) || fight.Actors.GetValueOrDefault(target)?.Player != true ||
                Number(e.Fields, "Remaining") is not long remaining || remaining < 0) continue;
            states.TryGetValue(key, out var old);
            int Id(string name, int fallback) => Number(e.Fields, name) is > 0 and <= int.MaxValue and var n ? (int)n : fallback;
            int? captured = SupportAttribution.Captured(e);
            int caster = captured ?? Id("ActorId", old?.Caster ?? 0), skill = Id("SkillCode", old?.Skill ?? 0), effect = Id("AbnormalId", old?.Effect ?? 0);
            if (captured == null && reused.Contains(caster)) caster = 0;
            if (!states.ContainsKey(key) && states.Count >= 16384) states.Clear();
            states[key] = new(remaining, caster, skill, effect);
            if (e.Timestamp < fight.Start) continue;
            long before = old?.Remaining ?? 0;
            long added = remaining > before ? remaining - before : 0, consumed = before > remaining ? before - remaining : 0;
            if (added == 0 && consumed == 0) continue;
            var group = (target, caster, skill, effect);
            if (!rows.TryGetValue(group, out var row)) {
                if (rows.Count >= 16384) continue;
                row = new(target, caster, skill, effect, 0, 0, 0);
            }
            rows[group] = row with { Added = EffectDuration.Add(row.Added, added), Consumed = EffectDuration.Add(row.Consumed, consumed), Updates = row.Updates == int.MaxValue ? int.MaxValue : row.Updates + 1 };
        }
        return rows.Values.OrderBy(r => r.TargetId).ThenBy(r => r.CasterId).ThenBy(r => r.SkillCode).ThenBy(r => r.EffectId).ToArray();
    }
    internal static ShieldRow[] Build(Fight fight, Func<int, string> skillName) => Summarize(fight).Select(r => new ShieldRow(
        fight.Name(r.TargetId), r.CasterId == 0 ? "Source not captured" : fight.Name(r.CasterId),
        r.SkillCode == 0 ? "Skill not captured" : skillName(r.SkillCode), r.Added, r.Consumed, r.Updates)).ToArray();
}
