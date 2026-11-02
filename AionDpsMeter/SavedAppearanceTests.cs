using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class SavedAppearanceTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        const long id = 650000000000000001;
        var actor = new Actor(7, "Synthetic", true, false, 1000, "Cleric", 2301) { PersistentId = id };
        var login = new LoginCharacter(id, "Synthetic", 32, 50, "Guild", 1000) {
            Appearance = new("light", "male", "DAID", 10, [new(1, 2, [])])
        };
        string path = Path.Combine(temp, "saved-appearance-library.json");
        var cache = new AccountCharacterCache(path); cache.Apply(new(0, 2301, [login]), 100);
        var engine = new CombatEngine(accountCharacters: cache); engine.Apply(new("local-player", Actor: actor));
        void Apply(string kind, long at, object value) => engine.Apply(new("network", Network: new(kind, at, "test", JsonSerializer.SerializeToElement(value))));
        Apply("appearance-current", 200, new CurrentAppearance(7, "dark", "female", "AQID", 20, 30));
        Apply("appearance-presets", 210, new AppearancePresetReading([new(0, "Saved preset", "BAUG")]));
        Apply("visible-equipment", 220, new EquipmentReading(7, true, [new VisibleItem(1, 100, 200, 0, true) { Dyes = [new(1, 255, 128, 0)] }]));
        Apply("wing-change", 230, new { EntityId = 7, Skin = true, WingId = 40 });
        var view = SavedAppearance.View(cache.Snapshot().Single())!;
        check(view is { Current: { Data: "AQID", WingSkinId: 40 }, Presets: [{ Name: "Saved preset" }], Equipment: [{ Item: 100 }], EquipmentComplete: true },
            "Production live appearance, named presets, equipment and wing updates feed the saved character library");
        cache.Save(); var restored = new AccountCharacterCache(path);
        var reopened = SavedAppearance.View(restored.Snapshot().Single())!;
        check(reopened.Current == view.Current && reopened.Presets.SequenceEqual(view.Presets) && reopened.Equipment![0].Dyes[0].Red == 255,
            "Saved library restores named presets, unrestricted local appearance bytes and equipment colours after restart");
        reopened.Presets[0] = new(0, "Changed", ""); reopened.Equipment![0].Dyes[0] = new(1, 0, 0, 0);
        check(SavedAppearance.View(restored.Snapshot().Single()) is { Presets: [{ Name: "Saved preset" }], Equipment: [{ Dyes: [{ Red: 255 }] }] },
            "Library views cannot mutate persisted preset or dye arrays");
        cache.Apply(new(0, 2301, [login with { Name = "Renamed" }]), 300);
        view = SavedAppearance.View(cache.Snapshot().Single())!;
        check(view is { Name: "Renamed", Current: { Data: "DAID", WingId: 10, WingSkinId: 0 }, Presets: [{ Name: "Saved preset" }], Equipment: [{ Item: 1 }] },
            "New login refreshes current appearance and equipment while preserving named presets through a rename");
        Apply("appearance-presets", 310, new AppearancePresetReading([]));
        view = SavedAppearance.View(cache.Snapshot().Single())!;
        check(view is { Current.Data: "DAID", PresetsCaptured: true, Presets.Length: 0, CurrentAt: 300 },
            "Explicitly empty preset list clears stored slots without making older current-appearance fields newer");
        Apply("appearance-presets", 250, new AppearancePresetReading([new(1, "Stale", "AA==")]));
        check(SavedAppearance.View(cache.Snapshot().Single())!.Presets.Length == 0, "Stale preset packets cannot resurrect cleared saved slots");
        var freshCache = new AccountCharacterCache(); var freshEngine = new CombatEngine(accountCharacters: freshCache);
        freshEngine.Apply(new("local-player", Actor: actor));
        freshEngine.Apply(new("network", Network: new("appearance-presets", 100, "test", JsonSerializer.SerializeToElement(new AppearancePresetReading([new(0, "No login", "AA==")])))));
        check(freshCache.Snapshot().Single() is { CapturedJob: "Cleric", Character.Id: id } && SavedAppearance.View(freshCache.Snapshot().Single())!.Presets[0].Name == "No login",
            "Capture started after login can save presets under explicit full character identity");
        foreach (int? score in new int?[] { null, 0, 432 }) {
            string gearPath = Path.Combine(temp, "appearance-created-gear-" + (score?.ToString() ?? "unknown") + ".json");
            var gearCache = new AccountCharacterCache(gearPath);
            var gearEngine = new CombatEngine(accountCharacters: gearCache);
            gearEngine.Apply(new("local-player", Actor: actor with { Metadata = score.HasValue ? new(GearScore: score, GearScoreAt: 90) : null }));
            gearEngine.Apply(new("network", Network: new("appearance-presets", 100, "test", JsonSerializer.SerializeToElement(new AppearancePresetReading([])))));
            gearCache.Save();
            check(new AccountCharacterCache(gearPath).Snapshot().Single().Character.GearScore == score,
                "Character first saved from live appearance preserves " + (score?.ToString() ?? "unknown") + " gear score after reload");
        }
        var ambiguous = actor with { PersistentId = 0 };
        var unknownCache = new AccountCharacterCache(); var unknownEngine = new CombatEngine(accountCharacters: unknownCache);
        unknownEngine.Apply(new("local-player", Actor: ambiguous));
        unknownEngine.Apply(new("network", Network: new("appearance-presets", 100, "test", JsonSerializer.SerializeToElement(new AppearancePresetReading([])))));
        check(unknownCache.Snapshot().Length == 0, "Missing full identity cannot persist appearance under a namesake");
        var invalid = view with { CharacterId = (id + 1).ToString(), PresetsAt = 500 };
        check(!SavedAppearance.Valid(invalid, 2301, id) && !SavedAppearance.Valid(view with { Presets = [new(0, null, "invalid!")] }, 2301, id),
            "Saved appearance validation rejects mismatched identity, future component clocks and invalid binary data");
        var tracker = new AppearancePresetTracker(); tracker.Follow(actor);
        tracker.Apply(new("appearance-presets", 100, "test", JsonSerializer.SerializeToElement(new AppearancePresetReading([new(0, "Old server", "AA==")]))));
        tracker.Follow(actor with { Server = 2302 });
        check(tracker.Snapshot() == null, "Same numeric character ID on another server cannot inherit the previous server's presets");
        var existing = freshCache.Snapshot().Single();
        var oversized = existing.LiveAppearance! with { Timestamp = 200, PresetsAt = 200, Presets = Enumerable.Range(0, 256).Select(n => new AppearancePreset((byte)n, new string('\u2665', 4096), "AA==")).ToArray() };
        freshCache.ApplyAppearance(actor, oversized);
        check(SavedAppearance.View(freshCache.Snapshot().Single())!.Presets.Single().Name == "No login", "One oversized appearance entry cannot evict the existing character library");
        engine.NewFight("Reset"); check(cache.Snapshot().Length == 1 && engine.Fight.Network.Count == 0,
            "Saved appearance library survives fight reset without raw combat retention");
    }
}
