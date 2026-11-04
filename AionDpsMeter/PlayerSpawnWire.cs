using System.IO;
using System.Text;

namespace AionDpsMeter.Local;

internal sealed record PlayerSpawnReading(Actor Actor, CharacterStateReading State, AbnormalReading[] Effects, Dictionary<ushort, int> Stats, VisibleItem[] Equipment) { public byte Reason { get; init; } }
internal static class PlayerSpawnWire
{
    static readonly UTF8Encoding Utf8 = new(false, true);
    internal static PlayerSpawnReading Read(ref WireReader r, long timestamp = 0)
    {
        int entity = r.Var(); uint flags = r.U32();
        if (entity <= 0 || (flags & ~0x7ffffffU) != 0) throw new InvalidDataException("Invalid player spawn header.");
        byte desc = r.Byte(); if ((desc & ~31) != 0) throw new InvalidDataException("Unknown player description flags.");
        string name = (desc & 1) != 0 ? Text(ref r) : ""; int template = unchecked((int)r.U32());
        byte race = (desc & 2) != 0 ? r.Byte() : (byte)0; if ((desc & 4) != 0) r.Byte();
        if ((desc & 8) != 0) r.Take(5); byte overrideRace = (desc & 16) != 0 ? r.Byte() : (byte)0;
        var state = CharacterStateWire.Read(ref r);
        ushort server = (flags & 1) != 0 ? r.U16() : (ushort)0;
        string guild = (flags & 2) != 0 ? PlayerMetadata.Guild(ref r) : "";
        var effects = AbnormalWire.List(ref r); var equipment = Equipment(ref r);
        if ((flags & 4) != 0) r.Byte(); if ((flags & 8) != 0) r.Byte();
        if ((flags & 16) != 0) r.Bit(); if ((flags & 32) != 0) r.Bit();
        if ((flags & 64) != 0) r.Byte(); if ((flags & 128) != 0) r.Take(12);
        if ((flags & 256) != 0) r.Take(8); if ((flags & 512) != 0) r.Take(2);
        if ((flags & 1024) != 0) r.Take(2); if ((flags & 2048) != 0) r.Take(8);
        if ((flags & 4096) != 0) r.Bit(); byte reason = (flags & 8192) != 0 ? r.Byte() : (byte)0;
        if ((flags & 16384) != 0) r.Bit();
        long persistentId = checked((long)r.U64());
        int count = Count(ref r, 2048); var stats = new Dictionary<ushort, int>(count);
        for (int i = 0; i < count; i++) if (!stats.TryAdd(r.U16(), unchecked((int)r.U32()))) throw new InvalidDataException("Duplicate spawn stat.");
        int level = checked((int)r.U32()); r.Take(4); int gearScore = checked((int)r.U32());
        if ((flags & 32768) != 0) r.Take(4); if ((flags & 65536) != 0) r.Take(4);
        if ((flags & 131072) != 0) r.Take(4);
        r.Take(Count(ref r, 65536));
        if ((flags & 262144) != 0) r.Byte(); if ((flags & 524288) != 0) r.Take(4);
        if ((flags & 1048576) != 0) Social(ref r);
        if ((flags & 2097152) != 0) r.Take(4); if ((flags & 4194304) != 0) r.Take(4);
        if ((flags & 8388608) != 0) r.Take(4); if ((flags & 16777216) != 0) r.Take(4);
        if ((flags & 33554432) != 0) r.Bit(); if ((flags & 67108864) != 0) r.Take(4);
        int cp = checked((int)r.U64()); r.Byte(); r.Bit();
        if (template <= 0 || r.Remaining != 0) throw new InvalidDataException("Invalid player spawn trailer.");
        return new(new(entity, string.IsNullOrWhiteSpace(name) ? $"Entity {entity}" : name, true, false, cp, CapturedClass.Name(template), server)
            { OverrideRace = overrideRace, Race = race switch { 1 => "light", 2 => "dark", _ => null }, PersistentId = persistentId, CpSource = "Player spawn packet", Metadata = new(level, guild, timestamp, timestamp, gearScore, timestamp) }, state, effects, stats, equipment) { Reason = reason };
    }
    internal static VisibleItem[] Equipment(ref WireReader r)
    {
        int count = Count(ref r, 128); var items = new VisibleItem[count];
        for (int i = 0; i < count; i++) items[i] = Item(ref r);
        if (items.Select(i => i.Slot).Distinct().Count() != count) throw new InvalidDataException("Duplicate visual slots.");
        return items;
    }
    internal static VisibleItem Item(ref WireReader r)
    {
            int id = checked((int)r.U32()); byte enchant = r.Byte(), slot = r.Byte(); r.Take(8); r.Bit(); int skin = checked((int)r.U32());
            var dyes = new List<VisibleDye>();
            for (int j = Count(ref r, 32); j > 0; j--) {
                byte dyeSlot = r.Byte(), red = r.Byte(), green = r.Byte(), blue = r.Byte(); bool remove = r.Bit();
                int material = unchecked((int)r.U32()); r.Take(7); r.Bit(); r.Take(9); r.Bit(); r.Take(4);
                if (!remove && (red != 0 || green != 0 || blue != 0 || material != 0)) dyes.Add(new(dyeSlot + 1, red, green, blue));
            }
            r.Take(Count(ref r, 32) * 4); bool display = r.Bit(); r.Bit(); r.Bit(); r.Var();
            return new(slot, id, skin, enchant, display) { Dyes = dyes.ToArray() };
    }
    static void Social(ref WireReader r)
    {
        byte flags = r.Byte(); if ((flags & ~15) != 0) throw new InvalidDataException("Unknown social action flags.");
        r.Take(7); r.Bit(); r.Take(4);
        if ((flags & 1) != 0) r.Take(8); if ((flags & 2) != 0) r.Take(4);
        if ((flags & 4) != 0) r.Take(8); if ((flags & 8) != 0) r.Take(4);
    }
    static string Text(ref WireReader r) => Utf8.GetString(r.Take(Count(ref r, 4096))).TrimEnd('\0');
    static int Count(ref WireReader r, int max) { int count = r.Var(); if (count > max) throw new InvalidDataException("Player spawn list exceeds limit."); return count; }
}
