using System.IO;
using System.Text;

namespace AionDpsMeter.Local;

internal static class ForceWire
{
    internal static CapturedPartyMember[] Full(ref WireReader r) => Full(ref r, out _);
    internal static CapturedPartyMember[] Full(ref WireReader r, out long leader)
    {
        byte flags = r.Byte(); if ((flags & ~3) != 0) throw new InvalidDataException("Unknown alliance flags.");
        r.Take(8); leader = checked((long)r.U64()); r.Take(Count(ref r, 64) * 8); int limit = checked((int)r.U32()); r.Take(3);
        if (limit is < 1 or > 64) throw new InvalidDataException("Invalid alliance capacity.");
        if ((flags & 1) != 0) for (int i = Count(ref r, 128); i > 0; i--) {
            byte f = r.Byte(); if ((f & ~1) != 0) throw new InvalidDataException("Unknown marker flags.");
            r.Take(5); new UTF8Encoding(false, true).GetString(r.Take(Count(ref r, 4096))); if ((f & 1) != 0) r.Take(4);
        }
        if ((flags & 2) != 0) for (int i = Count(ref r, 128); i > 0; i--) { r.Take(17); r.Bit(); }
        var members = new List<CapturedPartyMember>(); var groups = new HashSet<long>();
        for (int i = Count(ref r, 16); i > 0; i--) {
            long key = checked((long)r.U64()); r.Byte(); int capacity = checked((int)r.U32());
            if (capacity is < 1 or > 10 || !groups.Add(key)) throw new InvalidDataException("Invalid alliance subgroup.");
            int count = Count(ref r, capacity); if (members.Count + count > limit) throw new InvalidDataException("Alliance member limit exceeded.");
            for (int j = 0; j < count; j++) members.Add(PartyWire.Member(ref r, alliance: true) with { GroupKey = key });
        }
        r.Byte(); End(ref r); Validate(members); return members.ToArray();
    }
    internal static CapturedPartyMember Added(ref WireReader r, bool reconnect)
    {
        long key = reconnect ? 0 : checked((long)r.U64());
        var member = PartyWire.Member(ref r, alliance: true) with { GroupKey = key }; End(ref r); return member;
    }
    internal static long Removed(ref WireReader r) { long id = checked((long)r.U64()); r.Take(8); r.Byte(); End(ref r); return id; }
    internal static void Left(ref WireReader r, bool disband) { if (!disband) { r.Take(8); r.Byte(); } End(ref r); }
    internal static void Validate(IEnumerable<CapturedPartyMember> members)
    {
        var list = members.ToArray();
        if (list.Length > 64 || list.Select(m => m.Id).Distinct().Count() != list.Length ||
            list.Where(m => m.EntityId > 0).Select(m => m.EntityId).Distinct().Count() != list.Count(m => m.EntityId > 0))
            throw new InvalidDataException("Duplicate or excessive alliance members.");
    }
    static int Count(ref WireReader r, int max) { int n = r.Var(); if (n > max) throw new InvalidDataException("Alliance list exceeds limit."); return n; }
    static void End(ref WireReader r) { if (r.Remaining != 0) throw new InvalidDataException("Unknown alliance trailer."); }
}
