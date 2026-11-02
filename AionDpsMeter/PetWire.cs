using System.IO;

namespace AionDpsMeter.Local;

internal sealed record CapturedPetLevel(int PetId, int Level, int EntityId = 0, PetGenusLevel? Genus = null);
internal sealed record PetGenusLevel(byte Type, int Level);
internal sealed record CapturedPetProgress(int PetId, int Souls);
internal sealed record CapturedPetRoster(int ActivePetId, CapturedPetLevel[] Pets, CapturedPetProgress[] Progress, int TrailingBytes)
{
    public PetGenus[]? Genera { get; init; }
}
public sealed record PetGenusEffect(byte Slot, byte Grade, ushort Stat, long Value, bool Locked, bool Activated);
public sealed record PetGenus(byte Type, int Level, byte Panel, PetGenusEffect[] Effects);
internal static class PetWire
{
    // Pinned public field-layout reference is recorded in THIRD-PARTY-NOTICES.
    // These bounded forms remain experimental until validated against EU captures.
    static int Id(uint value) => value is >= 1000 and <= 99999 ? (int)value : throw new InvalidDataException("Invalid pet identifier.");
    internal static CapturedPetRoster Roster(ref WireReader reader)
    {
        uint activeValue = reader.U32(); int active = activeValue == 0 ? 0 : Id(activeValue);
        int count = reader.Var();
        if (count > 2000 || count > reader.Remaining / 12) throw new InvalidDataException("Invalid pet roster length.");
        var levels = new CapturedPetLevel[count]; var ids = new HashSet<int>();
        for (int i = 0; i < count; i++) {
            int id = Id(reader.U32()); uint repeated = reader.U32(), level = reader.U32();
            if (repeated != id || level is < 1 or > 3 || !ids.Add(id)) throw new InvalidDataException("Invalid pet roster entry.");
            levels[i] = new(id, (int)level);
        }
        int progressCount = reader.Var();
        if (progressCount > 2000 || progressCount > reader.Remaining / 8) throw new InvalidDataException("Invalid pet progress list.");
        var progress = new CapturedPetProgress[progressCount]; ids.Clear();
        for (int i = 0; i < progressCount; i++) {
            int id = Id(reader.U32()); uint souls = reader.U32();
            if (souls > 1000000 || !ids.Add(id)) throw new InvalidDataException("Invalid pet progress entry.");
            progress[i] = new(id, (int)souls);
        }
        // Retain only selected-panel effects, not the complete board history.
        int crystals = Count(ref reader, 256, 5);
        var keys = new HashSet<byte>();
        for (int i = 0; i < crystals; i++) {
            if (!keys.Add(reader.Byte())) throw new InvalidDataException("Duplicate crystal type.");
            reader.Take(4);
        }
        int collections = Count(ref reader, 2000, 4);
        reader.Take(collections * 4);
        int boards = Count(ref reader, 256, 15); keys.Clear();
        var genera = new List<(byte Type, int Level, Dictionary<byte, PetGenusEffect[]> Panels)>();
        int totalSlots = 0;
        for (int i = 0; i < boards; i++) {
            byte type = reader.Byte();
            if (!keys.Add(type)) throw new InvalidDataException("Duplicate creature board.");
            reader.Byte(); int level = checked((int)reader.U32()); reader.Take(8); // Experience is not retained.
            int panels = Count(ref reader, 256, 2);
            var panelValues = new Dictionary<byte, PetGenusEffect[]>();
            for (int j = 0; j < panels; j++) {
                byte panel = reader.Byte();
                if (panelValues.ContainsKey(panel)) throw new InvalidDataException("Duplicate creature panel.");
                int slots = Count(ref reader, 256, 12);
                totalSlots += slots; if (totalSlots > 8192) throw new InvalidDataException("Pet board slots exceed limit.");
                var effects = new PetGenusEffect[slots]; var slotIds = new HashSet<byte>();
                for (int k = 0; k < slots; k++) {
                    byte slot = reader.Byte(), grade = reader.Byte(); ushort stat = reader.U16(); long value = unchecked((long)reader.U64());
                    bool locked = reader.Bit(), activated = reader.Bit();
                    if (!slotIds.Add(slot)) throw new InvalidDataException("Duplicate creature stat slot.");
                    effects[k] = new(slot, grade, stat, value, locked, activated);
                }
                panelValues.Add(panel, effects);
            }
            genera.Add((type, level, panelValues));
        }
        byte selectedPanel = reader.Byte();
        if (reader.Remaining != 0) throw new InvalidDataException("Unknown pet roster trailer.");
        return new(active, levels, progress, 0) { Genera = genera.Select(g => new PetGenus(g.Type, g.Level, selectedPanel, g.Panels.GetValueOrDefault(selectedPanel) ?? [])).ToArray() };
    }
    static int Count(ref WireReader reader, int maximum, int minimumBytes)
    {
        int count = reader.Var();
        if (count > maximum || count > reader.Remaining / minimumBytes)
            throw new InvalidDataException("Invalid pet knowledge collection length.");
        return count;
    }
    internal static CapturedPetProgress[] Progress(ref WireReader reader)
    {
        int count = reader.Var();
        if (count > 2000 || reader.Remaining != count * 8) throw new InvalidDataException("Unknown pet progress layout.");
        var values = new CapturedPetProgress[count]; var ids = new HashSet<int>();
        for (int i = 0; i < count; i++) {
            int id = Id(reader.U32()); uint gained = reader.U32();
            if (gained > 1000000 || !ids.Add(id)) throw new InvalidDataException("Invalid pet progress gain.");
            values[i] = new(id, (int)gained);
        }
        return values;
    }
    internal static CapturedPetLevel Acquired(ref WireReader reader)
    {
        int entity = reader.Var(), id = Id(reader.U32()); uint level = reader.U32();
        byte genus = reader.Byte(); int genusLevel = checked((int)reader.U32()); reader.Take(8); // Experience is not a soul count.
        if (entity <= 0 || level is < 1 or > 3 || reader.Remaining != 0) throw new InvalidDataException("Invalid pet acquisition.");
        return new(id, (int)level, entity, new(genus, genusLevel));
    }
    internal static CapturedPetLevel Level(ref WireReader reader)
    {
        if (reader.Remaining != 10) throw new InvalidDataException("Unknown pet level layout.");
        reader.Take(2); // Undocumented prefix, deliberately not interpreted.
        int id = Id(reader.U32()); uint level = reader.U32();
        if (level is < 1 or > 3) throw new InvalidDataException("Invalid pet level.");
        return new(id, (int)level);
    }
}
