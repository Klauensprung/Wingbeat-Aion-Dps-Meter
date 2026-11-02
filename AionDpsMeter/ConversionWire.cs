using System.IO;

namespace AionDpsMeter.Local;

internal sealed record ConversionCount(int Recipe, int? Account, int? Character);
internal sealed record ConversionBatch(ConversionCount[] Counts);
internal static class ConversionWire
{
    internal static ConversionBatch? Read(ref WireReader r, bool full)
    {
        ushort result = r.U16(); var counts = new List<ConversionCount>();
        int Count(ref WireReader reader) { int n = reader.Var(); if (n > 4096) throw new InvalidDataException("Conversion count list exceeds limit."); return n; }
        if (full) {
            r.Take(2);
            for (int i = Count(ref r); i > 0; i--) counts.Add(new(checked((int)r.U32()), checked((int)r.U32()), null));
            for (int i = Count(ref r); i > 0; i--) counts.Add(new(checked((int)r.U32()), null, checked((int)r.U32())));
        } else {
            for (int i = Count(ref r); i > 0; i--) {
                int recipe = checked((int)r.U32()); r.Take(26);
                counts.Add(new(recipe, checked((int)r.U32()), checked((int)r.U32())));
            }
        }
        if (r.Remaining != 0 || counts.Any(c => c.Recipe <= 0)) throw new InvalidDataException("Invalid conversion count record.");
        return result != 0 || full && counts.Count == 0 ? null : new(counts.ToArray());
    }
}
