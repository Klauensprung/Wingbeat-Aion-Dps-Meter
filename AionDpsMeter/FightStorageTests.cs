using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class FightStorageTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var fight = FeatureTests.Sample();
        void Add(string kind, long time, object fields) => fight.Network.Add(new(kind, time, "synthetic", JsonSerializer.SerializeToElement(fields)));
        for (int i = 0; i < 1000; i++) Add(i == 0 ? "abnormal-apply" : "abnormal-refresh", 1000 + i * 20,
            new { TargetId = 1, CasterId = 2, Slot = 1, AbnormalId = 1000, SourceSkillId = 18010001, DurationMs = 5000,
                CasterX = 999, DebugPayload = "discard-this-diagnostic" });
        Add("skill-cooldown", 1000, new { SkillCode = 17010001, RemainingMs = 5000 });
        Add("ground-drops", 1000, new { DebugPayload = "discard-this-diagnostic" });
        Add("scene-signal", 1000, new { RawId = 1000 });
        Add("hp", 1000, new { EntityId = 1, CurrentHp = 5000, Extra = "discard-this-diagnostic" });
        Add("loot-pickup", 2000, new LootPickup(2000, 1, 500000001, 3, 30, 1, 1010));
        var effects = EffectAnalysis.Build(fight, id => "Skill " + id);
        var expectedHealth = CapturedHealth.Build(fight);
        var storage = fight.ForStorage();
        check(storage.Network.Count == 1 && storage.SavedEffects is { Length: 1 } && storage.SavedEffects[0].Segments == 1000 &&
            ReferenceEquals(storage.Damage, fight.Damage) && fight.Network.Count == 1005 && fight.SavedEffects == null,
            "Storage replaces repetitive protocol logs with effect summaries without copying combat lists or mutating the live snapshot");
        var store = new FightStore(Path.Combine(temp, "summary-storage")); store.Save(fight);
        string path = Path.Combine(store.Root, fight.Id + ".json"); var restored = store.Load(path);
        check(EffectAnalysis.Build(restored, id => "Skill " + id).SequenceEqual(effects) &&
            CapturedHealth.Build(restored).OrderBy(p => p.Key).SequenceEqual(expectedHealth.OrderBy(p => p.Key)) &&
            Analysis.Totals(restored).SequenceEqual(Analysis.Totals(fight)) && LootLedger.FromFight(restored).Rows().Single().Quantity == 3,
            "Saved summaries preserve buff uptime, current HP, combat totals and attributed loot after reload");
        check(!File.ReadAllText(path).Contains("discard-this-diagnostic") && restored.Network.All(e => e.Opcode == "saved-analysis"),
            "Saved fight files omit diagnostic fields and original protocol opcodes");
        check(new FileInfo(path).Length < JsonSerializer.SerializeToUtf8Bytes(fight).Length / 10,
            "A thousand repetitive buff packets shrink by over 90 percent while their analysis remains identical");
        long firstSize = new FileInfo(path).Length; store.Save(restored);
        check(new FileInfo(path).Length == firstSize && EffectAnalysis.Build(store.Load(path), id => "Skill " + id).SequenceEqual(effects),
            "Resaving a summarized fight preserves its analysis and does not regrow removed network logs");
        var legacy = fight.Copy(); legacy.Id = Guid.NewGuid().ToString("N");
        string legacyPath = Path.Combine(store.Root, legacy.Id + ".json"); File.WriteAllText(legacyPath, JsonSerializer.Serialize(legacy));
        check(EffectAnalysis.Build(store.Load(legacyPath), id => "Skill " + id).SequenceEqual(effects),
            "Existing full-log fight files remain readable alongside new summarized files");
        var lootOnly = new Fight { Label = "Loot summary", LocalPlayer = 1 };
        lootOnly.Actors[1] = new(1, "Collector", true, true, 0, "Cleric", 1) { CharacterId = 30 };
        for (int i = 0; i < 1000; i++) {
            long time = 1000 + i;
            lootOnly.Network.Add(new("loot-pickup", time, "synthetic", JsonSerializer.SerializeToElement(
                new LootPickup(time, (uint)i + 1, 500000001, 2, 30, 1, i % 2 == 0 ? 1010 : 1110))));
        }
        var lootRows = LootLedger.FromFight(lootOnly).Rows(); store.Save(lootOnly);
        var restoredLoot = store.Load(Path.Combine(store.Root, lootOnly.Id + ".json"));
        check(restoredLoot.Network.Count == 0 && restoredLoot.SavedLoot?.Rows.Length == 2 && restoredLoot.HasRecordedData &&
            SavedHistory.Includes(restoredLoot) && SavedHistory.LootPickups(restoredLoot) == 1000 &&
            LootLedger.FromFight(restoredLoot).Rows().SequenceEqual(lootRows),
            "A thousand pickup events persist as two map/item totals while loot-only history, counts and timestamps remain intact");
        check(FightArchiveSummary.Create(restoredLoot).ArchiveSummary!.LootPickups == 1000 &&
            LootLedger.FromFight(restoredLoot).OverlaySnapshot().Rows.SequenceEqual(LootLedger.FromFight(lootOnly).OverlaySnapshot().Rows),
            "Compact history and recent-loot presentation consume saved totals without reconstructing pickup events");
        var badLoot = restoredLoot.Copy(); badLoot.Id = Guid.NewGuid().ToString("N");
        badLoot.SavedLoot = restoredLoot.SavedLoot! with { Rows = [lootRows[0], lootRows[0]] }; store.Save(badLoot);
        bool rejected = false;
        try { store.Load(Path.Combine(store.Root, badLoot.Id + ".json")); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "Imported loot summaries reject duplicate session/map/item rows rather than double-counting them");
        var repeatedMaps = new Fight();
        for (int i = 0; i < 1000; i++) repeatedMaps.Network.Add(new("field-boss-list", 1000 + i, "synthetic",
            JsonSerializer.SerializeToElement(new FieldBossList(1010, [new(1, true, 1, 2, 3, 1700000000000)]))));
        var mapStorage = repeatedMaps.ForStorage();
        check(mapStorage.Network.Count == 1 && mapStorage.Network[0].Timestamp == 1999 && repeatedMaps.Network.Count == 1000,
            "Saving legacy repeated field-boss snapshots retains only the latest map state without mutating the source");
        var guardFight = FeatureTests.Sample(); guardFight.Network.Clear();
        for (int i = 0; i < 1000; i++) guardFight.Network.Add(new("guard", 1000 + i, "05 E0", JsonSerializer.SerializeToElement(
            new GuardReading(100, i == 999 ? (byte)3 : (byte)1, 1000, 999 - i, 7, 5000, 9000, true, 10000, true))));
        guardFight.Network.Add(new("guard", 1000, "test", JsonSerializer.SerializeToElement(new GuardReading(200, 1, 1000, 500, 0, 0, 0, false, 0, true))));
        guardFight.Network.Add(new("entity-removed", 2000, "test", JsonSerializer.SerializeToElement(new { EntityId = 200 })));
        var expectedGuard = CapturedGuard.Build(guardFight);
        store.Save(guardFight); string guardPath = Path.Combine(store.Root, guardFight.Id + ".json");
        var restoredGuard = store.Load(guardPath);
        check(restoredGuard.Network is [{ Kind: "guard", Opcode: "saved-analysis" }] &&
            restoredGuard.Network[0].Fields.EnumerateObject().Count() == 4 &&
            CapturedGuard.Build(restoredGuard).SequenceEqual(expectedGuard) && guardFight.Network.Count == 1002,
            "Saved fights reduce a thousand guard updates to one four-field state and do not revive a removed actor");
        long guardSize = new FileInfo(guardPath).Length; store.Save(restoredGuard);
        check(new FileInfo(guardPath).Length == guardSize && CapturedGuard.Build(store.Load(guardPath)).SequenceEqual(expectedGuard),
            "Resaving compact stagger state preserves values without regrowing guard history");
    }
}
