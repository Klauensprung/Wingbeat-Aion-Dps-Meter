using System.IO;

namespace AionDpsMeter.Local;

internal sealed record DungeonStateReading(int MapId, byte State, long? StartedAt, long? Deadline);
internal sealed record DungeonReward(int ItemId, long Quantity);
internal sealed record DungeonResultReading(int MapId, bool Succeeded, int? ContentsId, int? ClearLevel, DungeonReward[] Rewards);
internal static class DungeonWire
{
    internal static DungeonStateReading State(ref WireReader reader)
    {
        int map = checked((int)reader.U32()); byte state = reader.Byte();
        long? start = Time(reader.U64()), deadline = Time(reader.U64());
        if (map <= 0 || state > 4 || reader.Remaining != 0 || start.HasValue && deadline < start)
            throw new InvalidDataException("Invalid dungeon state.");
        return new(map, state, start, deadline);
    }
    static long? Time(ulong value) => value == 0 ? null : value is >= 1577836800000UL and <= 4102444800000UL
        ? (long)value : throw new InvalidDataException("Invalid dungeon timestamp.");
    internal static DungeonResultReading Result(ref WireReader reader)
    {
        byte flags = reader.Byte(); int map = checked((int)reader.U32()); bool success = reader.Bit();
        if ((flags & ~3) != 0 || map <= 0) throw new InvalidDataException("Invalid dungeon result flags or map.");
        int? contents = (flags & 1) != 0 ? reader.Var() : null;
        int count = reader.Var();
        if (count > 4096 || count > reader.Remaining / 12) throw new InvalidDataException("Invalid dungeon reward count.");
        var rewards = new DungeonReward[count]; var ids = new HashSet<int>();
        for (int i = 0; i < count; i++) {
            int item = checked((int)reader.U32()); long quantity = checked((long)reader.U64());
            if (item <= 0 || !ids.Add(item)) throw new InvalidDataException("Invalid dungeon reward.");
            rewards[i] = new(item, quantity);
        }
        int? level = (flags & 2) != 0 ? reader.Var() : null;
        if (reader.Remaining != 0) throw new InvalidDataException("Unknown dungeon result trailer.");
        return new(map, success, contents, level, rewards);
    }
}
