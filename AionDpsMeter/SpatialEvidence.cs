using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal sealed record SpatialEntity(int Id, int NpcId, int ObjectId, int OwnerId, long At,
    double? X = null, double? Y = null, double? Z = null, long PositionAt = -1, bool Moving = false, bool Visible = true, int TargetId = 0,
    string Kind = "unknown", long? EnteredViewAt = null, long? LeftViewAt = null, bool DepartedAtEdge = false);
internal sealed record SpatialConstraint(int MapId, int EntityId, double X, double Y, long PositionAt, long Timestamp,
    int Radius, bool Moving, string Source);

// Latest evidence only. A proximity constraint is not a captured player position.
internal sealed class SpatialEvidence
{
    const int Limit = 4096;
    readonly Dictionary<int, SpatialEntity> entities = new();
    long boundary = -1;
    long constraintAt = -1;
    internal int MapId { get; private set; }
    internal long Boundary => boundary;
    internal int Count => entities.Count;
    internal IEnumerable<SpatialEntity> Entities => entities.Values;
    internal SpatialConstraint? LastConstraint { get; private set; }
    internal SpatialEntity? Find(int id) => entities.GetValueOrDefault(id);
    internal void Clear(bool zone) {
        entities.Clear(); LastConstraint = null; constraintAt = boundary;
        if (zone) { MapId = 0; boundary = -1; constraintAt = -1; }
    }
    void Put(SpatialEntity entity) {
        if (!entities.ContainsKey(entity.Id) && entities.Count >= Limit) {
            var oldest = entities.MinBy(p => p.Value.At);
            if (entity.At < oldest.Value.At) return;
            entities.Remove(oldest.Key);
        }
        entities[entity.Id] = entity;
    }
    void Constrain(int id, long at, int radius, string source) {
        if (MapId <= 0 || at < boundary || at < constraintAt || !entities.TryGetValue(id, out var entity) ||
            !entity.Visible || entity.At > at || entity.PositionAt > at || entity.X is not double x || entity.Y is not double y) return;
        LastConstraint = new(MapId, id, x, y, entity.PositionAt, at, radius, entity.Moving, source);
        constraintAt = at;
    }
    internal void Combat(DamageEvent hit, int localPlayer) {
        if (localPlayer <= 0 || hit.Timestamp < 0) return;
        int actor = hit.SourceActorId > 0 ? hit.SourceActorId : hit.ActorId;
        if (actor == localPlayer && hit.TargetId != localPlayer) Constrain(hit.TargetId, hit.Timestamp, 3500, "damage");
        else if (hit.TargetId == localPlayer && actor != localPlayer) Constrain(actor, hit.Timestamp, 3500, "damage");
    }
    internal void Apply(NetworkEvent message, int localPlayer) {
        if (message.Timestamp < 0 || message.Timestamp < boundary || message.Fields.ValueKind != JsonValueKind.Object) return;
        var fields = message.Fields; long at = message.Timestamp;
        int Number(string key) => fields.TryGetProperty(key, out var f) && f.ValueKind == JsonValueKind.Number && f.TryGetInt32(out int n) ? n : 0;
        bool Flag(string key) => fields.TryGetProperty(key, out var f) && f.ValueKind == JsonValueKind.True;
        bool Edge(string key) => fields.TryGetProperty(key, out var f) && f.ValueKind == JsonValueKind.Number && f.TryGetInt32(out int reason) && reason is 0 or 1;
        bool ready = boundary >= 0 && at - boundary >= 4000;
        bool Coordinate(string key, out double value) {
            value = 0;
            return fields.TryGetProperty(key, out var f) && f.ValueKind == JsonValueKind.Number && f.TryGetDouble(out value) && double.IsFinite(value) && Math.Abs(value) <= 10000000;
        }
        if (message.Kind == "local-zone") {
            int map = Number("MapId"); if (map <= 0 || at == boundary && map == MapId) return;
            Clear(zone: true); MapId = map; boundary = at; return;
        }
        if (message.Kind == "npc-talk") { if (localPlayer > 0 && Flag("Talking")) Constrain(Number("EntityId"), at, 500, "npc-talk"); return; }
        if (message.Kind == "gathered") { if (localPlayer > 0) Constrain(Number("EntityId"), at, 700, "gathered"); return; }
        int id = message.Kind is "actor-position" or "displacement" or "target" ? Number("ActorId") : Number("EntityId");
        if (id <= 0 || id == localPlayer) return;
        entities.TryGetValue(id, out var previous);
        if (previous?.At > at) return;
        var old = previous ?? new(id, 0, 0, 0, at);
        switch (message.Kind) {
            case "npc-spawn":
                // Reused entity IDs must not inherit another NPC's coordinates.
                bool same = old.Visible && old.NpcId == Number("NpcId");
                Put((same ? old : new(id, 0, 0, 0, at)) with { NpcId = Number("NpcId"), OwnerId = Number("OwnerId"), At = at }); break;
            case "world-object":
                if (Coordinate("X", out double ox) && Coordinate("Y", out double oy) && Coordinate("Z", out double oz))
                    Put(new(id, 0, Number("ObjectId"), 0, at, ox, oy, oz, at, Kind: "object",
                        EnteredViewAt: ready && Edge("Reason") && (ox != 0 || oy != 0) ? at : null));
                break;
            case "actor-position": case "displacement":
                if (!Coordinate("X", out double x) || !Coordinate("Y", out double y) || !Coordinate("Z", out double z)) return;
                bool spawnPosition = fields.TryGetProperty("VisibilityReason", out _);
                bool remainsDeparted = old.DepartedAtEdge && !spawnPosition;
                var positioned = (old.Visible || remainsDeparted ? old : new(id, 0, 0, 0, at)) with { At = at, X = x, Y = y, Z = z, PositionAt = at,
                    Moving = fields.TryGetProperty("Stopped", out _) && !Flag("Stopped"), Visible = !remainsDeparted,
                    LeftViewAt = remainsDeparted ? old.LeftViewAt : null, DepartedAtEdge = remainsDeparted,
                    Kind = fields.TryGetProperty("EntityKind", out var kind) && kind.ValueKind == JsonValueKind.String ? kind.GetString()! : old.Kind,
                    EnteredViewAt = fields.TryGetProperty("VisibilityReason", out _) ? ready && Edge("VisibilityReason") && (x != 0 || y != 0) ? at : null : old.EnteredViewAt };
                Put(positioned);
                if (localPlayer > 0 && positioned.OwnerId == localPlayer) Constrain(id, at, 1500, "owned-summon");
                break;
            case "entity-removed": case "world-object-removed":
                Put(old with { At = at, Visible = false, OwnerId = 0, TargetId = 0,
                    DepartedAtEdge = Edge("Reason"),
                    LeftViewAt = ready && Edge("Reason") && old.PositionAt >= 0 && old.X is double lx && old.Y is double ly && (lx != 0 || ly != 0) ? at : null });
                if (LastConstraint?.EntityId == id) LastConstraint = null;
                break;
            case "target":
                int target = Number("TargetId");
                if (localPlayer > 0 && target == localPlayer && old.TargetId != target && old.NpcId > 0) Constrain(id, at, 2000, "npc-target");
                Put(old with { At = at, TargetId = target }); break;
        }
    }
}
