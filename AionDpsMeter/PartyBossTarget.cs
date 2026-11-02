using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class PartyBossTarget
{
    internal static LocalTargetState? Apply(LocalTargetState? previous, NetworkEvent message)
    {
        if (message.Timestamp < 0 || previous != null && message.Timestamp < previous.Timestamp) return previous;
        if (message.Kind == "local-zone") return new(0, message.Timestamp);
        if (message.Kind == "entity-removed") {
            if (previous != null && message.Fields.TryGetProperty("EntityId", out var removed) && removed.TryGetInt32(out int id) && id == previous.EntityId)
                return new(0, message.Timestamp);
            return previous;
        }
        if (message.Kind != "party-boss-target") return previous;
        if (message.Fields.ValueKind != JsonValueKind.Object || !message.Fields.TryGetProperty("EntityId", out var target) ||
            !target.TryGetInt32(out int entity) || entity < 0) throw new InvalidDataException("Invalid party boss target.");
        return new(entity, message.Timestamp);
    }

    internal static string Label(Fight fight) => fight.PartyBossTarget is { EntityId: > 0 } target
        ? "Boss aggro: " + (fight.Actors.GetValueOrDefault(target.EntityId)?.Name ?? "Entity " + target.EntityId)
        : "";
}
