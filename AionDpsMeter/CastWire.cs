using System.IO;

namespace AionDpsMeter.Local;

internal sealed record CastReading(int ActorId, int SkillCode, byte Level, int TargetId,
    int CastingMs, float ChargeRate, byte Movement, int ActionId, int AttackSpeed) { public SkillAim? Aim { get; init; } }
internal sealed record CastStopReading(int ActorId, ushort Result, int SkillCode, int CooldownMs)
{
    public string Reason => Result switch { 0 => "cancelled", 6152 => "interrupted", _ => "failed" };
}
internal static class CastWire
{
    internal static CastReading Start(ref WireReader r, bool charge)
    {
        int actor = r.Var(); byte flags = r.Byte(); int skill = checked((int)r.U32()); byte level = r.Byte();
        if (actor <= 0 || skill <= 0 || (flags & ~(charge ? 3 : 7)) != 0) throw new InvalidDataException("Invalid cast header.");
        byte targetFlags = r.Byte(); int target = r.Var();
        if ((targetFlags & ~3) != 0) throw new InvalidDataException("Unknown cast target flags.");
        if ((targetFlags & 1) != 0) r.U32();
        var aim = SkillAim.Read(ref r, targetFlags);
        int time = 0, speed = 0; float rate = 0;
        if (charge) {
            if ((flags & 1) != 0) rate = BitConverter.Int32BitsToSingle(unchecked((int)r.U32()));
            if (!float.IsFinite(rate)) throw new InvalidDataException("Invalid charge rate.");
        } else time = r.Var();
        byte movement = r.Byte();
        int action = (flags & (charge ? 2 : 1)) != 0 ? r.Var() : 0;
        if (!charge) {
            if ((flags & 2) != 0) speed = r.Var();
            if ((flags & 4) != 0) {
                int count = r.Var();
                if (count > 1024 || count > r.Remaining / 13) throw new InvalidDataException("Invalid cast projectile list.");
                for (int i = 0; i < count; i++) { r.Var(); r.Take(12); }
            }
        }
        End(ref r);
        return new(actor, skill, level, target, time, rate, movement, action, speed) { Aim = aim };
    }
    internal static CastStopReading Stop(ref WireReader r)
    {
        int actor = r.Var(); ushort result = r.U16(); int skill = checked((int)r.U32()), cooldown = r.Var();
        if (actor <= 0 || skill <= 0) throw new InvalidDataException("Invalid cast stop identity.");
        End(ref r); return new(actor, result, skill, cooldown);
    }
    static void End(ref WireReader r) { if (r.Remaining != 0) throw new InvalidDataException("Unknown cast trailer."); }
}
