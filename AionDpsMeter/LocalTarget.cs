using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

public sealed record LocalTargetState(int EntityId, long Timestamp);
internal static class LocalTarget
{
    internal static LocalTargetState? Apply(LocalTargetState? previous, NetworkEvent item, int localPlayer = 0)
    {
        if (item.Kind == "entity-removed") {
            if (previous == null || item.Timestamp < previous.Timestamp || item.Fields.ValueKind != JsonValueKind.Object) return previous;
            return item.Fields.TryGetProperty("EntityId", out var removed) && removed.ValueKind == JsonValueKind.Number &&
                removed.TryGetInt32(out int entity) && entity == previous.EntityId ? new(0, item.Timestamp) : previous;
        }
        if (item.Kind == "target") {
            if (localPlayer <= 0 || !item.Fields.TryGetProperty("ActorId", out var actor) || !actor.TryGetInt32(out int actorId) || actorId != localPlayer) return previous;
            if (!item.Fields.TryGetProperty("TargetId", out var target) || !target.TryGetInt32(out int targetId) || targetId < 0 || item.Timestamp < 0)
                throw new InvalidDataException("Invalid target update.");
            return previous != null && previous.Timestamp > item.Timestamp ? previous : new(targetId, item.Timestamp);
        }
        if (item.Kind != "local-target") return previous;
        if (!item.Fields.TryGetProperty("EntityId", out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int id) || id < 0 || item.Timestamp < 0)
            throw new InvalidDataException("Invalid local target.");
        return previous != null && previous.Timestamp > item.Timestamp ? previous : new(id, item.Timestamp);
    }
}
