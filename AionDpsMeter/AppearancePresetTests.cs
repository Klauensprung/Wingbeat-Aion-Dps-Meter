using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class AppearancePresetTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        byte[] Body(ushort result, params (byte Slot, string Name, byte[] Data)[] rows) {
            var w = new PartyWireTests.Writer(); w.Raw(BitConverter.GetBytes(result)); w.Var(rows.Length);
            foreach (var row in rows) { w.Raw(row.Slot); w.Var(row.Data.Length); w.Raw(row.Data); w.Text(row.Name); w.Int(123); }
            return w.Bytes.ToArray();
        }
        CapturedPayload Packet(byte[] body) { var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw(0x9e, 0x56); w.Raw(body); return new(13328, 50000, w.Bytes.ToArray()) { TimestampMs = 100 }; }
        DecodeResult Decode(byte[] body) => new EuGameDecoder().Decode(Packet(body));
        var bytes = Body(0, (0, "Example", [0, 255, 128]), (255, "", []));
        var decoded = Decode(bytes).Events.Single().Network!;
        check(decoded.Fields.Deserialize<AppearancePresetReading>() is { Presets: [{ Slot: 0, Name: "Example", Data: "AP+A" }, { Slot: 255, Name: null, Data: "" }] }, "Appearance presets preserve slot, optional name and exact binary data");
        check(Decode(Body(1, (0, "Failed", [1]))).Events.Count == 0 && Decode(Body(0)).Events.Single().Network!.Fields.Deserialize<AppearancePresetReading>()!.Presets.Length == 0, "Only successful preset lists replace state, including an explicitly empty list");
        check(Enumerable.Range(0, bytes.Length).All(n => Decode(bytes[..n]).Events.Count == 0) && Decode([..bytes, 0]).Events.Count == 0, "Appearance presets reject every truncated body and unexpected trailer");
        check(Decode(Body(0, (0, "Too large", new byte[65537]))).Events.Count == 0 && Decode(Body(0, Enumerable.Range(0, 5).Select(i => ((byte)i, "Large", new byte[65536])).ToArray())).Events.Count == 0, "Appearance preset reader enforces both per-preset and total byte limits");
        using var fixtures = new ReferencePacketFixtures(Path.Combine(temp, "reference-appearance-presets.jsonl"));
        foreach (var b in new[] { bytes, Body(0), Body(2, (0, "Ignored", [1])), Body(0, (1, "Unicode \u00e9\u2665", [1, 2, 3, 4])), Body(0, (2, "Large", new byte[65536])) }) fixtures.Add(Packet(b));
        var tracker = new AppearancePresetTracker(); tracker.Apply(decoded);
        check(tracker.Snapshot() == null, "Unassigned presets are not shown under a cached or previous character");
        var actor = new Actor(1, "Example", true, false, 0, "Unknown", 2301) { PersistentId = 650000000000000001 };
        tracker.Follow(actor); var snapshot = tracker.Snapshot()!; snapshot.Presets[0] = new(0, "Changed", "");
        check(tracker.Snapshot()!.Presets[0].Name == "Example" && tracker.Snapshot()!.CharacterId == "650000000000000001", "Preset snapshots isolate their arrays and preserve full character identity as text");
        tracker.Apply(new("appearance-presets", 99, "test", JsonSerializer.SerializeToElement(new AppearancePresetReading([]))));
        check(tracker.Snapshot()!.Presets.Length == 2, "Older preset lists cannot overwrite current state");
        tracker.Follow(actor with { Name = "Renamed" }); check(tracker.Snapshot()!.Presets.Length == 2, "Persistent character identity keeps presets through renames");
        tracker.Follow(actor with { PersistentId = actor.PersistentId + 1 }); check(tracker.Snapshot() == null, "A different character cannot inherit presets from a namesake");
        tracker.Clear(200); tracker.Apply(decoded); tracker.Follow(actor); check(tracker.Snapshot() == null, "Login boundary rejects delayed old-character presets");
        using var appearanceFixtures = new ReferencePacketFixtures(Path.Combine(temp, "reference-current-appearance.jsonl"));
        CapturedPayload LocalPacket(byte[] body) { var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw(0x33, 0x36); w.Raw(body); return new(13328, 50000, w.Bytes.ToArray()) { TimestampMs = 1000 }; }
        var currentEngine = new CombatEngine();
        foreach (var body in new[] {
            LocalCharacterWireTests.Body(1, "Synthetic", appearance: [0, 255, 128], race: 1, gender: 1, wing: 123, skin: 456),
            LocalCharacterWireTests.Body(1, "Synthetic", appearance: [1], race: 2, gender: 2),
            LocalCharacterWireTests.Body(1, "Synthetic", race: 0, gender: 0),
            LocalCharacterWireTests.Body(1, "Synthetic", appearance: new byte[65536], race: 3, gender: 3)
        }) appearanceFixtures.Add(LocalPacket(body));
        foreach (var e in new EuGameDecoder().Decode(LocalPacket(LocalCharacterWireTests.Body(1, "Synthetic", appearance: [0, 255, 128], race: 1, gender: 1, wing: 123, skin: 456))).Events) currentEngine.Apply(e);
        check(currentEngine.AppearancePresets.Snapshot() is { PresetsCaptured: false, Current: { Race: "light", Gender: "male", Data: "AP+A", WingId: 123, WingSkinId: 456 } }, "Local appearance is available before the preset list without claiming an empty list was captured");
        check(currentEngine.Fight.Network.All(e => e.Kind != "appearance-current"), "Current appearance bytes are excluded from raw fight history");
        void Wing(long time, int id, bool skin, int entity = 1) => currentEngine.Apply(new("network", Network: new("wing-change", time, "test", JsonSerializer.SerializeToElement(new { EntityId = entity, Skin = skin, WingId = id }))));
        Wing(1200, 789, true); Wing(1199, 999, true); Wing(1300, 999, true, 2);
        check(currentEngine.AppearancePresets.Snapshot()!.Current!.WingSkinId == 789, "Current appearance tracks local wing changes while ignoring older and other-character updates");
        currentEngine.Apply(new("network", Network: new("appearance-current", 1100, "test", JsonSerializer.SerializeToElement(new CurrentAppearance(1, "dark", "female", "AQ==", 1, 2)))));
        check(currentEngine.AppearancePresets.Snapshot()!.Current is { Race: "dark", Data: "AQ==", WingSkinId: 789 }, "Delayed appearance refresh cannot roll back a newer wing change");
        Wing(1400, 0, true); check(currentEngine.AppearancePresets.Snapshot()!.Current!.WingSkinId == 0, "Removing a wing skin clears its captured appearance value");
        var fullAppearance = LocalCharacterWireTests.Body(1, "Synthetic", all: true, appearance: [1, 2, 3]);
        check(Enumerable.Range(0, fullAppearance.Length).All(n => new EuGameDecoder().Decode(LocalPacket(fullAppearance[..n])).Events.Count == 0), "Truncated current appearance cannot publish partial character data");
        var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: actor)); engine.Apply(new("network", Network: decoded));
        check(engine.AppearancePresets.Snapshot()?.Presets.Length == 2 && engine.Fight.Network.Count == 0 && engine.Loot.Rows().Length == 0, "Presets integrate without raw fight history or loot");
        engine.NewFight("Reset"); check(engine.AppearancePresets.Snapshot()?.Presets.Length == 2, "Manual fight reset preserves current character presets");
        engine.Apply(new("network", Network: new("local-zone", 200, "test", JsonSerializer.SerializeToElement(new { AwaitingIdentity = true }))));
        check(engine.AppearancePresets.Snapshot() == null, "Map preparation hides old character presets until new identity and data arrive");
    }
}
