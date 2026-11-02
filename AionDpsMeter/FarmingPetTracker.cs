using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

// Retains only a bounded current roster and spend-ordering evidence, never packets.
internal sealed class FarmingPetTracker(FarmingCounterLedger registered, FarmingCounterLedger levelUps)
{
    PetCollectionSnapshot? state;
    long latest = -1;
    internal void ResetSession(long at) { state = null; latest = Math.Max(latest, at); }
    internal void Apply(NetworkEvent message, int localPlayer = 0)
    {
        if (message.Timestamp < 0 || message.Timestamp < latest || message.Kind is not
            ("pet-roster" or "pet-added" or "pet-level" or "pet-progress-decrease")) return;
        PetCollectionSnapshot? next;
        try { next = PetCollection.Apply(state, message, localPlayer); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or OverflowException or KeyNotFoundException) { return; }
        if (next == null || ReferenceEquals(next, state)) return;
        // A roster establishes ownership; seeing already-owned pets is not farming.
        if (message.Kind != "pet-roster") {
            var previous = state?.Pets.ToDictionary(p => p.PetId) ?? new();
            foreach (var pet in next.Pets) {
                int old = previous.GetValueOrDefault(pet.PetId)?.Level ?? 0;
                int level = pet.Level ?? 0;
                if (level <= old) continue;
                if (old == 0) registered.Record(pet.PetId);
                int upgrades = level - Math.Max(1, old);
                if (upgrades > 0) levelUps.Record(pet.PetId, upgrades);
            }
        }
        state = next; latest = message.Timestamp;
    }
}
