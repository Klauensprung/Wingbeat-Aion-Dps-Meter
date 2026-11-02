using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class WorldObjectTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        byte[] Body(byte flags, byte desc) {
            var w = new PartyWireTests.Writer(); w.Var(128); w.Raw(flags, desc);
            if ((desc & 1) != 0) w.Text("Discarded name"); w.Int(1234);
            if ((desc & 2) != 0) w.Raw(1); if ((desc & 4) != 0) w.Raw(2); if ((desc & 8) != 0) w.Zero(5); if ((desc & 16) != 0) w.Raw(1);
            foreach (float value in new[] { 1.25f, -2.5f, 3f }) w.Raw(BitConverter.GetBytes(value));
            w.Int(0); w.Raw(BitConverter.GetBytes(4f)); w.Raw(1); w.Bool(true); w.Raw(2); w.Long(12345); w.Int(0);
            if ((flags & 1) != 0) w.Long(999); w.Int(0); if ((flags & 2) != 0) w.Long(999);
            if ((flags & 4) != 0) w.Int(456); w.Raw(0, 3); if ((flags & 8) != 0) w.Int(789); w.Var(128);
            return w.Bytes.ToArray();
        }
        byte[] Frame(ushort op, byte[] body) { var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw((byte)(op >> 8), (byte)op); w.Raw(body); return w.Bytes.ToArray(); }
        DecodeResult Decode(ushort op, byte[] body) => new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, Frame(op, body)) { TimestampMs = 10 });
        using var fixtures = new ReferencePacketFixtures(Path.Combine(temp, "reference-world-objects-synthetic.jsonl"));
        foreach (byte flags in new byte[] { 0, 1, 2, 4, 8, 15 }) foreach (byte desc in new byte[] { 0, 31 }) {
            var body = Body(flags, desc); var reading = Decode(0x3436, body).Events.Single().Network!.Fields.Deserialize<WorldObjectReading>();
            check(reading is { EntityId: 128, ObjectId: 1234, X: 1.25f, Y: -2.5f, CanInteract: true, Phase: 3 } &&
                Enumerable.Range(0, body.Length).All(n => Decode(0x3436, body[..n]).Events.Count == 0) && Decode(0x3436, [..body, 0]).Events.Count == 0,
                $"World object flags {flags}/{desc} preserve fields and reject all truncations/trailers");
            fixtures.Add(new CapturedPayload(13328, 50000, Frame(0x3436, body)) { TimestampMs = 10 });
        }
        byte[] removal = [128, 1, 2, 3, 128, 1];
        check(Decode(0x3536, removal).Events.Single().Network!.Fields.Deserialize<WorldObjectRemoval>() is { EntityId: 128, State: 2, Reason: 3, ActionId: 128 } &&
            Enumerable.Range(0, removal.Length).All(n => Decode(0x3536, removal[..n]).Events.Count == 0), "World object removal consumes exact identity/state/reason/action fields");
        fixtures.Add(new CapturedPayload(13328, 50000, Frame(0x3536, removal)) { TimestampMs = 11 });
        var reused = Body(0, 0); BitConverter.GetBytes(5678).CopyTo(reused, 4);
        fixtures.Add(new CapturedPayload(13328, 50000, Frame(0x3436, reused)) { TimestampMs = 12 });
        fixtures.Add(new CapturedPayload(13328, 50000, Frame(0x3536, [127, 0, 0, 0])) { TimestampMs = 13 });
        fixtures.Add(new CapturedPayload(13328, 50000, Frame(0x3536, removal)) { TimestampMs = 14 });
        var tracker = new WorldObjectTracker();
        var spawn = Decode(0x3436, Body(15, 31)).Events.Single().Network!;
        var gone = Decode(0x3536, removal).Events.Single().Network! with { Timestamp = 11 };
        tracker.Apply(spawn); tracker.Apply(gone); tracker.Apply(spawn);
        check(tracker.Snapshot().Length == 0, "Removed object tombstone rejects an older respawn");
        tracker.Apply(spawn with { Timestamp = 12 });
        check(tracker.Snapshot().Length == 1, "Newer respawn restores the world object");
        tracker.Apply(new("local-zone", 20, "test", JsonSerializer.SerializeToElement(new { MapId = 1010 })));
        tracker.Apply(spawn);
        check(tracker.MapId == 1010 && tracker.Snapshot().Length == 0, "Zone boundary clears world objects and rejects prior-map traffic");
        for (int i = 1; i <= 4200; i++) tracker.Apply(new("world-object", 20 + i, "test", JsonSerializer.SerializeToElement(new WorldObjectReading(i, 1234, 1, 2, 3, 0, true, 0, 0, 0, 0))));
        check(tracker.RetainedEntries == 4096, "World object records and removal tombstones share a bounded 4096-entry cache");
        var engine = new CombatEngine(); engine.Apply(new("network", Network: spawn)); engine.NewFight("Next");
        check(engine.WorldObjects.Snapshot().Length == 1 && engine.Fight.Network.Count == 0,
            "Manual fight reset preserves current world objects without retaining packet history");
        var login = new NetworkEvent("account-login", 30, "test", JsonSerializer.SerializeToElement(new LoginReading(0, 2301, [])));
        engine.Apply(new("network", Network: new("local-zone", 20, "test", JsonSerializer.SerializeToElement(new { MapId = 1010 }))));
        engine.Apply(new("network", Network: spawn with { Timestamp = 25 }));
        engine.Apply(new("network", Network: login));
        check(engine.WorldObjects.MapId == 0 && engine.WorldObjects.RetainedEntries == 0,
            "Reconnect without a map packet clears previous-session world objects and tombstones");
        engine.Apply(new("network", Network: spawn with { Timestamp = 29 }));
        engine.Apply(new("network", Network: new("local-zone", 29, "test", JsonSerializer.SerializeToElement(new { MapId = 1010 }))));
        check(engine.WorldObjects.MapId == 0 && engine.WorldObjects.Snapshot().Length == 0,
            "Delayed pre-login map and object packets cannot restore the previous scene");
        engine.Apply(new("network", Network: spawn with { Timestamp = 31 }));
        engine.Apply(new("network", Network: login));
        check(engine.WorldObjects.Snapshot().Length == 1, "Duplicate login does not erase post-login object observations");
        engine.Apply(new("network", Network: new("gathered", 29, "test", JsonSerializer.SerializeToElement(new { EntityId = 128 }))));
        check(engine.Fight.GatheredObjects.Count == 0, "Delayed gathering cannot use a new-session object with a reused entity ID");
        engine.Apply(new("network", Network: new("gathered", 32, "test", JsonSerializer.SerializeToElement(new { EntityId = 128 }))));
        check(engine.Fight.GatheredObjects.Count == 0, "Fresh unconfirmed gathering remains separate from the previous fight");
        engine.Apply(new("local-player", Actor: new(1, "Incoming", true, true, 0, "Cleric", 2301)));
        check(engine.Fight.GatheredObjects.GetValueOrDefault(1234) == 1, "Fresh gathering resolves the new-session object once identity confirms");
        engine.Apply(new("network", Network: login with { Timestamp = 40 }));
        engine.Apply(new("network", Network: gone with { Timestamp = 39 }));
        check(engine.WorldObjects.RetainedEntries == 0 && engine.Fight.GatheredObjects.GetValueOrDefault(1234) == 1,
            "New login drops live objects and rejects stale removal without rewriting completed gathering summaries");
    }
}
