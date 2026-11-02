namespace AionDpsMeter.Local;

internal static class EngagedBoss
{
    internal static int Latest(Fight fight)
    {
        bool Friendly(int id) => id > 0 && (id == fight.LocalPlayer || fight.Actors.GetValueOrDefault(id)?.Party == true);
        var bosses = fight.Actors.Values.Where(CapturedBoss.IsBoss).Select(a => a.Id).ToHashSet();
        int latest = 0; long time = -1;
        void Observe(int id, long timestamp) {
            if (!bosses.Contains(id) || timestamp < 0) return;
            if (timestamp > time || timestamp == time && (latest == 0 || id < latest)) { latest = id; time = timestamp; }
        }
        foreach (var hit in fight.Damage.Where(h => h.Damage > 0 && !h.IsHeal)) {
            if (Friendly(hit.ActorId)) Observe(hit.TargetId, hit.Timestamp);
            if (Friendly(hit.TargetId)) Observe(hit.ActorId, hit.Timestamp);
        }
        foreach (var defense in fight.Defense) {
            if (Friendly(defense.ActorId)) Observe(defense.TargetId, defense.Timestamp);
            if (Friendly(defense.TargetId)) Observe(defense.ActorId, defense.Timestamp);
        }
        return latest;
    }
}
