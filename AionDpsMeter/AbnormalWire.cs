using System.Buffers.Binary;
using System.IO;

namespace AionDpsMeter.Local;

internal sealed record AbnormalReading(int Slot, int AbnormalId, long Duration, long Expires,
    int Caster, byte Level, int Skill, bool ReadyToRemove, int MultiplyCaster, int MultiplyValue,
    float? X, float? Y, float? Z);
internal sealed record AbnormalRemoval(int Slot, byte Reason, int Caster, int Skill, int Effect);

internal static class AbnormalWire
{
    internal static (int Target, AbnormalReading[] Values) Read(ref WireReader r, bool refresh)
    {
        int target = r.Var(), count = refresh ? 1 : Count(ref r);
        if (target <= 0) throw new InvalidDataException("Invalid abnormal target.");
        var values = Entries(ref r, count);
        if (refresh) r.Bit(); // Show-message shares the abnormal boolean cursor.
        End(ref r);
        return (target, values);
    }
    internal static AbnormalReading[] List(ref WireReader r) => Entries(ref r, Count(ref r));
    static AbnormalReading[] Entries(ref WireReader r, int count)
    {
        var values = new AbnormalReading[count];
        for (int i = 0; i < count; i++) {
            byte flags = r.Byte();
            if ((flags & ~31) != 0) throw new InvalidDataException("Unknown abnormal flags.");
            int slot = r.Var(), id = unchecked((int)r.U32());
            long duration = unchecked((long)r.U64()), expires = unchecked((long)r.U64());
            int caster = (flags & 1) != 0 ? r.Var() : 0;
            byte level = r.Byte();
            int skill = (flags & 2) != 0 ? unchecked((int)r.U32()) : 0;
            bool ready = r.Bit();
            int multiplyCaster = (flags & 4) != 0 ? r.Var() : 0;
            int multiply = (flags & 8) != 0 ? unchecked((int)r.U32()) : 0;
            float? x = null, y = null, z = null;
            if ((flags & 16) != 0) {
                float a = BinaryPrimitives.ReadSingleLittleEndian(r.Take(4));
                float b = BinaryPrimitives.ReadSingleLittleEndian(r.Take(4));
                float c = BinaryPrimitives.ReadSingleLittleEndian(r.Take(4));
                if (float.IsFinite(a) && float.IsFinite(b) && float.IsFinite(c)) { x = a; y = b; z = c; }
            }
            values[i] = new(slot, id, duration, expires, caster, level, skill, ready, multiplyCaster, multiply, x, y, z);
        }
        return values;
    }
    internal static (int Target, AbnormalRemoval[] Values) Remove(ref WireReader r)
    {
        int target = r.Var(), count = Count(ref r);
        if (target <= 0) throw new InvalidDataException("Invalid abnormal target.");
        var values = new AbnormalRemoval[count];
        for (int i = 0; i < count; i++) {
            byte flags = r.Byte();
            if ((flags & ~7) != 0) throw new InvalidDataException("Unknown abnormal removal flags.");
            int slot = r.Var(); byte reason = r.Byte();
            int caster = (flags & 1) != 0 ? r.Var() : 0;
            int skill = (flags & 2) != 0 ? unchecked((int)r.U32()) : 0;
            int effect = (flags & 4) != 0 ? unchecked((int)r.U32()) : 0;
            values[i] = new(slot, reason, caster, skill, effect);
        }
        End(ref r); return (target, values);
    }
    static int Count(ref WireReader r) { int count = r.Var(); if (count > 4096) throw new InvalidDataException("Abnormal list exceeds bounds."); return count; }
    static void End(ref WireReader r) { if (r.Remaining != 0) throw new InvalidDataException("Unknown abnormal trailer."); }
}
