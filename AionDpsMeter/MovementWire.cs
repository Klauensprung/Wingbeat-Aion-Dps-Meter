using System.IO;

namespace AionDpsMeter.Local;

internal sealed record MovementReading(int ActorId, float X, float Y, float Z, bool Stopped, ushort Rotation);
internal static class MovementWire
{
    internal static MovementReading Read(ref WireReader r, bool stopped)
    {
        int actor = r.Var(); byte flags = r.Byte();
        if (actor <= 0 || (flags & ~(stopped ? 15 : 7)) != 0) throw new InvalidDataException("Invalid movement header.");
        if (stopped) { r.Byte(); if ((flags & 1) != 0) r.Byte(); }
        else if ((flags & 1) != 0) r.Bit();
        float x = LocalPositionWire.Coordinate(ref r), y = LocalPositionWire.Coordinate(ref r), z = LocalPositionWire.Coordinate(ref r);
        if (!stopped) r.U16(); // Direction of movement.
        ushort rotation = r.U16();
        if ((flags & 2) != 0) {
            r.U32(); // Vessel identity and local frame, not world position.
            LocalPositionWire.Coordinate(ref r); LocalPositionWire.Coordinate(ref r); LocalPositionWire.Coordinate(ref r);
            if (!stopped) r.Var();
            r.Var();
        }
        if ((flags & 4) != 0) r.Bit();
        if (stopped && (flags & 8) != 0) r.U64();
        if (r.Remaining != 0) throw new InvalidDataException("Unknown movement trailer.");
        return new(actor, x, y, z, stopped, rotation);
    }
}
