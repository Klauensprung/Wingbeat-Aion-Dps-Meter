using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

public sealed record StaggerBreak(int EntityId, int NpcId, long StartedAt, long? ObservedEnd);
internal sealed class StaggerBreakTracker
{
    internal const int Capacity = 2048;
    readonly List<StaggerBreak> rows = new();
    readonly Dictionary<int, long> active = new();
    long latest = -1, omittedThrough = -1;
    internal int Count => rows.Count;
    internal void ArchiveActor(int id, int archived)
    {
        for (int i = 0; i < rows.Count; i++) if (rows[i].EntityId == id) rows[i] = rows[i] with { EntityId = archived };
        active.Remove(id);
    }
    internal void Apply(NetworkEvent message, Func<int, int> bossNpc)
    {
        if (message.Timestamp < 0 || message.Timestamp < latest || message.Fields.ValueKind != JsonValueKind.Object) return;
        int Number(string key) => message.Fields.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int n) ? n : 0;
        if (message.Kind is "local-zone" or "account-login") { active.Clear(); latest = message.Timestamp; return; }
        if (message.Kind == "actor-position" && message.Fields.TryGetProperty("VisibilityReason", out _)) {
            active.Remove(Number("ActorId")); latest = message.Timestamp; return;
        }
        int actor;
        bool broken;
        if (message.Kind == "guard") { actor = Number("ActorId"); broken = Number("State") == 3; }
        else if (message.Kind is "entity-death" or "entity-removed") { actor = Number("EntityId"); broken = false; }
        else return;
        if (actor <= 0) return;
        latest = message.Timestamp;
        if (!broken) {
            if (active.Remove(actor, out long started)) {
                int index = rows.FindLastIndex(r => r.EntityId == actor && r.StartedAt == started);
                if (index >= 0) rows[index] = rows[index] with { ObservedEnd = message.Timestamp };
            }
            return;
        }
        if (active.ContainsKey(actor) || bossNpc(actor) is not > 0) return;
        if (rows.Count == Capacity) {
            var oldest = rows[0]; rows.RemoveAt(0); omittedThrough = Math.Max(omittedThrough, oldest.StartedAt);
            if (active.GetValueOrDefault(oldest.EntityId, -1) == oldest.StartedAt) active.Remove(oldest.EntityId);
        }
        rows.Add(new(actor, bossNpc(actor), message.Timestamp, null)); active[actor] = message.Timestamp;
    }
    internal (StaggerBreak[] Rows, bool Truncated) Snapshot(long start, long end) =>
        (rows.Where(r => r.StartedAt >= start && r.StartedAt <= end).ToArray(), omittedThrough >= start);
    internal static void Validate(StaggerBreak[] rows)
    {
        if (rows.Length > Capacity || rows.Any(r => r == null || r.EntityId <= 0 || r.NpcId <= 0 || r.StartedAt < 0 || r.ObservedEnd < r.StartedAt))
            throw new InvalidDataException("Invalid stagger break summary.");
    }
}
