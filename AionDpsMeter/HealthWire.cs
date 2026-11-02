using System.IO;

namespace AionDpsMeter.Local;

internal sealed record PointReading(int EntityId, Dictionary<byte, long> Values)
{
    internal long? CurrentHp => Values.TryGetValue(0, out long hp) ? hp : null;
    internal long? MaximumHp => Values.TryGetValue(7, out long hp) ? hp : null;
}
internal static class HealthWire
{
    internal static PointReading Read(ref WireReader reader)
    {
        int entity = reader.Var(); byte flags = reader.Byte();
        if (entity <= 0 || (flags & ~3) != 0) throw new InvalidDataException("Invalid point update header.");
        var values = new Dictionary<byte, long>();
        for (int width = 0; width < 2; width++) {
            if ((flags & (1 << width)) == 0) continue;
            int count = reader.Var();
            if (count > 256 || count > reader.Remaining / (width == 0 ? 5 : 9)) throw new InvalidDataException("Invalid point map count.");
            var seen = new HashSet<byte>();
            for (int i = 0; i < count; i++) {
                byte type = reader.Byte(); long value = width == 0 ? unchecked((int)reader.U32()) : unchecked((long)reader.U64());
                if (!seen.Add(type) || type is 0 or 7 && value < 0) throw new InvalidDataException("Invalid point map value.");
                values.TryAdd(type, value); // The reference gives the 32-bit map precedence.
            }
        }
        if (reader.Remaining != 0) throw new InvalidDataException("Unknown point update trailer.");
        return new(entity, values);
    }
}
