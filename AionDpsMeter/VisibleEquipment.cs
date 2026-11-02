using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

public sealed record VisibleDye(int Slot, byte Red, byte Green, byte Blue);
public sealed record VisibleItem(byte Slot, int ItemId, int SkinId, byte Enchant, bool Display)
{
    public VisibleDye[] Dyes { get; init; } = [];
}
public sealed record VisibleEquipmentSnapshot(long Timestamp, bool Complete, VisibleItem[] Items);
internal sealed record EquipmentReading(int ActorId, bool Complete, VisibleItem[] Items, int? GearScore = null);
internal sealed record VisibleEquipmentRow(string Character, string Slot, string Item, string Skin, int Enchant, string Visibility, string Coverage, string Dyes);
internal static class VisibleEquipment
{
    internal static EquipmentReading ReadUpdate(ref WireReader r)
    {
        int actor = r.Var(); int gearScore = checked((int)r.U32()); int count = r.Var();
        if (actor <= 0 || count > 128) throw new InvalidDataException("Invalid equipment update.");
        var items = new VisibleItem[count];
        for (int i = 0; i < count; i++) {
            byte slot = r.Byte(); PlayerSpawnWire.Item(ref r); items[i] = PlayerSpawnWire.Item(ref r) with { Slot = slot };
        }
        if (r.Remaining != 0) throw new InvalidDataException("Unknown equipment update trailer.");
        Validate(new(0, false, items)); return new(actor, false, items, gearScore);
    }
    internal static void Validate(VisibleEquipmentSnapshot value)
    {
        if (value.Timestamp < 0 || value.Items == null || value.Items.Length > 128 ||
            value.Items.Any(i => i == null || i.ItemId < 0 || i.SkinId < 0 || i.Dyes == null || i.Dyes.Length > 32 ||
                i.Dyes.Any(d => d == null || d.Slot is < 1 or > 256)) || value.Items.Select(i => i.Slot).Distinct().Count() != value.Items.Length)
            throw new InvalidDataException("Invalid visible equipment snapshot.");
    }
    internal static VisibleEquipmentSnapshot? Apply(VisibleEquipmentSnapshot? old, NetworkEvent e)
    {
        if (e.Kind != "visible-equipment" || e.Timestamp < 0 || e.Timestamp < old?.Timestamp) return old;
        var reading = e.Fields.Deserialize<EquipmentReading>();
        if (reading == null) return old;
        var incoming = new VisibleEquipmentSnapshot(e.Timestamp, reading.Complete, reading.Items); Validate(incoming);
        var slots = reading.Complete ? new Dictionary<byte, VisibleItem>() : old?.Items.ToDictionary(i => i.Slot) ?? new();
        foreach (var item in reading.Items) {
            if (item.ItemId == 0 && item.SkinId == 0) slots.Remove(item.Slot); else slots[item.Slot] = item;
        }
        var result = new VisibleEquipmentSnapshot(e.Timestamp, reading.Complete || old?.Complete == true, slots.Values.OrderBy(i => i.Slot).ToArray());
        Validate(result); return result;
    }
    internal static VisibleEquipmentRow[] Rows(Fight fight) => fight.Actors.Values.Where(a => a.VisibleEquipment != null)
        .OrderBy(a => a.Name).SelectMany(a => a.VisibleEquipment!.Items.Select(i => new VisibleEquipmentRow(a.Name,
            $"Visual slot {i.Slot}", i.ItemId == 0 ? "Not captured" : ItemNames.Default.Find(i.ItemId),
            i.SkinId == 0 ? "Default" : ItemNames.Default.Find(i.SkinId), i.Enchant, i.Display ? "Shown" : "Hidden",
            a.VisibleEquipment.Complete ? "Full appearance snapshot" : "Partial updates",
            string.Join(", ", i.Dyes.Select(d => $"{d.Slot}: #{d.Red:X2}{d.Green:X2}{d.Blue:X2}"))))).ToArray();
}
