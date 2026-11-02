using System.IO;

namespace AionDpsMeter.Local;

internal sealed record PeriodicReading(int TargetId, int ActorId, int Uid, int EffectId,
    long Remaining, long Amount, int[] BarrierIds, int Skill, int BlockedActor, int BlockedSkill, int ReflectionSkill);

internal static class PeriodicWire
{
    internal static PeriodicReading Read(ref WireReader r)
    {
        int target = r.Var(); byte flags = r.Byte();
        int actor = r.Var(), uid = r.Var(), effect = unchecked((int)r.U32());
        if (target <= 0 || (flags & 128) != 0) throw new InvalidDataException("Invalid periodic effect header.");
        long remaining = (flags & 1) != 0 ? HitWire.Number(ref r) : 0;
        long amount = (flags & 2) != 0 ? HitWire.Number(ref r) : 0;
        int[] barriers = [];
        if ((flags & 4) != 0) {
            int count = r.Var();
            if (count > 4096 || count > r.Remaining / 4) throw new InvalidDataException("Invalid periodic barrier list.");
            barriers = new int[count];
            for (int i = 0; i < count; i++) barriers[i] = unchecked((int)r.U32());
        }
        int skill = (flags & 8) != 0 ? unchecked((int)r.U32()) : 0;
        int blockedActor = (flags & 16) != 0 ? r.Var() : 0;
        int blockedSkill = (flags & 32) != 0 ? unchecked((int)r.U32()) : 0;
        int reflection = (flags & 64) != 0 ? unchecked((int)r.U32()) : 0;
        if (r.Remaining != 0) throw new InvalidDataException("Unknown periodic effect trailer.");
        return new(target, actor, uid, effect, remaining, amount, barriers, skill, blockedActor, blockedSkill, reflection);
    }
}
