using System.Text.Json;

namespace AionDpsMeter.Local;

internal sealed record GuardDisplay(int Current, int Maximum, bool Broken, long Timestamp)
{
    internal double Ratio => Maximum > 0 ? Math.Clamp((double)Current / Maximum, 0, 1) : 0;
    internal string Label => Broken ? "Stagger · Broken (last seen)" : $"Stagger {Current:N0} / {Maximum:N0} (last seen)";
}
internal static class CapturedGuard
{
    internal static Dictionary<int, GuardDisplay> Build(Fight fight)
    {
        var result = new Dictionary<int, GuardDisplay>();
        var reused = fight.Actors.Values.Where(a => a.OriginalEntityId > 0).Select(a => a.OriginalEntityId).ToHashSet();
        foreach (var e in fight.Network.Where(e => e.Kind == "guard" && e.Timestamp >= 0)) {
            GuardReading? guard;
            try { guard = e.Fields.Deserialize<GuardReading>(); } catch (JsonException) { continue; }
            if (guard is not { ActorId: > 0, MaxGuard: >= 0, CurrentGuard: >= 0 } || reused.Contains(guard.ActorId)) continue;
            if (!result.TryGetValue(guard.ActorId, out var old) || e.Timestamp >= old.Timestamp)
                result[guard.ActorId] = new(guard.CurrentGuard, guard.MaxGuard, guard.State == 3, e.Timestamp);
        }
        foreach (var e in fight.Network.Where(e => e.Kind is "local-zone" or "entity-removed" or "entity-death")) {
            int entity = e.Fields.ValueKind == JsonValueKind.Object && e.Fields.TryGetProperty("EntityId", out var value) &&
                value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int id) ? id : 0;
            foreach (int key in result.Where(p => (e.Kind == "local-zone" || p.Key == entity) &&
                (p.Value.Timestamp < e.Timestamp || e.Kind != "local-zone" && p.Value.Timestamp == e.Timestamp)).Select(p => p.Key).ToArray()) result.Remove(key);
        }
        foreach (int key in result.Where(p => p.Value.Maximum == 0).Select(p => p.Key).ToArray()) result.Remove(key);
        return result;
    }
}
