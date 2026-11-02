using System.IO;

namespace AionDpsMeter.Local;

internal sealed record WarpReading(int ActorId, float X, float Y, float Z, float Direction, byte MoveState,
    byte MoveSubstate, byte Reason, int SkillId, int EffectId, int Duration, bool Entering);
internal static class WarpWire
{
    internal static WarpReading Read(ref WireReader r, bool entering)
    {
        int actor = r.Var(); byte flags = r.Byte();
        if (actor <= 0 || (flags & ~(entering ? 15 : 3)) != 0) throw new InvalidDataException("Invalid warp header.");
        float x = LocalPositionWire.Coordinate(ref r), y = LocalPositionWire.Coordinate(ref r), z = LocalPositionWire.Coordinate(ref r);
        float direction = LocalPositionWire.Coordinate(ref r);
        byte state = !entering || (flags & 1) != 0 ? r.Byte() : (byte)0;
        byte substate = !entering || (flags & 2) != 0 ? r.Byte() : (byte)0;
        byte reason = entering ? (byte)0 : r.Byte();
        int skill = (flags & (entering ? 4 : 1)) != 0 ? unchecked((int)r.U32()) : 0;
        int effect = (flags & (entering ? 8 : 2)) != 0 ? unchecked((int)r.U32()) : 0;
        int duration = r.Var();
        if (r.Remaining != 0) throw new InvalidDataException("Unknown warp trailer.");
        return new(actor, x, y, z, direction, state, substate, reason, skill, effect, duration, entering);
    }
}
