using System.IO;

namespace AionDpsMeter.Local;

internal sealed record FieldBossSlot(int Slot, bool Alive, float? X, float? Y, float? Z, long TimeMs);
internal sealed record FieldBossList(int MapId, FieldBossSlot[] Slots);
internal static class FieldBossWire
{
    // The map list identifies slots, not NPC templates. Never infer a boss name
    // by treating its slot number as an entity ID or NPC ID.
    internal static FieldBossList Read(ref WireReader source)
    {
        if (source.U16() != 0) throw new InvalidDataException("Unsuccessful field boss response.");
        int map = checked((int)source.U32());
        if (map is < 1000 or > 99999) throw new InvalidDataException("Invalid field boss map.");
        int count = Count(ref source);
        var slots = new FieldBossSlot[count]; var seen = new HashSet<int>();
        for (int i = 0; i < count; i++) {
            byte flags = source.Byte();
            if ((flags & ~1) != 0) throw new InvalidDataException("Unknown field boss flags.");
            int dataId = source.Var(), slot = dataId - map * 100;
            if (slot is < 1 or > 99 || !seen.Add(slot)) throw new InvalidDataException("Invalid field boss slot.");
            float? x = null, y = null, z = null;
            if ((flags & 1) != 0) { var pos = Position(ref source); x = pos.X; y = pos.Y; z = pos.Z; }
            bool alive = source.Bit();
            long time = unchecked((long)source.U64());
            if (time != 0 && time is < 1600000000000 or > 2600000000000) throw new InvalidDataException("Invalid field boss time.");
            slots[i] = new(slot, alive, x, y, z, time);
        }
        // The response also carries visible bosses, rifts and environment objects.
        // Consume their complete layouts; the timer overlay needs only named slots.
        int bosses = Count(ref source);
        for (int i = 0; i < bosses; i++) { source.Var(); source.U32(); Position(ref source); }
        int rifts = Count(ref source);
        for (int i = 0; i < rifts; i++) {
            source.Var(); source.U32(); source.U32(); Position(ref source); source.Var(); source.U64();
        }
        int objects = Count(ref source);
        for (int i = 0; i < objects; i++) { source.Var(); source.U32(); Position(ref source); }
        if (source.Remaining != 0) throw new InvalidDataException("Unknown field boss response trailer.");
        return new(map, slots);
    }
    static int Count(ref WireReader reader) {
        int count = reader.Var();
        if (count > 4096) throw new InvalidDataException("Field map list exceeds bounds.");
        return count;
    }
    static (float X, float Y, float Z) Position(ref WireReader reader) {
        float x = BitConverter.Int32BitsToSingle(unchecked((int)reader.U32()));
        float y = BitConverter.Int32BitsToSingle(unchecked((int)reader.U32()));
        float z = BitConverter.Int32BitsToSingle(unchecked((int)reader.U32()));
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) throw new InvalidDataException("Invalid field map position.");
        return (x, y, z);
    }
}
