using System.IO;

namespace AionDpsMeter.Local;

internal sealed record BarrierReading(int Uid, long Remaining);
internal static class BarrierWire
{
    internal static (int Target, BarrierReading[] Values) Read(ref WireReader reader)
    {
        int target = reader.Var(), count = reader.Var();
        if (target <= 0 || count > 4096 || count > reader.Remaining / 2) throw new InvalidDataException("Invalid barrier list.");
        var values = new BarrierReading[count]; var seen = new HashSet<int>();
        for (int i = 0; i < count; i++) {
            int uid = reader.Var(); long remaining = HitWire.Number(ref reader);
            if (remaining < 0 || !seen.Add(uid)) throw new InvalidDataException("Invalid barrier entry.");
            values[i] = new(uid, remaining);
        }
        if (reader.Remaining != 0) throw new InvalidDataException("Unknown barrier trailer.");
        return (target, values);
    }
}
