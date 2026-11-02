using System.IO;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal sealed record GatheringRow(int ObjectId, decimal Count, int Records);
internal sealed record GatheringOverview(GatheringRow[] Rows, int Records, decimal OmittedGathers);
internal static class Gathering
{
    internal static GatheringOverview Summarize(IEnumerable<Fight> records)
    {
        // Last snapshot of an ID wins, so the live copy replaces an autosave.
        var unique = new Dictionary<string, Fight>(StringComparer.Ordinal);
        foreach (var record in records) unique[record.Id] = record;
        var totals = new Dictionary<int, (decimal Count, int Records)>();
        decimal omitted = 0; int participating = 0;
        foreach (var record in unique.Values) {
            if (record.GatheredObjects.Count > 0 || record.OmittedGathers > 0) participating++;
            omitted += record.OmittedGathers;
            foreach (var entry in record.GatheredObjects) {
                if (!totals.TryGetValue(entry.Key, out var old) && totals.Count >= 16384) { omitted += entry.Value; continue; }
                totals[entry.Key] = (old.Count + entry.Value, old.Records + 1);
            }
        }
        return new(totals.OrderBy(p => p.Key).Select(p => new GatheringRow(p.Key, p.Value.Count, p.Value.Records)).ToArray(), participating, omitted);
    }
    internal static void Merge(Fight destination, Fight source) {
        destination.OmittedGathers = checked(destination.OmittedGathers + source.OmittedGathers);
        foreach (var entry in source.GatheredObjects) {
            if (!destination.GatheredObjects.ContainsKey(entry.Key) && destination.GatheredObjects.Count >= 4096) { destination.OmittedGathers = checked(destination.OmittedGathers + entry.Value); continue; }
            destination.GatheredObjects[entry.Key] = checked(destination.GatheredObjects.GetValueOrDefault(entry.Key) + entry.Value);
        }
    }
    internal static int? Read(ref WireReader r)
    {
        r.Byte(); r.Byte(); int entity = r.Var(); r.Take(4);
        int basic = unchecked((int)r.U32()), additional = unchecked((int)r.U32()); r.Take(8);
        if (entity <= 0 || r.Remaining != 0) throw new InvalidDataException("Invalid gathering completion.");
        // The reference uses the main reward counts, not the result byte or the
        // separate bonus item. Inventory updates remain authoritative for loot.
        return (long)basic + additional > 0 ? entity : null;
    }
    internal static void Apply(Fight fight, WorldObjectTracker objects, NetworkEvent message) =>
        fight.OmittedGathers = Apply(fight.GatheredObjects, fight.OmittedGathers, objects, message);

    internal static long Apply(Dictionary<int, long> totals, long omitted, WorldObjectTracker objects, NetworkEvent message)
    {
        if (message.Kind != "gathered" || message.Timestamp < 0) return omitted;
        int entity = message.Fields.GetProperty("EntityId").GetInt32();
        var item = objects.Find(entity, message.Timestamp);
        if (item == null) return omitted;
        if (!totals.ContainsKey(item.ObjectId) && totals.Count >= 4096) return checked(omitted + 1);
        totals[item.ObjectId] = checked(totals.GetValueOrDefault(item.ObjectId) + 1);
        return omitted;
    }
}
