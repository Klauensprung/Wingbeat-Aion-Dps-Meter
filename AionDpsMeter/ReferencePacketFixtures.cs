using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

// Explicit, offline diagnostic export only. Never enabled by the live capture path.
internal sealed class ReferencePacketFixtures(string path) : IDisposable
{
    readonly StreamWriter output = new(path);
    readonly Dictionary<string, GameRecordStream> streams = new();
    readonly EuGameDecoder decoder = new();
    readonly InventoryLootTracker inventory = new();
    readonly WorldObjectTracker worldObjects = new();
    readonly Fight gathering = new();
    readonly MapEventTracker mapEvents = new();
    EquippedGearSnapshot? gear;
    CharacterCollectionSnapshot? collections;
    readonly Actor inventoryOwner = new(1, "Synthetic comparison owner", true, false, 0, "Unknown", 1);
    int count;
    internal void Add(CapturedPayload payload)
    {
        if (!streams.TryGetValue(payload.Connection, out var stream)) streams[payload.Connection] = stream = new();
        foreach (var record in stream.Push(payload.Data)) Read(record, payload, 0);
    }
    void Read(byte[] record, CapturedPayload payload, int depth)
    {
        if (depth > 4) throw new InvalidDataException("Fixture bundle depth exceeded.");
        var reader = new WireReader(record); reader.Var();
        ushort opcode = BinaryPrimitives.ReadUInt16BigEndian(reader.Take(2));
        if (opcode == 0xffff) {
            int size = checked((int)reader.U32());
            if (size > GameRecordStream.Limit * 8) throw new InvalidDataException("Fixture bundle exceeds limit.");
            var inner = new GameRecordStream();
            foreach (var child in inner.Push(Lz4Block.Expand(reader.Take(reader.Remaining), size))) Read(child, payload, depth + 1);
            if (inner.PendingBytes != 0) throw new InvalidDataException("Incomplete fixture bundle.");
            return;
        }
        // Feed context records too: periodic attribution depends on prior buffs.
        var decoded = decoder.Decode(payload with { Data = record });
        object? rawAbnormals = null;
        if (opcode is 0x2a38 or 0x2b38) {
            var batch = AbnormalWire.Read(ref reader, opcode == 0x2b38);
            rawAbnormals = new { batch.Target, batch.Values };
        } else if (opcode == 0x2c38) {
            var batch = AbnormalWire.Remove(ref reader);
            rawAbnormals = new { batch.Target, batch.Values };
        }
        if (opcode is not (0x05e0 or 0x0638 or 0x2a38 or 0x2b38 or 0x2c38 or 0x048d or 0x4936 or 0x4a36 or 0x008d or 0x0061 or 0x0161 or 0x0b61 or 0x0c61 or 0x0c38 or 0x2138 or 0x4136 or 0x0238 or 0x0938 or 0x1c38 or 0x1c56 or 0x9e56 or 0x4838 or 0x468d or 0x26e3 or 0x02e3 or 0x03e3 or 0x05e3 or 0x07e3 or 0x08e3 or 0x0a40 or 0x578d or 0x41e2 or 0x43e2 or 0x158d or 0x35e3 or 0x448d or 0x0297 or 0x0b97 or 0x1597 or 0x1697 or 0x1d97 or 0x2a97 or 0x9156 or 0x9556 or 0x8f8d or 0x8b8d or 0x8e56 or 0x8f56 or 0x018c or 0x038c or 0x1c8d or 0x1c92 or 0x2c96 or 0x338a or 0x918d or 0x4536 or 0x2d92 or 0x2e92 or 0x2f92 or 0x338d or 0x3436 or 0x3536 or 0x8356 or 0x8456 or 0x268d or 0x0092 or 0x1192 or 0x0d92 or 0x2292 or 0x1392 or 0x1992 or 0x1f92 or 0x1f93 or 0x0296 or 0x0a96 or 0x1b96 or 0x2296 or 0x1a96 or 0x2e96 or 0x2092 or 0x5b8d or 0xaf8a or 0x0be3 or 0x7956 or 0x3336 or 0x7256 or 0x7656 or 0x26e2 or 0x1838 or 0x0040 or 0x528d or 0x3538 or 0x3c37 or 0x3d37 or 0x1438 or 0x1638 or 0x2d38 or 0x0538 or 0x0438 or 0x1156 or 0x1b56 or 0x0090 or 0x0d90 or 0x0e90 or 0x0990 or 0x0051 or 0x0039 or 0x1338 or 0x0f38 or 0x5636)) return;
        if (++count > 10000) throw new InvalidDataException("Reference fixture limit exceeded.");
        var used = new List<InventoryUse>();
        var loot = decoded.Events.Where(e => e.Network != null).SelectMany(e => { var gains = inventory.Apply(e.Network!, inventoryOwner); used.AddRange(inventory.Uses); return gains; })
            .Select(g => new { g.ItemId, g.Quantity, g.Source }).ToArray();
        foreach (var message in decoded.Events.Select(e => e.Network).Where(e => e?.Kind is "inventory" or "inventory-update"))
            gear = EquippedGear.Apply(gear, message!);
        foreach (var message in decoded.Events.Select(e => e.Network).Where(e => e?.Kind == "achievements"))
            collections = CharacterCollections.Apply(collections, message!);
        foreach (var message in decoded.Events.Select(e => e.Network).Where(e => e?.Kind is "world-object" or "world-object-removed"))
            worldObjects.Apply(message!);
        foreach (var message in decoded.Events.Select(e => e.Network).Where(e => e?.Kind == "gathered"))
            Gathering.Apply(gathering, worldObjects, message!);
        var mapChanges = decoded.Events.Select(e => e.Network).Where(e => e != null).Select(e => mapEvents.Apply(e!)).Where(e => e != null).ToArray();
        output.WriteLine(JsonSerializer.Serialize(new { Opcode = opcode.ToString("X4"), Record = Convert.ToBase64String(record),
            SpawnVisibility = decoded.Events.Select(e => e.Network).FirstOrDefault(e => e?.Kind == "actor-position" && e.Fields.TryGetProperty("VisibilityReason", out _))?.Fields,
            Loot = loot,
            RawAbnormals = rawAbnormals,
            SpawnEffects = opcode == 0x3336 ? decoded.Events.Where(e => e.Network?.Kind == "abnormal-apply")
                .Select(e => e.Network!.Fields.EnumerateObject().Where(p => p.Name is not ("CasterX" or "CasterY" or "CasterZ"))
                    .ToDictionary(p => p.Name, p => p.Value)).ToArray() : null,
            FullStats = decoded.Events.Where(e => e.Stats != null).Select(e => new { e.Stats!.Values, e.Stats.IsComplete, e.Stats.MaximumHp }).ToArray(),
            DeathActors = opcode == 0x048d ? decoded.Events.Where(e=>e.Actor!=null).Select(e=>new {e.Actor!.Id,e.Actor.Name,e.Actor.Server}).ToArray() : null,
            Currencies = inventory.Currencies(),
            Tickets = inventory.Tickets(), TicketsComplete = inventory.TicketsComplete,
            Received = loot.GroupBy(g => g.ItemId).Select(g => new { ItemId = g.Key, Count = g.Sum(i => (decimal)i.Quantity) }).OrderBy(i => i.ItemId).ToArray(),
            Used = used.GroupBy(u => u.ItemId).Select(g => new { ItemId = g.Key, Count = g.Sum(u => (decimal)u.Count) }).OrderBy(u => u.ItemId).ToArray(),
            AppearanceEquipment = decoded.Events.Select(e => e.Network).Where(e => e?.Kind == "visible-equipment").SelectMany(e => e!.Fields.Deserialize<EquipmentReading>()!.Items).Where(i => i.Display && (i.ItemId != 0 || i.SkinId != 0)).Select(i => new { item = i.ItemId, skin = i.SkinId, dyes = i.Dyes.Select(d => new { slot = d.Slot, color = new[] { (int)d.Red, (int)d.Green, (int)d.Blue } }).ToArray() }).ToArray(),
            CurrentAppearance = decoded.Events.Select(e => e.Network).FirstOrDefault(e => e?.Kind == "appearance-current")?.Fields,
            ActorIdentities = decoded.Events.Where(e => e.Actor?.Player == true).Select(e => new { EntityId = e.Actor!.Id, PersistentId = e.Actor.PersistentId.ToString(System.Globalization.CultureInfo.InvariantCulture) }).ToArray(),
            ActorMetadata = decoded.Events.Where(e => e.Actor?.Metadata != null).Select(e => new { EntityId = e.Actor!.Id, e.Actor.Metadata!.Level, e.Actor.Metadata.Guild }).ToArray(),
            ActorGearScores = decoded.Events.Where(e => e.Actor?.Metadata?.GearScore != null).Select(e => new { EntityId = e.Actor!.Id, e.Actor.Metadata!.GearScore }).ToArray(),
            MapChanges = mapChanges,
            GatheredObjects = gathering.GatheredObjects,
            WorldObjects = opcode is 0x3436 or 0x3536 ? worldObjects.Snapshot() : null,
            Party = decoded.Events.Select(e => e.Party).LastOrDefault(p => p != null),
            Gear = gear?.Items ?? [],
            Achievements = collections?.Achievements?.Completed ?? [],
            Hit = opcode == 0x0438 ? HitWire.Read(ref reader) : null,
            Periodic = opcode == 0x0538 ? PeriodicWire.Read(ref reader) : null,
            Combat = decoded.Events.Where(e => e.Damage != null || e.Defense != null).Select(e => new { e.Kind, e.Damage, e.Defense }).ToArray(),
            Events = decoded.Events.Select(e => e.Network).Where(e => e != null && opcode != 0x4536 && (opcode != 0x3336 || e.Kind == "wing-change")).ToArray() }));
    }
    public void Dispose() => output.Dispose();
}
