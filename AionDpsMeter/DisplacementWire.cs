using System.IO;

namespace AionDpsMeter.Local;

internal sealed record DisplacementReading(int ActorId, int CasterId, int EffectId, float X, float Y, float Z,
    int DurationMs, byte MoveType, byte StartTerrain, byte EndTerrain);
internal static class DisplacementWire
{
    internal static DisplacementReading Read(ref WireReader r, bool timed)
    {
        int actor = r.Var(), caster = r.Var(), effect = unchecked((int)r.U32());
        float x = LocalPositionWire.Coordinate(ref r), y = LocalPositionWire.Coordinate(ref r), z = LocalPositionWire.Coordinate(ref r);
        int duration = timed ? checked((int)r.U32()) : 0;
        byte move = r.Byte(), start = r.Byte(), end = r.Byte();
        if (actor <= 0 || r.Remaining != 0) throw new InvalidDataException("Invalid displacement record.");
        return new(actor, caster, effect, x, y, z, duration, move, start, end);
    }
    internal static string? Name(long move) => move switch { 0 => "Push", 1 or 3 => "Knockback", 2 => "Pull", 4 => "Knockdown", _ => null };
}
