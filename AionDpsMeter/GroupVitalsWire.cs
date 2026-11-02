using System.IO;

namespace AionDpsMeter.Local;

internal sealed record GroupVitalsReading(int EntityId, long CurrentHp, long MaximumHp, int Mp, int MaximumMp, bool Alive);
internal sealed record GuardReading(int ActorId, byte State, int MaxGuard, int CurrentGuard,
    int AttackCount, long AttackDurationMs, long AttackExpiresAt, bool Paused, long CooldownExpiresAt, bool Activated);
internal static class GroupVitalsWire
{
    internal static GroupVitalsReading Read(ref WireReader r)
    {
        int entity = r.Var(); long hp = HitWire.Number(ref r), maxHp = HitWire.Number(ref r);
        int mp = unchecked((int)r.U32()), maxMp = unchecked((int)r.U32());
        r.Take(16); // AP / maximum AP / DP / maximum DP are not displayed.
        bool alive = r.Bit();
        if (entity <= 0 || hp < 0 || maxHp < 0 || mp < 0 || maxMp < 0 || r.Remaining != 0)
            throw new InvalidDataException("Invalid group vitals record.");
        return new(entity, hp, maxHp, mp, maxMp, alive);
    }
    internal static GuardReading Guard(ref WireReader r)
    {
        int entity = r.Var(); var result = GuardInfo(ref r, entity);
        if (r.Remaining != 0) throw new InvalidDataException("Unknown guard trailer.");
        return result;
    }
    internal static GuardReading GuardInfo(ref WireReader r, int entity)
    {
        byte flags = r.Byte(), state = r.Byte();
        if (entity <= 0 || (flags & ~63) != 0) throw new InvalidDataException("Invalid guard header.");
        int maximum = (flags & 1) != 0 ? unchecked((int)r.U32()) : 0;
        int current = (flags & 2) != 0 ? unchecked((int)r.U32()) : 0;
        int count = (flags & 4) != 0 ? unchecked((int)r.U32()) : 0;
        long duration = (flags & 8) != 0 ? unchecked((long)r.U64()) : 0;
        long expires = (flags & 16) != 0 ? unchecked((long)r.U64()) : 0;
        bool paused = r.Bit();
        long cooldown = (flags & 32) != 0 ? unchecked((long)r.U64()) : 0;
        bool active = r.Bit();
        if (maximum < 0 || current < 0 || count < 0)
            throw new InvalidDataException("Invalid guard record.");
        return new(entity, state, maximum, current, count, duration, expires, paused, cooldown, active);
    }
}
