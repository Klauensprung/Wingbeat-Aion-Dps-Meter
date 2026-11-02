using System.IO;

namespace AionDpsMeter.Local;

// These are observed state fields, not an assertion that a dungeon was cleared.
internal sealed record ZoneStateReading(int MapId, int State, long? ServerTimestampMs, int? Variant, ulong? Reference);
internal static class ZoneStateWire
{
    internal static ZoneStateReading Read(ref WireReader reader)
    {
        if (reader.Remaining is not (15 or 24)) throw new InvalidDataException("Unknown zone-state size.");
        uint map = reader.U32(); int timed = reader.Byte(); ulong stamp = reader.U64(); int state = reader.Byte(), extended = reader.Byte();
        if (map is 0 or > int.MaxValue || timed is not (0 or 1) || state is < 1 or > 3 || extended is not (0 or 1) ||
            (timed == 0 ? stamp != 0 : stamp is < 1577836800000UL or > 4102444800000UL))
            throw new InvalidDataException("Invalid zone-state fields.");
        if (extended == 0) {
            if (reader.Remaining != 0 || timed != 0 || state != 1) throw new InvalidDataException("Unknown ordinary zone state.");
            return new((int)map, state, null, null, null);
        }
        if (reader.Remaining != 9) throw new InvalidDataException("Truncated extended zone state.");
        int variant = reader.Byte(); ulong reference = reader.U64();
        if (variant is not (1 or 2)) throw new InvalidDataException("Unknown zone-state variant.");
        return new((int)map, state, timed == 0 ? null : (long)stamp, variant, reference);
    }
}
