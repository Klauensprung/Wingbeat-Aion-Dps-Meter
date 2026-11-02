namespace AionDpsMeter.Local;

// Called under engine.Gate with a snapshot from the same scene. Session identity
// is explicit because login packets intentionally do not survive in fight logs.
internal sealed class LiveOverlaySession
{
    WeakReference<CombatEngine>? owner;
    long revision = -1;
    internal void Observe(CombatEngine engine, Fight snapshot, CooldownTracker cooldowns, LiveEffectTracker effects,
        PartyDebuffTracker party, long utcNow, long monotonicNow)
    {
        if (owner == null || !owner.TryGetTarget(out var previous) || !ReferenceEquals(previous, engine) || revision != engine.LiveSceneRevision || !engine.LiveIdentityReady) {
            cooldowns.Reset(); effects.Reset(); party.Reset();
            owner = new(engine); revision = engine.LiveSceneRevision;
        }
        if (!engine.LiveIdentityReady) return;
        cooldowns.Observe(snapshot, utcNow, monotonicNow, engine.LiveIdentityBoundary);
        effects.Observe(snapshot, utcNow, monotonicNow, engine.LiveIdentityBoundary);
        party.Observe(snapshot, utcNow, monotonicNow, engine.LiveIdentityBoundary);
    }
}
