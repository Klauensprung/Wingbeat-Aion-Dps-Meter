using System.IO;

namespace AionDpsMeter.Local;

internal sealed record InventoryStack(long InstanceId, int ItemId, long Count, byte Container,
    byte Modification = 1, byte IncreaseReason = 0, byte DecreaseReason = 0, byte UpdateReason = 0)
{
    public GearDetails? Equipment { get; init; }
}
public sealed record GearStat(ushort Stat, int Value);
public sealed record GearSkill(int Skill, byte Level);
public sealed record GearStone(int Item, ushort Stat, byte Grade);
public sealed record GearDetails(byte Slot, byte Enchant, byte Surpass, int ItemLevel, GearStat[] MainStats,
    GearStat[] Stats, GearStat[] SoulStats, GearSkill[] Skills, GearStone[] Manastones, int[] Godstones);
internal sealed record InventoryBatch(bool Snapshot, InventoryStack[] Stacks, ushort Result = 0);

// Independently written reader of the inventory wire layout observed in Questlog
// desktop 0.1.46. See protocol/inventory-reference.json for evidence and limits.
internal static class InventoryWire
{
    internal static InventoryBatch Read(ref WireReader reader, bool snapshot)
    {
        ushort result = snapshot ? reader.U16() : (ushort)0;
        int count = Count(ref reader, 8192);
        var rows = new InventoryStack[count];
        for (int i = 0; i < count; i++) {
            byte modification = snapshot ? (byte)1 : reader.Byte();
            if (modification > 2) throw new InvalidDataException("Unknown inventory modification.");
            var row = Item(ref reader);
            if (!snapshot) row = row with { Modification = modification, IncreaseReason = reader.Byte(),
                DecreaseReason = reader.Byte(), UpdateReason = reader.Byte() };
            rows[i] = row;
        }
        if (snapshot) reader.Take(6); // Inventory/storage expansion counters.
        if (reader.Remaining != 0) throw new InvalidDataException("Unknown inventory trailer.");
        return new(snapshot, rows, result);
    }

    static InventoryStack Item(ref WireReader reader)
    {
        byte flags = reader.Byte();
        if ((flags & ~1) != 0) throw new InvalidDataException("Unknown inventory item flags.");
        long instance = checked((long)reader.U64());
        int item = checked((int)reader.U32());
        long quantity = checked((long)reader.U64());
        byte container = reader.Byte();
        reader.Take(4); // Slot index.
        reader.Bit(); // Locked. Boolean bits are shared throughout the packet.
        int nameBytes = Count(ref reader, 4096); reader.Take(nameBytes); // Do not retain maker identities.
        reader.Take(8); reader.Bit(); // Expiry and fixed-expiry flag.
        var equipment = (flags & 1) != 0 ? Equipment(ref reader, container == 11) : null;
        if (instance <= 0 || item <= 0) throw new InvalidDataException("Invalid inventory item identity.");
        return new(instance, item, quantity, container) { Equipment = equipment };
    }

    static GearDetails? Equipment(ref WireReader reader, bool retain)
    {
        if (retain) {
            byte enchant = reader.Byte(); reader.Take(8); byte surpass = reader.Byte(); reader.Take(4); int itemLevel = checked((int)reader.U32());
            reader.Bit(); byte slot = reader.Byte();
            var stones = new List<GearStone>();
            for (int i = Count(ref reader, 256); i > 0; i--) {
                int item = checked((int)reader.U32()); ushort stat = reader.U16(); byte grade = reader.Byte();
                if (item != 0 && stat != 0) stones.Add(new(item, stat, grade));
            }
            reader.Take(9);
            var gods = new List<int>(); for (int i = Count(ref reader, 256); i > 0; i--) { int id = unchecked((int)reader.U32()); if (id > 0) gods.Add(id); }
            var main = Stats(ref reader); var sub = Stats(ref reader); var added = Stats(ref reader);
            var skills = Skills(ref reader); var addedSkills = Skills(ref reader);
            var soul = Stats(ref reader); var soulSkills = Skills(ref reader); reader.Byte();
            return new(slot, enchant, surpass, itemLevel, main, [..sub, ..added, ..soul], soul, [..skills, ..addedSkills, ..soulSkills], stones.ToArray(), gods.ToArray());
        }
        reader.Take(18); // Enchant level/experience/chance, surpass level/chance, item level.
        reader.Bit(); reader.Byte(); // Broken flag and equipped slot.
        Entries(ref reader, 7); // Magic stones: item ID, stat type, grade.
        reader.Take(9); // Soul-bound character ID and bind count; not retained.
        Entries(ref reader, 4); // Godstones.
        Entries(ref reader, 6); Entries(ref reader, 6); Entries(ref reader, 6); // Main/sub/additional stats.
        Entries(ref reader, 5); Entries(ref reader, 5); // Sub/additional skills.
        Entries(ref reader, 6); Entries(ref reader, 5); // Soul stats and skills.
        reader.Byte(); // Skin extraction count.
        return null;
    }

    static GearStat[] Stats(ref WireReader reader)
    {
        var values = new List<GearStat>();
        for (int i = Count(ref reader, 256); i > 0; i--) {
            ushort stat = reader.U16(); int value = unchecked((int)reader.U32());
            if (stat != 0 && value != 0) values.Add(new(stat, value));
        }
        return values.ToArray();
    }
    static GearSkill[] Skills(ref WireReader reader)
    {
        var values = new List<GearSkill>();
        for (int i = Count(ref reader, 256); i > 0; i--) {
            int skill = unchecked((int)reader.U32()); byte level = reader.Byte();
            if (skill < 0 && level != 0) throw new InvalidDataException("Invalid equipment skill identity.");
            if (skill != 0 && level != 0) values.Add(new(skill, level));
        }
        return values.ToArray();
    }

    static int Count(ref WireReader reader, int maximum)
    {
        int count = reader.Var();
        if (count > maximum) throw new InvalidDataException("Inventory collection exceeds bounds.");
        return count;
    }
    static void Entries(ref WireReader reader, int size) => reader.Take(Count(ref reader, 256) * size);
}
