namespace AionDpsMeter.Local;

// Current identities only: no packet payloads or combat history are retained.
internal sealed class AbnormalAttribution
{
    internal const int Limit = 16384;
    internal sealed record Source(int Buff, int Skill, int Caster);
    sealed record Entry(long Timestamp, long? Deadline, Source? Source);
    readonly Dictionary<(int Target, int Slot), Entry> entries = new();
    string? connection;
    internal int Count => entries.Count;
    internal void Connection(string value) { if (connection == value) return; Clear(); connection = value; }
    internal void Clear() => entries.Clear();
    internal void RemoveEntity(int target, long time) {
        foreach (var key in entries.Where(p => p.Key.Target == target && p.Value.Timestamp <= time).Select(p => p.Key).ToArray())
            entries[key] = new(time, null, null);
    }
    internal void Add(int target, AbnormalReading buff, long time, long? expiresAt = null)
    {
        var key = (target, buff.Slot);
        if (entries.TryGetValue(key, out var old) && old.Timestamp > time) return;
        if (!entries.ContainsKey(key) && entries.Count >= Limit) entries.Clear();
        long? deadline = expiresAt ?? (buff.Duration > 0 ? time > long.MaxValue - buff.Duration ? long.MaxValue : time + buff.Duration : null);
        entries[key] = new(time, deadline, new(buff.AbnormalId, buff.Skill, buff.Caster));
    }
    internal void Remove(int target, int slot, long time)
    {
        var key = (target, slot);
        if (entries.TryGetValue(key, out var old) && old.Timestamp > time) return;
        if (!entries.ContainsKey(key) && entries.Count >= Limit) entries.Clear();
        entries[key] = new(time, null, null);
    }
    internal Source? Find(int target, int slot, long time) => entries.TryGetValue((target, slot), out var e) &&
        time >= e.Timestamp && (e.Deadline == null || time < e.Deadline) ? e.Source : null;
}
