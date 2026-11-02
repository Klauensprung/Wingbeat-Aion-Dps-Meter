using System.IO;

namespace AionDpsMeter.Local;

internal sealed record HitReading(int TargetId, int ActorId, int SkillCode, byte Action, byte HitType,
    int EffectId, int EffectSequence, int AttackSpeed, long Amount, long[] AdditionalHits,
    byte Angle, bool ShieldBlock, bool WeaponBlock, bool Perfect, bool HardHit, bool IronWall,
    bool Restoration, bool PerfectBlock, long RestoredHp, long HpDrain, long HpRecovery);

// Complete SkillEffect_NT layout observed in the installed 0.1.46 reference.
// Kept separate from semantic classification so layouts and totals can be audited.
internal static class HitWire
{
    internal static HitReading Read(ref WireReader r)
    {
        int target = r.Var(); ushort flags = r.U16(); int actor = r.Var(), skill = checked((int)r.U32()); byte action = r.Byte();
        if ((flags & ~0x3fff) != 0 || target <= 0 || actor <= 0 || skill <= 0) throw new InvalidDataException("Invalid hit identity or flags.");
        if ((flags & 1) != 0) r.Take(4);
        byte type = r.Byte(), angle = 0;
        bool shield = false, weapon = false, perfect = false, hard = false, iron = false, restoration = false, perfectBlock = false;
        long restored = 0;
        if ((flags & 2) != 0) {
            shield = r.Bit(); weapon = r.Bit(); perfect = r.Bit(); hard = r.Bit(); iron = r.Bit(); restoration = r.Bit();
            restored = Number(ref r); perfectBlock = r.Bit(); angle = r.Byte();
        }
        int effect = unchecked((int)r.U32()), effectSequence = unchecked((int)r.U32()), speed = r.Var();
        long amount = (flags & 4) != 0 ? Number(ref r) : 0;
        if ((flags & 8) != 0) r.Take(4);
        if ((flags & 16) != 0) r.Bit();
        long[] extra = [];
        if ((flags & 32) != 0) { extra = new long[Count(ref r)]; for (int i = 0; i < extra.Length; i++) extra[i] = Number(ref r); }
        if ((flags & 64) != 0) r.Take(Count(ref r) * 4);
        if ((flags & 128) != 0) r.Byte();
        if ((flags & 256) != 0) r.Take(Count(ref r) * 4);
        r.U16(); // Skill process ID, including its second byte even when zero.
        long drain = (flags & 512) != 0 ? Number(ref r) : 0;
        long recovery = (flags & 1024) != 0 ? Number(ref r) : 0;
        if ((flags & 2048) != 0) r.Take(4);
        if ((flags & 4096) != 0) r.Bit();
        if ((flags & 8192) != 0) r.Take(4);
        if (r.Remaining != 0) throw new InvalidDataException("Unknown hit trailer.");
        return new(target, actor, skill, action, type, effect, effectSequence, speed, amount, extra,
            angle, shield, weapon, perfect, hard, iron, restoration, perfectBlock, restored, drain, recovery);
    }
    static int Count(ref WireReader r) {
        int count = r.Var(); if (count > 4096) throw new InvalidDataException("Hit list exceeds bounds."); return count;
    }
    internal static long Number(ref WireReader r) {
        ulong value = 0;
        for (int shift = 0; shift <= 63; shift += 7) {
            byte b = r.Byte();
            if (shift == 63 && b > 1) throw new InvalidDataException("Hit number exceeds Int64 wire width.");
            value |= (ulong)(b & 127) << shift;
            if (b < 128) return unchecked((long)value);
        }
        throw new InvalidDataException("Unterminated hit number.");
    }
}
