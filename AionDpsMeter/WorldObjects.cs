using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal sealed record WorldObjectReading(int EntityId, int ObjectId, float X, float Y, float Z, byte State, bool CanInteract,
    byte Reason, long CreatedAt, int Spawner, byte Phase);
internal sealed record WorldObjectRemoval(int EntityId, byte State, byte Reason, int ActionId);
internal static class WorldObjectWire
{
    internal static WorldObjectReading Spawn(ref WireReader r)
    {
        int entity = r.Var(); byte flags = r.Byte(), desc = r.Byte();
        if ((flags & ~15) != 0 || (desc & ~31) != 0) throw new InvalidDataException("Unknown world object flags.");
        if ((desc & 1) != 0) { int bytes = r.Var(); if (bytes > 4096) throw new InvalidDataException("Object name exceeds limit."); r.Take(bytes); }
        int id = checked((int)r.U32());
        if ((desc & 2) != 0) r.Byte(); if ((desc & 4) != 0) r.Byte(); if ((desc & 8) != 0) r.Take(5); if ((desc & 16) != 0) r.Byte();
        float x = LocalPositionWire.Coordinate(ref r), y = LocalPositionWire.Coordinate(ref r), z = LocalPositionWire.Coordinate(ref r);
        r.Take(4); LocalPositionWire.Coordinate(ref r); byte state = r.Byte(); bool interact = r.Bit(); byte reason = r.Byte();
        long created = unchecked((long)r.U64()); r.Take(4);
        if ((flags & 1) != 0) r.Take(8); r.Take(4); if ((flags & 2) != 0) r.Take(8);
        int spawner = (flags & 4) != 0 ? unchecked((int)r.U32()) : 0;
        r.Byte(); byte phase = r.Byte(); if ((flags & 8) != 0) r.Take(4); r.Var();
        if (entity <= 0 || id <= 0 || r.Remaining != 0) throw new InvalidDataException("Invalid world object spawn.");
        return new(entity, id, x, y, z, state, interact, reason, created, spawner, phase);
    }
    internal static WorldObjectRemoval Remove(ref WireReader r)
    {
        var result = new WorldObjectRemoval(r.Var(), r.Byte(), r.Byte(), r.Var());
        if (result.EntityId <= 0 || r.Remaining != 0) throw new InvalidDataException("Invalid world object removal.");
        return result;
    }
}
internal sealed class WorldObjectTracker
{
    readonly Dictionary<int, (long At, WorldObjectReading? Object)> entries = new();
    long boundary = -1;
    internal int MapId { get; private set; }
    internal int RetainedEntries => entries.Count;
    internal void ResetSession(long timestamp)
    {
        if (timestamp < 0 || timestamp <= boundary) return;
        boundary = timestamp; MapId = 0; entries.Clear();
    }
    internal WorldObjectReading? Find(int entity, long timestamp) => timestamp >= boundary && entries.TryGetValue(entity, out var entry) && entry.At <= timestamp ? entry.Object : null;
    internal Dictionary<int,long> Around()=>entries.Values.Where(e=>e.Object!=null).GroupBy(e=>e.Object!.ObjectId).ToDictionary(g=>g.Key,g=>(long)g.Count());
    internal WorldObjectReading[] Snapshot() => entries.Values.Where(e => e.Object != null).Select(e => e.Object!).OrderBy(e => e.ObjectId).ThenBy(e => e.EntityId).ToArray();
    internal void Apply(NetworkEvent message)
    {
        if (message.Timestamp < 0 || message.Timestamp < boundary) return;
        if (message.Kind == "local-zone") {
            if (!message.Fields.TryGetProperty("MapId", out var map) || !map.TryGetInt32(out int id) || id <= 0) return;
            if (message.Timestamp == boundary && MapId == id) return;
            MapId = id; boundary = message.Timestamp; entries.Clear(); return;
        }
        WorldObjectReading? spawn = null; int entity;
        if (message.Kind == "world-object") { spawn = message.Fields.Deserialize<WorldObjectReading>()!; entity = spawn.EntityId; }
        else if (message.Kind == "world-object-removed") entity = message.Fields.Deserialize<WorldObjectRemoval>()!.EntityId;
        else return;
        if (entries.TryGetValue(entity, out var old) && old.At > message.Timestamp) return;
        if (!entries.ContainsKey(entity) && entries.Count >= 4096) {
            var oldest = entries.MinBy(e => e.Value.At);
            if (message.Timestamp < oldest.Value.At) return;
            entries.Remove(oldest.Key);
        }
        entries[entity] = (message.Timestamp, spawn);
    }
}
