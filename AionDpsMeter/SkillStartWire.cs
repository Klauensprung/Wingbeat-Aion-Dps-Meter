using System.IO;

namespace AionDpsMeter.Local;

internal sealed record SkillAim(float Yaw, float? X, float? Y, float? Z)
{
    internal static SkillAim Read(ref WireReader reader, byte flags) {
        float yaw = LocalPositionWire.Coordinate(ref reader);
        return (flags & 2) != 0 ? new(yaw, LocalPositionWire.Coordinate(ref reader), LocalPositionWire.Coordinate(ref reader), LocalPositionWire.Coordinate(ref reader)) : new(yaw, null, null, null);
    }
}
internal sealed record SkillStartReading(int ActorId, int SkillCode, byte Sequence, int TargetId, int CooldownMs,
    byte Charges, int RechargeMs) { public SkillAim? Aim { get; init; } }
internal static class SkillStartWire
{
    internal static SkillStartReading Read(ref WireReader reader)
    {
        int actor = reader.Var(); byte flags = reader.Byte(); int skill = checked((int)reader.U32()); byte sequence = reader.Byte();
        if (actor <= 0 || skill <= 0 || (flags & ~63) != 0) throw new InvalidDataException("Invalid skill start identity or flags.");
        if ((flags & 1) != 0) reader.Byte(); // Client action sequence.
        byte targetFlags = reader.Byte(); int target = reader.Var();
        if ((targetFlags & ~3) != 0) throw new InvalidDataException("Unknown skill target flags.");
        if ((targetFlags & 1) != 0) reader.Take(4); // Target part.
        var aim = SkillAim.Read(ref reader, targetFlags);
        reader.Var(); reader.Byte(); // Attack speed and movement type.
        if ((flags & 2) != 0) reader.Var(); // Skill action ID.
        int cooldown = reader.Var();
        byte charges = (flags & 4) != 0 ? reader.Byte() : (byte)0;
        int recharge = (flags & 8) != 0 ? reader.Var() : 0;
        if ((flags & 16) != 0) reader.Bit(); // Proxy skill.
        if ((flags & 32) != 0) {
            int count = reader.Var();
            if (count > 1024) throw new InvalidDataException("Too many projectile targets.");
            for (int i = 0; i < count; i++) { reader.Var(); reader.Take(12); }
        }
        if (reader.Remaining != 0 || cooldown > 86400000 || recharge > 86400000)
            throw new InvalidDataException("Invalid skill start timing or trailer.");
        return new(actor, skill, sequence, target, cooldown, charges, recharge) { Aim = aim };
    }
}
