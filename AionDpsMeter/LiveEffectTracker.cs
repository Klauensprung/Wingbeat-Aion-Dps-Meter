using System.Text.Json;

namespace AionDpsMeter.Local;

internal sealed record LiveEffect(int Slot, int EffectId, int SourceSkill, int CasterId, double? RemainingSeconds, bool UntilRemoved = false);
internal sealed class LiveEffectTracker
{
    sealed record Entry(long Timestamp, bool Removed, int Effect, int Skill, int Caster, long? Deadline, bool UntilRemoved = false);
    readonly Dictionary<int, Entry> slots = new();
    string? fightId, identity;
    int processed;
    long discardedBefore = -1;
    long sceneStartedAt = -1;
    int map;
    long zoneTime = -1;
    internal int RetainedSlots => slots.Count;
    internal void Reset() { slots.Clear(); fightId = identity = null; processed = 0; discardedBefore = sceneStartedAt = zoneTime = -1; map = 0; }
    void Put(int slot, Entry entry)
    {
        if (!slots.ContainsKey(slot) && slots.Count >= 1024) {
            var oldest = slots.MinBy(p => p.Value.Timestamp);
            discardedBefore = Math.Max(discardedBefore, oldest.Value.Timestamp); slots.Remove(oldest.Key);
        }
        slots[slot] = entry;
    }
    static long? Number(JsonElement fields, string key) => fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long n) ? n : null;
    internal void Observe(Fight fight, long utcNow, long monotonicNow, long minimumTimestamp = -1)
    {
        var actor = fight.Actors.GetValueOrDefault(fight.LocalPlayer);
        if (actor is not { Player: true } || fight.LocalPlayer <= 0) { if (identity != null) Reset(); return; }
        string current = $"{actor.Id}:{actor.Server}:{actor.Name}";
        if (identity != null && identity != current) Reset();
        identity = current;
        if (fightId != fight.Id || processed > fight.Network.Count) { fightId = fight.Id; processed = 0; }
        for (; processed < fight.Network.Count; processed++) {
            var e = fight.Network[processed];
            if (e.Timestamp < minimumTimestamp) continue;
            if (e.Timestamp >= 0 && e.Timestamp <= utcNow && (e.Kind == "local-zone" ||
                e.Kind == "entity-removed" && Number(e.Fields, "EntityId") == fight.LocalPlayer)) {
                // A character spawn emits local-zone before its effects at the
                // same timestamp. Preserve that stream order at the scene boundary.
                if (e.Kind == "local-zone") {
                    if (e.Timestamp < zoneTime) continue;
                    int nextMap = (int)(Number(e.Fields,"MapId") ?? 0);
                    bool preparing = e.Fields.TryGetProperty("AwaitingIdentity", out var value) && value.ValueKind == JsonValueKind.True;
                    bool duplicate = zoneTime >= 0 && nextMap == map && (!preparing || e.Timestamp == zoneTime);
                    zoneTime = e.Timestamp; map = nextMap;
                    if (duplicate) continue;
                    sceneStartedAt = Math.Max(sceneStartedAt, e.Timestamp);
                }
                else discardedBefore = Math.Max(discardedBefore, e.Timestamp);
                foreach (int removedSlot in slots.Where(p => p.Value.Timestamp <= e.Timestamp).Select(p => p.Key).ToArray()) slots.Remove(removedSlot);
                continue;
            }
            if (e.Kind is not ("abnormal-apply" or "abnormal-refresh" or "abnormal-remove") || e.Timestamp < 0 || e.Timestamp < sceneStartedAt || e.Timestamp <= discardedBefore || e.Timestamp > utcNow ||
                Number(e.Fields, "TargetId") != fight.LocalPlayer || Number(e.Fields, "Slot") is not long slot || slot < 0 || slot > int.MaxValue) continue;
            if (slots.TryGetValue((int)slot, out var previous) && (e.Timestamp < previous.Timestamp || e.Timestamp == previous.Timestamp && previous.Removed)) continue;
            if (e.Kind == "abnormal-remove") { Put((int)slot, new(e.Timestamp, true, 0, 0, 0, null)); continue; }
            if (Number(e.Fields, "AbnormalId") is not long effect || effect <= 0 || effect > 1000000000 ||
                !EffectDuration.TryRead(e.Fields, out var duration, out bool untilRemoved)) continue;
            int skill = Number(e.Fields, "SourceSkillId") is > 0 and <= int.MaxValue and var s ? (int)s : 0;
            int caster = Number(e.Fields, "CasterId") is > 0 and <= int.MaxValue and var c ? (int)c : 0;
            Put((int)slot, new(e.Timestamp, false, (int)effect, skill, caster,
                EffectDuration.Deadline(duration, e.Timestamp, utcNow, monotonicNow), untilRemoved));
        }
    }
    internal LiveEffect[] Active(long now) => slots.Where(p => !p.Value.Removed && (p.Value.Deadline == null || p.Value.Deadline > now))
        .Select(p => new LiveEffect(p.Key, p.Value.Effect, p.Value.Skill, p.Value.Caster,
            p.Value.Deadline is long end ? Math.Max(0, end - now) / 1000d : null, p.Value.UntilRemoved)).OrderBy(e => e.RemainingSeconds ?? double.MaxValue).ThenBy(e => e.Slot).ToArray();
}
