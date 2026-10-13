using System.Text.Json;

namespace AionDpsMeter.Local;

internal sealed class CooldownTracker
{
    readonly Dictionary<int, (long Timestamp, int ReportedMs, long Deadline, long Sequence)> timers = new();
    readonly Dictionary<int, (long Timestamp, long Started)> casts = new();
    string? fightId, identity;
    int processed;
    long sequence;
    internal void Reset() { timers.Clear(); casts.Clear(); fightId = identity = null; processed = 0; sequence = 0; }
    internal void Observe(Fight fight, long utcNow, long monotonicNow)
    {
        var actor = fight.Actors.GetValueOrDefault(fight.LocalPlayer);
        string? current = actor is { Player: true, Server: > 0 } && !actor.Name.StartsWith("Entity ") ? $"{actor.Server}:{actor.Name}" : null;
        if (current != null && identity != null && current != identity) Reset();
        if (current != null) identity = current;
        if (fightId != fight.Id || processed > fight.Network.Count) { fightId = fight.Id; processed = 0; }
        for (; processed < fight.Network.Count; processed++) {
            var e = fight.Network[processed];
            if (e.Kind == "skill-use" && e.Timestamp >= 0 && e.Timestamp <= utcNow && e.Fields.ValueKind == JsonValueKind.Object &&
                e.Fields.TryGetProperty("ActorId", out var actorId) && actorId.ValueKind == JsonValueKind.Number && actorId.TryGetInt32(out int caster) && caster > 0 && caster == fight.LocalPlayer &&
                e.Fields.TryGetProperty("SkillCode", out var castSkill) && castSkill.ValueKind == JsonValueKind.Number && castSkill.TryGetInt32(out int castId) && castId > 0) {
                if (!casts.TryGetValue(castId, out var oldCast) || e.Timestamp > oldCast.Timestamp)
                    casts[castId] = (e.Timestamp, monotonicNow - (utcNow - e.Timestamp));
                continue;
            }
            if (e.Kind != "skill-cooldown" || e.Timestamp < 0 || e.Timestamp > utcNow || e.Fields.ValueKind != JsonValueKind.Object ||
                !e.Fields.TryGetProperty("SkillCode", out var code) || code.ValueKind != JsonValueKind.Number || !code.TryGetInt32(out int id) || id <= 0 ||
                !e.Fields.TryGetProperty("RemainingMs", out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int ms) || ms is < 0 or > 86400000) continue;
            if (timers.TryGetValue(id, out var previous) && (e.Timestamp < previous.Timestamp || e.Timestamp == previous.Timestamp && ms == previous.ReportedMs)) continue;
            long remaining = Math.Max(0, ms - (utcNow - e.Timestamp));
            timers[id] = (e.Timestamp, ms, monotonicNow + remaining, ++sequence);
        }
    }
    internal double? Remaining(int skill, long now) => timers.TryGetValue(skill, out var value) ? Math.Max(0, value.Deadline - now) / 1000d : null;
    internal double? Remaining(int skill, long now, SkillNames names)
        => Reading(skill, now, names, null).Seconds;
    internal (double? Seconds, bool Estimated) Reading(int skill, long now, SkillNames names, IReadOnlyDictionary<int, double>? fallbackSeconds)
    {
        var choice = CooldownSkillChoice.Resolve(skill, names);
        // The most recent report for any matching variant is authoritative,
        // including zero/reset reports and differently named charge tiers.
        long timestamp = -1, latestSequence = -1, deadline = 0;
        foreach (var (id, value) in timers) {
            if (!choice.Matches(CooldownSkillChoice.Resolve(id, names))) continue;
            if (value.Timestamp > timestamp || value.Timestamp == timestamp && value.Sequence > latestSequence) {
                timestamp = value.Timestamp; latestSequence = value.Sequence; deadline = value.Deadline;
            }
        }
        double duration = CooldownDurations.Seconds(skill, names, fallbackSeconds);
        if (double.IsFinite(duration) && duration is > 0 and <= 86400) {
            long castTimestamp = timestamp, started = 0;
            foreach (var (id, cast) in casts) {
                if (cast.Timestamp <= castTimestamp || !choice.Matches(CooldownSkillChoice.Resolve(id, names))) continue;
                castTimestamp = cast.Timestamp; started = cast.Started;
            }
            if (castTimestamp > timestamp)
                return (Math.Max(0, started + duration * 1000 - now) / 1000d, true);
        }
        return (timestamp < 0 ? null : Math.Max(0, deadline - now) / 1000d, false);
    }
    internal bool HasCooldownReports => timers.Count > 0;
    internal int[] ObservedSkills => timers.Keys.Concat(casts.Keys).Distinct().Order().ToArray();
}
