namespace AionDpsMeter.Local;

internal sealed class BackAttackIndicator
{
    internal const int HoldMilliseconds = 1000;
    string? fightId;
    int localPlayer, processed;
    long? until;
    internal bool Active(long now) => until is long deadline && now < deadline;
    internal void Observe(Fight fight, long now, long eventNow)
    {
        if (fightId != fight.Id || localPlayer != fight.LocalPlayer || fight.Damage.Count < processed) {
            fightId = fight.Id; localPlayer = fight.LocalPlayer; processed = 0; until = null;
        }
        for (; processed < fight.Damage.Count; processed++) {
            var hit = fight.Damage[processed];
            if (localPlayer <= 0 || hit.ActorId != localPlayer || hit.IsHeal || hit.Damage <= 0 ||
                !hit.IsBackAttack || hit.IsFrontAttack) continue;
            // Do not flash old fights on opening the overlay or restoring an encounter.
            long age = eventNow - hit.Timestamp;
            if (age < 0 || age >= HoldMilliseconds) continue;
            until = now + HoldMilliseconds;
        }
    }
}
