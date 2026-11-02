namespace AionDpsMeter.Local;

internal sealed record PetLevelNotice(int PetId, int Level, long Timestamp, bool Acquired = false);
internal sealed class PetLevelNotices
{
    readonly Queue<PetLevelNotice> pending = new();
    internal int Count => pending.Count;
    internal void Clear() => pending.Clear();
    internal void Observe(PetCollectionSnapshot? before, PetCollectionSnapshot? after, string kind)
    {
        if (kind is not ("pet-level" or "pet-added") || after == null || ReferenceEquals(before, after)) return;
        var previous = before?.Pets.ToDictionary(p => p.PetId) ?? new();
        foreach (var pet in after.Pets) {
            if (pet.Level is not int level || pet.LevelInferred) continue;
            var oldState = previous.GetValueOrDefault(pet.PetId);
            var old = oldState?.Level;
            bool confirmed = oldState?.LevelInferred == true && old == level;
            // A roster can already contain souls for an unowned pet (level 0).
            // Spending may also infer level 1 before its acquisition arrives.
            bool acquired = kind == "pet-added" && (old is null or 0 || confirmed && level == 1);
            if (!acquired && !confirmed && (old == null || level <= old)) continue;
            if (pending.Count >= 64) pending.Dequeue();
            pending.Enqueue(new(pet.PetId, level, after.Timestamp, acquired));
        }
    }
    internal PetLevelNotice[] Drain(long now)
    {
        var result = new List<PetLevelNotice>();
        while (pending.TryDequeue(out var notice))
            if (notice.Timestamp <= now && now - notice.Timestamp <= 30000) result.Add(notice);
        return result.ToArray();
    }
}
