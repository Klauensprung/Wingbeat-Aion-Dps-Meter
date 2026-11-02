using System.IO;
using System.Text;

namespace AionDpsMeter.Local;

internal sealed record CapturedPartyMember(long Id, int EntityId, ushort Server, string Name, int CombatPower,
    long Hp, long MaximumHp, int? Mp, int? MaximumMp, bool Online)
{
    internal long GroupKey { get; init; }
    internal int ClassId { get; init; }
    internal int Level { get; init; }
    internal int GearScore { get; init; }
    internal string Guild { get; init; } = "";
}
internal sealed record CapturedParty(long Key, CapturedPartyMember[] Members, long LeaderId = 0);
internal static class PartyWire
{
    static readonly UTF8Encoding Utf8 = new(false, true);
    internal static CapturedParty Full(ref WireReader r)
    {
        byte flags = r.Byte(); if ((flags & ~31) != 0) throw new InvalidDataException("Unknown party flags.");
        long key = checked((long)r.U64()); long leader = checked((long)r.U64()); int limit = checked((int)r.U32()); r.Take(4);
        if (limit is < 1 or > 10) throw new InvalidDataException("Invalid party capacity.");
        if ((flags & 1) != 0) { byte f = r.Byte(); if ((f & ~1) != 0) throw new InvalidDataException("Unknown focus flags."); r.Take(4); if ((f & 1) != 0) r.Take(4); r.Take(4); }
        if ((flags & 2) != 0) for (int i = Count(ref r, 128); i > 0; i--) {
            byte f = r.Byte(); if ((f & ~1) != 0) throw new InvalidDataException("Unknown marker flags."); r.Take(5); Text(ref r); if ((f & 1) != 0) r.Take(4);
        }
        if ((flags & 4) != 0) for (int i = Count(ref r, 128); i > 0; i--) { r.Take(17); r.Bit(); }
        if ((flags & 8) != 0) for (int i = Count(ref r, 128); i > 0; i--) { r.Byte(); r.Var(); Text(ref r); r.Take(4); Text(ref r); r.Take(16); }
        int count = Count(ref r, limit); var members = new CapturedPartyMember[count];
        for (int i = 0; i < count; i++) members[i] = Member(ref r) with { GroupKey = key };
        if ((flags & 16) != 0) for (int i = Count(ref r, 128); i > 0; i--) {
            byte f = r.Byte(); if ((f & ~1) != 0) throw new InvalidDataException("Unknown map-pin flags."); r.Byte(); Text(ref r); r.Take(32);
            if ((f & 1) != 0) r.Take(Count(ref r, 128) * 8);
        }
        r.Byte(); End(ref r);
        if (members.Select(m => m.Id).Distinct().Count() != members.Length ||
            members.Where(m => m.EntityId > 0).Select(m => m.EntityId).Distinct().Count() != members.Count(m => m.EntityId > 0))
            throw new InvalidDataException("Duplicate party identities.");
        return new(key, members, leader);
    }
    internal static CapturedPartyMember Added(ref WireReader r) { var member = Member(ref r); End(ref r); return member; }
    internal static (long Id, int CombatPower, int ClassId, int Level, int GearScore, string Guild) Status(ref WireReader r, bool alliance)
    {
        long id = checked((long)r.U64()); int classId = unchecked((int)r.U32()); r.Take(24); int level = checked((int)r.U32()); r.Take(4); int gearScore = unchecked((int)r.U32()); string guild = Text(ref r); r.Take(4);
        if (!alliance) r.Bit();
        int cp = checked((int)r.U64()); r.Byte(); End(ref r);
        if (id <= 0) throw new InvalidDataException("Invalid member status identity.");
        return (id, cp, classId, level, gearScore, guild);
    }
    internal static CapturedPartyMember Member(ref WireReader r, bool alliance = false)
    {
        byte flags = r.Byte(); if ((flags & ~(alliance ? 3 : 7)) != 0) throw new InvalidDataException("Unknown party member flags.");
        r.Byte(); int entity = checked((int)r.U32()); ushort server = r.U16(); r.U16(); Text(ref r); // Account identity is discarded.
        long id = checked((long)r.U64()); string name = Text(ref r); int classId = unchecked((int)r.U32());
        long hp = HitWire.Number(ref r), maxHp = HitWire.Number(ref r);
        int? mp = null, maxMp = null;
        if (alliance) r.Take(12); // Map and subzone; force members carry no resource fields here.
        else { mp = unchecked((int)r.U32()); maxMp = unchecked((int)r.U32()); r.Take(40); }
        r.Bit(); string guild = (flags & 1) != 0 ? Text(ref r) : ""; if ((flags & 2) != 0) r.Take(4);
        int level = checked((int)r.U32());
        bool online; int gearScore;
        if (alliance) { r.Take(4); online = r.Bit(); gearScore = unchecked((int)r.U32()); r.Take(12); }
        else { r.Take(4); gearScore = unchecked((int)r.U32()); r.Bit(); online = r.Bit(); }
        if ((flags & 4) != 0) for (int i = Count(ref r, 256); i > 0; i--) {
            byte f = r.Byte(); if ((f & ~1) != 0) throw new InvalidDataException("Unknown rebirth skill flags."); r.Take(5); if ((f & 1) != 0) r.Var();
        }
        int cp = checked((int)r.U64()); r.Byte();
        if (id <= 0 || string.IsNullOrWhiteSpace(name) || hp < 0 || maxHp < 0 || mp < 0 || maxMp < 0)
            throw new InvalidDataException("Invalid party member.");
        return new(id, entity, server, name, cp, hp, maxHp, mp, maxMp, online) { ClassId = classId, Level = level, GearScore = gearScore, Guild = guild };
    }
    internal static bool Left(ref WireReader r, ushort opcode)
    {
        bool left = true;
        switch (opcode) {
            case 0x1392: left = r.U16() == 0; break;
            case 0x1992: r.Bit(); break;
            case 0x1f92: r.Byte(); break;
            case 0x1f93: r.Take(8); r.Byte(); break;
            default: throw new InvalidDataException("Unknown party departure.");
        }
        End(ref r); return left;
    }
    static string Text(ref WireReader r) => Utf8.GetString(r.Take(Count(ref r, 4096))).TrimEnd('\0');
    static int Count(ref WireReader r, int max) { int n = r.Var(); if (n > max) throw new InvalidDataException("Party list exceeds limit."); return n; }
    static void End(ref WireReader r) { if (r.Remaining != 0) throw new InvalidDataException("Unknown party trailer."); }
}
