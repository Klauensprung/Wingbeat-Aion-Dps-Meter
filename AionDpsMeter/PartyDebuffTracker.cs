using System.Text.Json;

namespace AionDpsMeter.Local;

internal sealed record PartyDebuffNotice(int EffectId, string Name, int Members);
internal sealed record PartyMemberDebuff(int Target, int Effect, string Name, double? Seconds);
internal sealed class PartyDebuffTracker
{
    sealed record Entry(long Timestamp, int Effect, long? Deadline, bool Removed);
    readonly Dictionary<(int Target, int Slot), Entry> slots = new();
    readonly Dictionary<int, string> members = new();
    readonly Dictionary<int, long> lastAlert = new();
    readonly Dictionary<int, long> removedAt = new();
    HashSet<int> aboveThreshold = new();
    string? identity, fightId;
    int processed;
    long discardedBefore = -1;
    long sceneStartedAt = -1;
    internal int RetainedSlots => slots.Count;
    internal PartyMemberDebuff[] Active(long now) => slots.Where(p=>!p.Value.Removed&&(p.Value.Deadline==null||p.Value.Deadline>now))
        .GroupBy(p=>(p.Key.Target,p.Value.Effect)).Select(g=>new PartyMemberDebuff(g.Key.Target,g.Key.Effect,
            EffectCatalogue.Default.Find(g.Key.Effect)?.Name??$"Effect {g.Key.Effect}",
            g.Any(p=>p.Value.Deadline==null)?null:Math.Max(0,g.Max(p=>p.Value.Deadline!.Value)-now)/1000d))
        .OrderBy(e=>e.Target).ThenBy(e=>e.Seconds??double.MaxValue).ThenBy(e=>e.Effect).ToArray();
    internal void Reset() { slots.Clear(); members.Clear(); lastAlert.Clear(); removedAt.Clear(); aboveThreshold.Clear(); identity = fightId = null; processed = 0; discardedBefore = sceneStartedAt = -1; }
    static string Identity(Actor actor) => $"{actor.Id}:{actor.Server}:{actor.Name}:{actor.CharacterId}";
    static long? Number(JsonElement fields, string key) => fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number) ? number : null;
    internal void Observe(Fight fight, long utcNow, long monotonicNow, long minimumTimestamp = -1)
    {
        if (fight.LocalPlayer <= 0 || fight.Actors.GetValueOrDefault(fight.LocalPlayer) is not { Player: true } local) { Reset(); return; }
        if (identity != null && identity != Identity(local)) Reset();
        identity = Identity(local);
        var current = fight.Actors.Values.Where(a => a.Player && a.OriginalEntityId == 0 && (a.Id == fight.LocalPlayer || a.Party)).ToDictionary(a => a.Id, Identity);
        foreach (var key in slots.Keys.Where(k => !current.TryGetValue(k.Target, out var name) || !members.TryGetValue(k.Target, out var old) || name != old).ToArray()) slots.Remove(key);
        members.Clear(); foreach (var entry in current) members.Add(entry.Key, entry.Value);
        foreach (int departed in removedAt.Keys.Where(id => !members.ContainsKey(id)).ToArray()) removedAt.Remove(departed);
        var recycled = fight.Actors.Values.Where(a => a.OriginalEntityId > 0).Select(a => a.OriginalEntityId).ToHashSet();
        if (fightId != fight.Id || processed > fight.Network.Count) { fightId = fight.Id; processed = 0; }
        for (; processed < fight.Network.Count; processed++) {
            var e = fight.Network[processed];
            if (e.Timestamp < minimumTimestamp) continue;
            if (e.Timestamp >= 0 && e.Timestamp <= utcNow && e.Kind == "local-zone") {
                // Spawn effects follow the scene marker with the same timestamp.
                sceneStartedAt = Math.Max(sceneStartedAt, e.Timestamp);
                foreach (var removedKey in slots.Where(p => p.Value.Timestamp <= e.Timestamp).Select(p => p.Key).ToArray()) slots.Remove(removedKey);
                continue;
            }
            if (e.Timestamp >= 0 && e.Timestamp <= utcNow && e.Kind == "entity-removed" &&
                Number(e.Fields, "EntityId") is > 0 and <= int.MaxValue and var removed && members.ContainsKey((int)removed)) {
                int removedId = (int)removed;
                removedAt[removedId] = Math.Max(removedAt.GetValueOrDefault(removedId, -1), e.Timestamp);
                foreach (var removedKey in slots.Where(p => p.Key.Target == removedId && p.Value.Timestamp <= e.Timestamp).Select(p => p.Key).ToArray()) slots.Remove(removedKey);
                continue;
            }
            if (e.Kind is not ("abnormal-apply" or "abnormal-refresh" or "abnormal-remove") || e.Timestamp < 0 || e.Timestamp < sceneStartedAt || e.Timestamp > utcNow || e.Timestamp <= discardedBefore ||
                Number(e.Fields, "TargetId") is not long target || target is <= 0 or > int.MaxValue || !members.ContainsKey((int)target) || recycled.Contains((int)target) ||
                Number(e.Fields, "Slot") is not long slot || slot is < 0 or > int.MaxValue || e.Timestamp <= removedAt.GetValueOrDefault((int)target, -1)) continue;
            var key = ((int)target, (int)slot);
            if (slots.TryGetValue(key, out var old) && (e.Timestamp < old.Timestamp || e.Timestamp == old.Timestamp && old.Removed)) continue;
            Entry next;
            if (e.Kind == "abnormal-remove") next = new(e.Timestamp, 0, null, true);
            else {
                if (Number(e.Fields, "AbnormalId") is not long effect || effect is <= 0 or > 1000000000 ||
                    !EffectDuration.TryRead(e.Fields, out var duration, out bool untilRemoved)) continue;
                bool friendlyCaster = Number(e.Fields, "CasterId") is > 0 and <= int.MaxValue and var caster && members.ContainsKey((int)caster);
                bool debuff = !friendlyCaster && EffectCatalogue.Default.Find((int)effect)?.Kind == "Debuff";
                next = new(e.Timestamp, debuff ? (int)effect : 0, EffectDuration.Deadline(duration, e.Timestamp, utcNow, monotonicNow), !debuff);
            }
            if (!slots.ContainsKey(key) && slots.Count >= 4096) {
                var oldest = slots.MinBy(p => p.Value.Timestamp); discardedBefore = Math.Max(discardedBefore, oldest.Value.Timestamp); slots.Remove(oldest.Key);
            }
            slots[key] = next;
        }
    }
    internal PartyDebuffNotice[] Poll(long now, int threshold, bool enabled)
    {
        threshold = Math.Clamp(threshold, 2, 10);
        var groups = slots.Where(p => !p.Value.Removed && (p.Value.Deadline == null || p.Value.Deadline > now))
            .GroupBy(p => p.Value.Effect).Select(g => (Effect: g.Key, Count: g.Select(p => p.Key.Target).Distinct().Count()))
            .Where(g => g.Count >= threshold).ToArray();
        var notices = new List<PartyDebuffNotice>();
        foreach (var group in groups) {
            if (!enabled || aboveThreshold.Contains(group.Effect) || lastAlert.TryGetValue(group.Effect, out long previous) && now - previous < 10000) continue;
            lastAlert[group.Effect] = now;
            notices.Add(new(group.Effect, EffectCatalogue.Default.Find(group.Effect)!.Name, group.Count));
        }
        aboveThreshold = groups.Select(g => g.Effect).ToHashSet();
        return notices.ToArray();
    }
}
