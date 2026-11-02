using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal sealed class FarmingSoulTracker(FarmingCounterLedger totals)
{
    long latest = -1;
    internal void Apply(NetworkEvent message)
    {
        if (message.Timestamp < 0 || message.Timestamp < latest || message.Kind is not ("pet-progress" or "pet-progress-decrease" or "pet-roster" or "pet-added" or "pet-level")) return;
        if (message.Kind != "pet-progress") { latest = message.Timestamp; return; }
        if (!message.Fields.TryGetProperty("Gains", out var data)) return;
        CapturedPetProgress[]? gains;
        try { gains = data.Deserialize<CapturedPetProgress[]>(); } catch (JsonException) { return; }
        if (gains is not { Length: <= 2000 } || gains.Any(g => g == null || g.PetId is < 1000 or > 99999 || g.Souls is < 0 or > 1000000) ||
            gains.Select(g => g.PetId).Distinct().Count() != gains.Length) return;
        latest = message.Timestamp;
        // Balance snapshots and spending never represent acquisitions. The
        // inventory's soul-item quantities are a separate farming category.
        foreach (var gain in gains) if (gain.Souls > 0) totals.Record(gain.PetId, gain.Souls);
    }
}
