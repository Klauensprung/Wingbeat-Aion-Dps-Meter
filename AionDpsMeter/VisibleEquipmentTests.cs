using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class VisibleEquipmentTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var engine = new CombatEngine(); engine.Apply(new("actor", Actor: new(1, "Player", true, false, 0, "Cleric", 1)));
        engine.Apply(new("build", Build: new(1, [new(17090000, 1, [])], true, "Test skill build")));
        void Update(long time, bool complete, params VisibleItem[] items) => engine.Apply(new("network", Network: new("visible-equipment", time, "test",
            JsonSerializer.SerializeToElement(new EquipmentReading(1, complete, items)))));
        Update(1000, true, new(1, 110220003, 0, 5, true), new(2, 110230033, 0, 3, false));
        var before = engine.Snapshot(); Update(1100, false, new VisibleItem(1, 110220014, 110230033, 6, true));
        Update(1050, false, new VisibleItem(2, 0, 0, 0, false));
        check(engine.Fight.Actors[1].VisibleEquipment is { Complete: true, Items.Length: 2 } &&
            engine.Fight.Actors[1].VisibleEquipment!.Items[0].Enchant == 6 && before.Actors[1].VisibleEquipment!.Items[0].Enchant == 5 &&
            engine.Fight.Builds[1].Skills.Single().Skill == 17090000 && engine.Fight.Network.Count == 0,
            "Equipment updates retain unchanged slots, ignore stale data and preserve skills/snapshots without raw event retention");
        Update(1200, false, new VisibleItem(2, 0, 0, 0, false));
        var store = new FightStore(Path.Combine(temp, "visible-equipment")); store.Save(engine.Snapshot());
        var saved = store.Load(Path.Combine(store.Root, engine.Fight.Id + ".json"));
        check(saved.Actors[1].VisibleEquipment!.Items.Length == 1 && VisibleEquipment.Rows(saved).Single() is { Enchant: 6, Skin: "Kromede Sword" },
            "Equipment removal and saved appearance IDs preserve item-name lookup and skin details");
        Update(1300, true);
        check(engine.Fight.Actors[1].VisibleEquipment is { Complete: true, Items.Length: 0 }, "Empty full appearance clears old items");
        var partial = VisibleEquipment.Apply(null, new("visible-equipment", 1, "test", JsonSerializer.SerializeToElement(new EquipmentReading(1, false, [new(1, 1, 0, 0, true)]))));
        check(partial is { Complete: false }, "Mid-capture equipment changes remain explicitly partial");
        byte[] DyedUpdate(bool display, bool remove, bool black, bool material) {
            var writer = new PartyWireTests.Writer(); writer.Var(1); writer.Int(100); writer.Var(1); writer.Raw(3);
            void DyedItem(bool after) {
                writer.Int(after ? 110220014 : 110220003); writer.Raw(5, 9); writer.Long(0); writer.Bool(false); writer.Int(after ? 110230033 : 0);
                writer.Var(after ? 2 : 0);
                if (after) for (int d = 0; d < 2; d++) {
                    writer.Raw((byte)(d == 0 ? 0 : 255), (byte)(black ? 0 : 255), (byte)(black ? 0 : 128), 0); writer.Bool(remove);
                    writer.Int(material ? 123 : 0); writer.Int(999); writer.Raw(3, 4, 5); writer.Bool(true); writer.Zero(8); writer.Raw(2); writer.Bool(true); writer.Zero(4);
                }
                writer.Var(0); writer.Bool(after && display); writer.Bool(false); writer.Bool(true); writer.Var(0);
            }
            DyedItem(false); DyedItem(true); return writer.Bytes.ToArray();
        }
        CapturedPayload DyePacket(byte[] body) { var f = new PartyWireTests.Writer(); f.Var(body.Length + 6); f.Raw(0x1c, 0x56); f.Raw(body); return new(13328, 50000, f.Bytes.ToArray()) { TimestampMs = 1000 }; }
        using var fixtures = new ReferencePacketFixtures(Path.Combine(temp, "reference-equipment-dyes.jsonl"));
        foreach (var dyeBody in new[] { DyedUpdate(true, false, false, false), DyedUpdate(true, true, false, true), DyedUpdate(true, false, true, true), DyedUpdate(true, false, true, false), DyedUpdate(false, false, false, true) }) fixtures.Add(DyePacket(dyeBody));
        var dyed = new EuGameDecoder().Decode(DyePacket(DyedUpdate(true, false, false, false))).Events.Single().Network!;
        var dyeReading = dyed.Fields.Deserialize<EquipmentReading>()!;
        check(dyeReading.Items.Single().Dyes is [{ Slot: 1, Red: 255, Green: 128, Blue: 0 }, { Slot: 256 }], "Equipment dye slots become one-based and preserve RGB across shared packed booleans");
        foreach (var (remove, material, expected) in new[] { (true, true, 0), (false, false, 0), (false, true, 2) }) {
            var item = new EuGameDecoder().Decode(DyePacket(DyedUpdate(true, remove, true, material))).Events.Single().Network!.Fields.Deserialize<EquipmentReading>()!.Items.Single();
            check(item.Dyes.Length == expected, "Removed or unset black dye is omitted while material-backed black remains present");
        }
        var appearance = new AppearancePresetTracker(); appearance.Follow(new(1, "Synthetic", true, false, 0, "Unknown", 2301));
        appearance.Apply(new("appearance-presets", 900, "test", JsonSerializer.SerializeToElement(new AppearancePresetReading([])))); appearance.Apply(dyed);
        var exported = appearance.Snapshot()!;
        check(exported.Equipment is [{ Item: 110220014, Skin: 110230033, Dyes.Length: 2 }] && !exported.EquipmentComplete, "Appearance export includes displayed equipment, skin and dyes with partial capture coverage");
        exported.Equipment![0].Dyes[0] = new(1, 0, 0, 0);
        check(appearance.Snapshot()!.Equipment![0].Dyes[0].Red == 255, "Exported dye arrays cannot mutate the live appearance tracker");
        appearance.Apply(new("visible-equipment", 1100, "test", JsonSerializer.SerializeToElement(new EquipmentReading(1, true, [dyeReading.Items[0] with { Display = false }]))));
        check(appearance.Snapshot() is { Equipment.Length: 0, EquipmentComplete: true }, "Hidden equipment is excluded from appearance export without losing complete-capture status");
        engine.Apply(new("network", Network: dyed with { Timestamp = 1400 })); var dyeStore = new FightStore(Path.Combine(temp, "dye-save")); dyeStore.Save(engine.Snapshot());
        var dyeSaved = dyeStore.Load(Path.Combine(dyeStore.Root, engine.Fight.Id + ".json"));
        check(dyeSaved.Actors[1].VisibleEquipment!.Items.Single().Dyes.Length == 2 && VisibleEquipment.Rows(dyeSaved).Single().Dyes.Contains("#FF8000"), "Equipment dye colours survive saved-fight reload and are readable in the equipment view");
        var w = new PartyWireTests.Writer(); w.Var(1); w.Int(100); w.Var(1); w.Raw(3);
        void Item(int id, bool display) { w.Int(id); w.Raw(5, 9); w.Long(0); w.Bool(false); w.Int(0); w.Var(0); w.Var(0); w.Bool(display); w.Bool(false); w.Bool(true); w.Var(0); }
        Item(110220003, false); Item(110220014, true); byte[] body = w.Bytes.ToArray();
        DecodeResult Decode(byte[] bytes) { var f = new PartyWireTests.Writer(); f.Var(bytes.Length + 6); f.Raw(0x1c, 0x56); f.Raw(bytes); return new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, f.Bytes.ToArray())); }
        var update = Decode(body).Events.Single().Network!.Fields.Deserialize<EquipmentReading>()!;
        check(update is { Complete: false, ActorId: 1 } && update.Items.Single() is { Slot: 3, ItemId: 110220014, Display: true } &&
            Enumerable.Range(0, body.Length).All(n => Decode(body[..n]).Events.Count == 0) && Decode([..body, 0]).Events.Count == 0,
            "Equipment change parses both entries with shared booleans, uses the authoritative changed slot and rejects every truncation");
    }
}
