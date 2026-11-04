using System.IO;
using System.Text;

namespace AionDpsMeter.Local;

internal sealed record LocalCharacterReading(Actor Actor, CharacterStateReading State, int MapId,
    Dictionary<ushort, int> Stats, AbnormalReading[] Effects, VisibleItem[] Equipment, CurrentAppearance Appearance);
internal static class LocalCharacterWire
{
    static readonly UTF8Encoding Utf8 = new(false, true);
    internal static LocalCharacterReading Read(ref WireReader r, long timestamp = 0)
    {
        int entity = r.Var(); uint flags = r.U32();
        if (entity <= 0 || (flags & ~0x3fffffffU) != 0) throw new InvalidDataException("Invalid local character header.");
        byte detail = r.Byte(); if ((detail & ~63) != 0) throw new InvalidDataException("Unknown character detail flags.");
        string name = (detail & 1) != 0 ? Text(ref r) : ""; ushort server = r.U16(); int template = checked((int)r.U32());
        byte race = (detail & 2) != 0 ? r.Byte() : (byte)0;
        int level = checked((int)r.U32()), gearScore = checked((int)r.U32()); r.Take(28); byte gender = (detail & 4) != 0 ? r.Byte() : (byte)0;
        r.Take(Count(ref r, 256) * 14); r.Take(Count(ref r, 256) * 13);
        r.Take(17); byte overrideRace = (detail & 8) != 0 ? r.Byte() : (byte)0;
        if ((detail & 16) != 0) Points(ref r); if ((detail & 32) != 0) Points(ref r);
        var state = CharacterStateWire.Read(ref r);
        string guild = (flags & 1) != 0 ? PlayerMetadata.Guild(ref r) : "";
        int count = Count(ref r, 2048); var stats = new Dictionary<ushort, int>(count);
        for (int i = 0; i < count; i++) if (!stats.TryAdd(r.U16(), unchecked((int)r.U32()))) throw new InvalidDataException("Duplicate local stat.");
        var effects = AbnormalWire.List(ref r); var equipment = PlayerSpawnWire.Equipment(ref r);
        if ((flags & 2) != 0) r.Byte();
        r.Take(8); int map = checked((int)r.U32());
        if ((flags & 4) != 0) r.Take(4); if ((flags & 8) != 0) r.Take(4);
        if ((flags & 16) != 0) r.Take(12); if ((flags & 32) != 0) r.Take(2);
        long persistentId = checked((long)r.U64());
        if ((flags & 64) != 0) r.Take(4); if ((flags & 128) != 0) r.Take(4);
        if ((flags & 256) != 0) r.Byte(); if ((flags & 512) != 0) r.Byte();
        if ((flags & 1024) != 0) r.Byte(); if ((flags & 2048) != 0) r.Bit();
        if ((flags & 4096) != 0) r.Var();
        var appearanceData = r.Take(Count(ref r, 65536));
        string? appearance = appearanceData.Length == 0 ? null : Convert.ToBase64String(appearanceData); r.Bit();
        if ((flags & 8192) != 0) r.Byte(); if ((flags & 16384) != 0) r.Var();
        if ((flags & 32768) != 0) r.Take(4); if ((flags & 65536) != 0) r.Take(8);
        if ((flags & 131072) != 0) Pins(ref r);
        if ((flags & 262144) != 0) r.Take(4); if ((flags & 524288) != 0) r.Take(4);
        if ((flags & 1048576) != 0) r.Take(4); if ((flags & 2097152) != 0) r.Take(8);
        if ((flags & 4194304) != 0) { r.Take(10); Text(ref r); r.Take(5); }
        if ((flags & 8388608) != 0) r.Take(2);
        r.Take(16);
        if ((flags & 16777216) != 0) r.Bit(); if ((flags & 33554432) != 0) r.Take(8);
        if ((flags & 67108864) != 0) r.Take(4); if ((flags & 134217728) != 0) r.Take(16);
        int cp = checked((int)r.U64()); r.Take(8); Points(ref r); Points(ref r);
        if ((flags & 268435456) != 0) r.Take(Count(ref r, 4096) * 8);
        if ((flags & 536870912) != 0) r.Take(Count(ref r, 4096) * 8);
        if (string.IsNullOrWhiteSpace(name) || server == 0 || template <= 0 || map <= 0 || r.Remaining != 0)
            throw new InvalidDataException("Invalid local character record.");
        return new(new(entity, name, true, false, cp, CapturedClass.Name(template), server) { OverrideRace = overrideRace, Race = race switch { 1 => "light", 2 => "dark", _ => null }, PersistentId = persistentId, CpSource = "Local character packet", Metadata = new(level, guild, timestamp, timestamp, gearScore, timestamp) },
            state, map, stats, effects, equipment, new(entity, race == 0 ? null : race == 1 ? "light" : "dark", gender == 0 ? null : gender == 1 ? "male" : "female", appearance, state.WingId, state.WingSkinId));
    }
    static void Pins(ref WireReader r)
    {
        for (int i = Count(ref r, 128); i > 0; i--) {
            byte flags = r.Byte(); if ((flags & ~1) != 0) throw new InvalidDataException("Unknown map-pin flags.");
            r.Byte(); Text(ref r); r.Take(32); if ((flags & 1) != 0) r.Take(Count(ref r, 128) * 8);
        }
    }
    static void Points(ref WireReader r) => r.Take(Count(ref r, 4096) * 3);
    static string Text(ref WireReader r) => Utf8.GetString(r.Take(Count(ref r, 4096))).TrimEnd('\0');
    static int Count(ref WireReader r, int max) { int n = r.Var(); if (n > max) throw new InvalidDataException("Local character list exceeds limit."); return n; }
}
