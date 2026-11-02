using System.Text.Json;

namespace AionDpsMeter.Local;

internal sealed class CooldownTracker
{
    readonly Dictionary<int, (long Timestamp, int ReportedMs, long Deadline, long Sequence, long PreviousTimestamp, long ReductionMs)> timers = new();
    readonly Dictionary<int, (long Timestamp, long Started)> casts = new();
    CooldownCharges charges = new();
    readonly Dictionary<int, int> capacities = new();
    string? fightId, identity;
    int processed;
    long sequence;
    internal void Reset() { timers.Clear(); casts.Clear(); charges.Clear(); capacities.Clear(); fightId = identity = null; processed = 0; sequence = 0; }
    internal void Observe(Fight fight, long utcNow, long monotonicNow, long minimumTimestamp = -1)
    {
        var actor = fight.Actors.GetValueOrDefault(fight.LocalPlayer);
        string? current = actor is { Player: true, Server: > 0 } && !actor.Name.StartsWith("Entity ") ? $"{actor.Server}:{actor.Name}" : null;
        if (current != null && identity != null && current != identity) Reset();
        if (current != null) identity = current;
        capacities.Clear();
        if (actor != null && CapturedSkills.Build(actor) is { } build)
            foreach (var skill in build.Skills) {
                int capacity = SkillChargeCapacity.Capacity(skill.Skill, skill.Level, skill.Choices);
                if (capacity > 1) capacities[skill.Skill] = capacity;
            }
        if (fight.LiveCooldowns is { } compact) { Import(compact, utcNow, monotonicNow, minimumTimestamp); return; }
        if (fightId != fight.Id || processed > fight.Network.Count) { fightId = fight.Id; processed = 0; }
        for (; processed < fight.Network.Count; processed++) {
            var e = fight.Network[processed];
            if (e.Timestamp < minimumTimestamp) continue;
            Apply(e, fight.LocalPlayer, utcNow, monotonicNow);
        }
    }
    internal void Capture(AionDpsMeter.Models.NetworkEvent e, int localPlayer) => Apply(e, localPlayer, e.Timestamp, e.Timestamp);
    internal CooldownTracker Copy()
    {
        var copy = new CooldownTracker { sequence = sequence };
        copy.charges = charges.Copy();
        foreach (var pair in capacities) copy.capacities.Add(pair.Key, pair.Value);
        foreach (var pair in timers) copy.timers.Add(pair.Key, pair.Value);
        foreach (var pair in casts) copy.casts.Add(pair.Key, pair.Value);
        return copy;
    }
    internal int RetainedCount => timers.Count + casts.Count + charges.Count;
    internal ChargeReading? Charges(int skill, long now) => charges.Reading(skill, now);
    internal ChargeDisplay? ChargeDisplay(int skill, long now, int capacity = 1) => charges.Display(skill, now, Math.Max(capacity, capacities.GetValueOrDefault(skill, 1)));
    void Apply(AionDpsMeter.Models.NetworkEvent e, int localPlayer, long utcNow, long monotonicNow)
    {
        charges.Apply(e, localPlayer, utcNow, monotonicNow);
        if (e.Kind == "skill-use" && e.Timestamp >= 0 && e.Timestamp <= utcNow && e.Fields.ValueKind == JsonValueKind.Object &&
            e.Fields.TryGetProperty("ActorId", out var actorId) && actorId.ValueKind == JsonValueKind.Number && actorId.TryGetInt32(out int caster) && caster > 0 && caster == localPlayer &&
            e.Fields.TryGetProperty("SkillCode", out var castSkill) && castSkill.ValueKind == JsonValueKind.Number && castSkill.TryGetInt32(out int castId) && castId > 0) {
            if (!casts.TryGetValue(castId, out var oldCast) || e.Timestamp > oldCast.Timestamp)
                casts[castId] = (e.Timestamp, monotonicNow - (utcNow - e.Timestamp));
            Trim(); return;
        }
        if (e.Kind != "skill-cooldown" || e.Timestamp < 0 || e.Timestamp > utcNow || e.Fields.ValueKind != JsonValueKind.Object ||
            !e.Fields.TryGetProperty("SkillCode", out var code) || code.ValueKind != JsonValueKind.Number || !code.TryGetInt32(out int id) || id <= 0 ||
            !e.Fields.TryGetProperty("RemainingMs", out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int ms) || ms is < 0 or > 86400000) return;
        if (e.Fields.TryGetProperty("ActorId", out var cooldownActor) &&
            (cooldownActor.ValueKind != JsonValueKind.Number || !cooldownActor.TryGetInt32(out int owner) || owner <= 0 || owner != localPlayer)) return;
        if (timers.TryGetValue(id, out var previous) && (e.Timestamp < previous.Timestamp || e.Timestamp == previous.Timestamp && ms == previous.ReportedMs)) return;
        long remaining = Math.Max(0, ms - (utcNow - e.Timestamp));
        // Compare packet timestamps, not arrival times: delayed delivery must
        // not manufacture a reduction. A new cast is excluded when queried.
        long reduction = previous.Timestamp + previous.ReportedMs - (e.Timestamp + ms);
        if (previous.Sequence == 0 || previous.Timestamp + previous.ReportedMs <= e.Timestamp || reduction < 250) reduction = 0;
        timers[id] = (e.Timestamp, ms, monotonicNow + remaining, ++sequence, previous.Timestamp, reduction);
        Trim();
    }
    void Import(CooldownTracker source, long utcNow, long monotonicNow, long minimumTimestamp)
    {
        charges.Import(source.charges, utcNow, monotonicNow, minimumTimestamp);
        foreach (var (id, value) in source.timers) {
            if (value.Timestamp < minimumTimestamp || value.Timestamp > utcNow ||
                timers.TryGetValue(id, out var old) && (old.Timestamp > value.Timestamp || old.Timestamp == value.Timestamp && old.ReportedMs == value.ReportedMs)) continue;
            timers[id] = value with { Deadline = monotonicNow + Math.Max(0, value.Timestamp + value.ReportedMs - utcNow) };
        }
        foreach (var (id, value) in source.casts) {
            if (value.Timestamp < minimumTimestamp || value.Timestamp > utcNow || casts.TryGetValue(id, out var old) && old.Timestamp >= value.Timestamp) continue;
            casts[id] = (value.Timestamp, monotonicNow - (utcNow - value.Timestamp));
        }
        Trim();
    }
    void Trim()
    {
        while (timers.Count > 4096) timers.Remove(timers.MinBy(p => p.Value.Timestamp).Key);
        while (casts.Count > 4096) casts.Remove(casts.MinBy(p => p.Value.Timestamp).Key);
    }
    internal double? Remaining(int skill, long now) => timers.TryGetValue(skill, out var value) ? Math.Max(0, value.Deadline - now) / 1000d : null;
    internal double? Remaining(int skill, long now, SkillNames names)
        => Reading(skill, now, names, null).Seconds;
    internal (double? Seconds, bool Estimated) Reading(int skill, long now, SkillNames names, IReadOnlyDictionary<int, double>? fallbackSeconds)
    {
        if (ChargeDisplay(skill, now) is { } charge) return (charge.RemainingSeconds, charge.Estimated);
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
    internal double? ReductionSeconds(int skill, long now, SkillNames names)
    {
        if (Charges(skill, now) != null) return null;
        var choice = CooldownSkillChoice.Resolve(skill, names);
        var reports = timers.Where(p => choice.Matches(CooldownSkillChoice.Resolve(p.Key, names))).ToArray();
        if (reports.Length == 0) return null;
        var latest = reports.OrderByDescending(p => p.Value.Timestamp).ThenByDescending(p => p.Value.Sequence).First();
        var value = latest.Value;
        if (value.ReductionMs <= 0 || value.Deadline <= now) return null;
        // Reports from a different charge tier and casts after the earlier report
        // make the comparison ambiguous. Prefer no marker to a false reduction.
        if (reports.Any(p => p.Key != latest.Key && p.Value.Timestamp > value.PreviousTimestamp) ||
            casts.Any(p => p.Value.Timestamp > value.PreviousTimestamp && choice.Matches(CooldownSkillChoice.Resolve(p.Key, names)))) return null;
        return value.ReductionMs / 1000d;
    }
    internal int[] ObservedSkills => timers.Keys.Concat(casts.Keys).Concat(charges.Skills).Distinct().Order().ToArray();
}
