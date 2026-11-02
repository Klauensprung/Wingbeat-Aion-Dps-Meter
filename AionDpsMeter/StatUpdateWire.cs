using System.IO;

namespace AionDpsMeter.Local;

internal sealed record StatUpdateReading(int ActorId, Dictionary<ushort, int> Values, long MaximumHp);
internal static class StatUpdateWire
{
    internal static StatUpdateReading Read(ref WireReader reader)
    {
        int actor = reader.Var(), count = reader.Var();
        if (actor <= 0 || count > 2048 || count > reader.Remaining / 6) throw new InvalidDataException("Invalid stat update header.");
        var values = new Dictionary<ushort, int>();
        for (int i = 0; i < count; i++)
            if (!values.TryAdd(reader.U16(), unchecked((int)reader.U32()))) throw new InvalidDataException("Duplicate stat update.");
        long maximumHp = unchecked((long)reader.U64());
        if (maximumHp < 0 || reader.Remaining != 0) throw new InvalidDataException("Invalid stat update trailer.");
        return new(actor, values, maximumHp);
    }
}
