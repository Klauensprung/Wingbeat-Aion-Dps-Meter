using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

public sealed record EquippedItem(long InstanceId, int ItemId, GearDetails Details);
public sealed record EquippedGearSnapshot(long Timestamp, bool Complete, EquippedItem[] Items);
internal static class EquippedGear
{
    internal static void Validate(EquippedGearSnapshot state)
    {
        if (state.Timestamp < 0 || state.Items == null || state.Items.Length > 128 || state.Items.Any(i => i == null || i.InstanceId <= 0 || i.ItemId <= 0 || i.Details == null) ||
            state.Items.Select(i => i.InstanceId).Distinct().Count() != state.Items.Length) throw new InvalidDataException("Invalid equipped gear snapshot.");
        foreach (var item in state.Items) {
            var d = item.Details;
            bool Stats(GearStat[]? stats, int maximum) => stats != null && stats.Length <= maximum && stats.All(s => s != null && s.Stat > 0 && s.Value != 0);
            if (d.ItemLevel < 0 || !Stats(d.MainStats, 256) || !Stats(d.Stats, 768) || !Stats(d.SoulStats, 256) ||
                d.Skills == null || d.Skills.Length > 768 || d.Skills.Any(s => s == null || s.Skill <= 0 || s.Level == 0) ||
                d.Manastones == null || d.Manastones.Length > 256 || d.Manastones.Any(s => s == null || s.Item <= 0 || s.Stat == 0) ||
                d.Godstones == null || d.Godstones.Length > 256 || d.Godstones.Any(id => id <= 0)) throw new InvalidDataException("Invalid equipment details.");
        }
    }
    internal static EquippedGearSnapshot? Apply(EquippedGearSnapshot? previous, NetworkEvent message)
    {
        if (message.Timestamp < 0 || previous != null && message.Timestamp < previous.Timestamp) return previous;
        var batch = message.Fields.Deserialize<InventoryBatch>() ?? throw new InvalidDataException("Missing inventory batch.");
        if (batch.Stacks == null || batch.Stacks.Length > 8192 || batch.Stacks.Any(s => s == null || s.InstanceId <= 0 || s.ItemId <= 0 || s.Count < 0 || s.Modification > 2))
            throw new InvalidDataException("Invalid inventory batch for gear.");
        var items = batch.Snapshot ? new Dictionary<long, EquippedItem>() : previous?.Items.ToDictionary(i => i.InstanceId) ?? new();
        bool complete = batch.Snapshot || previous?.Complete == true;
        foreach (var stack in batch.Stacks) {
            if (stack.Modification == 2 || stack.Container != 11) { items.Remove(stack.InstanceId); continue; }
            if (stack.Equipment == null) { items.Remove(stack.InstanceId); complete = false; continue; }
            items[stack.InstanceId] = new(stack.InstanceId, stack.ItemId, stack.Equipment);
        }
        var next = new EquippedGearSnapshot(message.Timestamp, complete, items.Values.OrderBy(i => i.Details.Slot).ThenBy(i => i.InstanceId).ToArray());
        Validate(next); return next;
    }
}
