using System.IO;
using System.Text;

namespace AionDpsMeter.Local;

internal sealed record NpcSpawnReading(int EntityId, int NpcId, int OwnerId, int Level, long CurrentHp, long MaximumHp,
    float X, float Y, float Z, AbnormalReading[] Effects, Dictionary<ushort, int> Stats, GuardReading? Guard, long BerserkAt) { public byte Reason { get; init; } }
internal static class NpcSpawnWire
{
    static readonly UTF8Encoding Utf8 = new(false, true);
    internal static NpcSpawnReading Read(ref WireReader r)
    {
        int entity = r.Var(); ushort flags = r.U16(); int npc = Description(ref r);
        var state = CharacterStateWire.Read(ref r);
        var effects = AbnormalWire.List(ref r);
        if ((flags & 1) != 0) r.Byte();
        if ((flags & 2) != 0) r.Byte();
        byte reason = (flags & 4) != 0 ? r.Byte() : (byte)0;
        if ((flags & 8) != 0) r.Bit();
        int owner = (flags & 16) != 0 ? checked((int)r.U32()) : 0;
        if ((flags & 32) != 0) Description(ref r);
        if ((flags & 64) != 0) { r.Take(8); Text(ref r); r.Take(17); Text(ref r); }
        int count = Count(ref r, 4096); var stats = new Dictionary<ushort, int>(count);
        for (int i = 0; i < count; i++) stats[r.U16()] = unchecked((int)r.U32());
        int level = unchecked((int)r.U32());
        if ((flags & 128) != 0) r.Take(8);
        if ((flags & 256) != 0) r.Take(4);
        var guard = (flags & 512) != 0 ? GroupVitalsWire.GuardInfo(ref r, entity) : null;
        if ((flags & 1024) != 0) r.Take(9);
        if ((flags & 2048) != 0) r.Take(8);
        if ((flags & 4096) != 0) r.Take(4);
        if ((flags & 8192) != 0) r.Take(4);
        r.Take(checked(Count(ref r, 4096) * 4));
        long berserk = (flags & 16384) != 0 ? unchecked((long)r.U64()) : 0;
        if ((flags & 32768) != 0) r.Take(4);
        if (entity <= 0 || npc <= 0 || r.Remaining != 0)
            throw new InvalidDataException("Invalid NPC spawn record.");
        return new(entity, npc, owner, level, state.Hp, state.MaximumHp, state.X, state.Y, state.Z, effects, stats, guard, berserk) { Reason = reason };
    }
    static int Description(ref WireReader r)
    {
        byte flags = r.Byte();
        if ((flags & ~31) != 0) throw new InvalidDataException("Unknown NPC description flags.");
        if ((flags & 1) != 0) Text(ref r);
        int id = unchecked((int)r.U32());
        if ((flags & 2) != 0) r.Byte();
        if ((flags & 4) != 0) r.Byte();
        if ((flags & 8) != 0) r.Take(5);
        if ((flags & 16) != 0) r.Byte();
        return id;
    }
    static void Text(ref WireReader r) { int length = Count(ref r, 4096); Utf8.GetString(r.Take(length)); }
    static int Count(ref WireReader r, int limit) { int n = r.Var(); if (n > limit) throw new InvalidDataException("NPC list exceeds bounds."); return n; }
}
