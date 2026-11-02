using System.IO;

namespace AionDpsMeter.Local;

internal sealed record AchievementReading(bool All, int[] Completed);
internal static class AchievementWire
{
    internal static AchievementReading Read(ref WireReader reader, bool full)
    {
        int count = Count(ref reader, 16384);
        var values = new Dictionary<int, int>(count);
        for (int i = 0; i < count; i++) {
            int id = checked((int)reader.U32()); int completed = unchecked((int)reader.U32()); reader.Take(9);
            if (id <= 0) throw new InvalidDataException("Invalid achievement identity.");
            values[id] = completed;
        }
        if (full) {
            int groups = Count(ref reader, 4096), total = 0;
            for (int i = 0; i < groups; i++) {
                reader.Take(4); int steps = Count(ref reader, 4096); total = checked(total + steps);
                if (total > 131072) throw new InvalidDataException("Achievement reward flags exceed limit.");
                for (int j = 0; j < steps; j++) reader.Bit();
            }
        }
        if (reader.Remaining != 0) throw new InvalidDataException("Unknown achievement trailer.");
        return new(full, values.Where(p => p.Value > 0).Select(p => p.Key).ToArray());
    }
    static int Count(ref WireReader reader, int limit) {
        int count = reader.Var(); if (count > limit) throw new InvalidDataException("Achievement list exceeds limit."); return count;
    }
}
