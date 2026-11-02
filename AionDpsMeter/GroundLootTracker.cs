using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal sealed record LootPickup(long Timestamp, uint DropId, int ItemId, long Quantity, uint CharacterId, int Server, int MapId = 0,
    long InventoryInstance = 0, string? Source = null, long Sequence = 0,
    string? InventorySession = null, string? CapturedCharacter = null);

internal sealed class GroundLootTracker
{
    const long Lifetime = 180000;
    readonly Dictionary<uint, (GroundDrop Drop, long Seen, bool Removed)> entries = new();
    uint character;
    int server;
    long latest;
    long discardedBefore = -1;
    internal int Count => entries.Count;
    internal void Clear() { entries.Clear(); character = 0; server = 0; latest = 0; discardedBefore = -1; }
    internal LootPickup[] Apply(NetworkEvent message, Actor? local)
    {
        if (message.Kind is not ("ground-drops" or "ground-removals")) return [];
        if (local?.CharacterId != character || local?.Server != server) {
            Clear(); character = local?.CharacterId ?? 0; server = local?.Server ?? 0;
        }
        if (character == 0 || server <= 0 || message.Timestamp < 0 || message.Timestamp <= discardedBefore || message.Timestamp < latest - Lifetime) return [];
        latest = Math.Max(latest, message.Timestamp);
        foreach (var key in entries.Where(p => p.Value.Seen < latest - Lifetime).Select(p => p.Key).ToArray()) entries.Remove(key);
        try {
            if (message.Kind == "ground-drops") {
                var batch = message.Fields.Deserialize<GroundDropBatch>();
                if (batch?.Drops is not { Length: > 0 and <= 255 } || batch.Flag is not (1 or 2) ||
                    batch.Drops.Any(d => d == null || d.DropId == 0 || d.ItemId <= 0 || d.Quantity > 1_000_000_000) ||
                    batch.Drops.Select(d => d.DropId).Distinct().Count() != batch.Drops.Length) return [];
                foreach (var drop in batch.Drops) {
                    if (drop.OwnerId != character || drop.Server != server || drop.Quantity == 0 ||
                        drop.ExpiresAt <= (ulong)message.Timestamp || drop.ExpiresAt - (ulong)message.Timestamp > Lifetime) continue;
                    // Keep removal tombstones: replaying a spawn must not count the same pickup twice.
                    if (entries.TryGetValue(drop.DropId, out var old) && (old.Seen >= message.Timestamp || old.Drop == drop)) continue;
                    Put(drop.DropId, drop, message.Timestamp, false);
                }
                return [];
            }
            var removed = message.Fields.Deserialize<GroundRemovalBatch>();
            if (removed?.Drops is not { Length: > 0 and <= 255 } || removed.Drops.Any(d => d == null || d.DropId == 0) ||
                removed.Drops.Select(d => d.DropId).Distinct().Count() != removed.Drops.Length) return [];
            var pickups = new List<LootPickup>();
            foreach (var gone in removed.Drops) {
                if (!entries.TryGetValue(gone.DropId, out var old) || old.Removed || message.Timestamp < old.Seen) continue;
                var drop = old.Drop;
                if (gone.SourceId != drop.SourceId || gone.Reference != drop.Reference || gone.OwnerId != character || gone.Server != server) continue;
                Put(gone.DropId, drop, message.Timestamp, true);
                if (removed.PickedUp && (ulong)message.Timestamp < drop.ExpiresAt)
                    pickups.Add(new(message.Timestamp, drop.DropId, drop.ItemId, drop.Quantity, character, server));
            }
            return pickups.ToArray();
        } catch (JsonException) { return []; }
    }
    void Put(uint id, GroundDrop drop, long seen, bool removed)
    {
        if (!entries.ContainsKey(id) && entries.Count >= 4096) {
            var oldest = entries.MinBy(p => p.Value.Seen);
            discardedBefore = Math.Max(discardedBefore, oldest.Value.Seen);
            entries.Remove(oldest.Key);
        }
        entries[id] = (drop, seen, removed);
    }
}
