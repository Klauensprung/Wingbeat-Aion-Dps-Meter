using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class AccountCharacterTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        string path = Path.Combine(temp, "account-characters.json");
        var cache = new AccountCharacterCache(path);
        var original = new LoginCharacter(650000000000000001, "Before", 25, 50, "Guild", 12345) { GearScore = 321 };
        cache.Apply(new(0, 1, [original]), 10); var snapshot = cache.Snapshot();
        cache.Apply(new(0, 1, [original with { Name = "After", Level = 51 }]), 20);
        cache.Apply(new(0, 1, [original]), 15);
        check(cache.Snapshot().Single().Character is { Name: "After", Level: 51 } && snapshot.Single().Character.Name == "Before",
            "Saved roster merges renames by wide identity without stale regression or snapshot mutation");
        cache.Apply(new(0, 2, [original]), 20); cache.Apply(new(0, 1, []), 21);
        check(cache.Snapshot().Length == 2, "Same IDs on different servers stay separate and missing roster entries remain last-known");
        cache.Apply(new(1, 1, [original]), 30); cache.Apply(new(0, 0, [original]), 30);
        check(cache.Snapshot().Single(r => r.Server == 1).Character.Name == "After", "Failed logins and unknown servers cannot change saved roster ownership");
        cache.Save();
        check(new AccountCharacterCache(path).Snapshot().SequenceEqual(cache.Snapshot()), "Compact roster persists with 64-bit identity and capture time");
        var gearCache = new AccountCharacterCache(Path.Combine(temp, "account-gear.json"));
        gearCache.Apply(new(0, 1, [original with { GearScore = null }]), 1); gearCache.Save();
        check(new AccountCharacterCache(Path.Combine(temp, "account-gear.json")).Snapshot().Single().Character.GearScore == null,
            "Legacy account entries retain unknown gear score through persistence");
        gearCache.Apply(new(0, 1, [original]), 2); gearCache.Apply(new(0, 1, [original with { GearScore = 0 }]), 3);
        gearCache.Apply(new(0, 1, [original with { GearScore = 999 }]), 2);
        gearCache.Apply(new(0, 1, [original with { GearScore = -1 }]), 4); gearCache.Save();
        check(new AccountCharacterCache(Path.Combine(temp, "account-gear.json")).Snapshot().Single().Character.GearScore == 0,
            "Explicit zero account gear score survives stale and invalid updates and cache reload");
        var engine = new CombatEngine(accountCharacters: cache);
        engine.Apply(new("network", Network: new("account-login", 100, "1536", JsonSerializer.SerializeToElement(new LoginReading(0, 3, [original])))));
        engine.NewFight("Reset");
        check(cache.Snapshot().Any(r => r.Server == 3), "Production login feeds shared roster cache independently of fight reset");
        var bounded = new AccountCharacterCache();
        for (int i = 1; i <= 600; i++) bounded.Apply(new(0, 1, [original with { Id = i }]), i);
        check(bounded.Snapshot().Length == 512 && bounded.Snapshot().All(r => r.Character.Id > 88), "Roster retention is bounded to the 512 newest identities");
        var appearance = new LoginAppearance("light", "female", "DAID", 456, [new(123, 456, [new(1, 255, 128, 0)])]);
        var dressed = original with { Appearance = appearance };
        var appearancePath = Path.Combine(temp, "appearance-roster.json"); var appearanceCache = new AccountCharacterCache(appearancePath);
        appearanceCache.Apply(new(0, 1, [dressed]), 40);
        appearance.Equipment[0].Dyes[0] = new(1, 0, 0, 0);
        var copied = appearanceCache.Snapshot(); copied[0].Character.Appearance!.Equipment[0].Dyes[0] = new(1, 1, 1, 1);
        check(appearanceCache.Snapshot()[0].Character.Appearance!.Equipment[0].Dyes[0].Red == 255, "Saved appearance cache isolates both incoming and outgoing nested dye arrays");
        appearanceCache.Save(); var restoredAppearance = new AccountCharacterCache(appearancePath).Snapshot().Single();
        check(restoredAppearance.Character.Appearance is { Data: "DAID", Wing: 456 } && restoredAppearance.Character.Appearance.Equipment[0].Dyes[0].Red == 255,
            "Login appearance data and equipment dyes survive persistent character-cache reload");
        check(File.ReadAllText(appearancePath).Contains("\"Id\":\"650000000000000001\""), "Saved character identities serialize as decimal strings without precision loss");
        appearanceCache.Apply(new(0, 1, [dressed with { Appearance = appearance with { Data = "bad!" } }]), 50);
        check(appearanceCache.Snapshot().Single().Timestamp == 40, "Malformed appearance data cannot overwrite a valid saved character");
        var payload = new byte[65536]; payload[0] = 12;
        var largeAppearance = appearance with { Data = Convert.ToBase64String(payload) };
        for (int i = 1; i <= 40; i++) appearanceCache.Apply(new(0, 2, [original with { Id = i, Appearance = largeAppearance }]), 100 + i);
        appearanceCache.Save(); var limited = new AccountCharacterCache(appearancePath).Snapshot();
        check(new FileInfo(appearancePath).Length <= 2 * 1024 * 1024 && limited.Length < 40 && limited.Any(r => r.Character.Id == 40) && limited.All(r => r.Timestamp > 100),
            "Appearance roster evicts oldest snapshots to keep serialized retention and persisted JSON under the byte budget");
        string bad = Path.Combine(temp, "invalid-account-characters.json"); File.WriteAllText(bad, "[{\"Server\":1}]");
        check(new AccountCharacterCache(bad).Snapshot().Length == 0 && File.ReadAllText(bad) == "[{\"Server\":1}]", "Malformed saved roster is rejected without deleting the source file");
        string badDuty = Path.Combine(temp, "invalid-duty.json"); File.WriteAllText(badDuty, "[{\"Server\":-1}]");
        check(new DutyMissionLedger(badDuty).Snapshot().Length == 0 && File.ReadAllText(badDuty) == "[{\"Server\":-1}]",
            "Invalid duty cache fails safely without an unhandled validation exception");
        string badProfiles = Path.Combine(temp, "invalid-profiles.json"); File.WriteAllText(badProfiles, "{\"Version\":99}");
        _ = new PlayerProfileCache(badProfiles);
        check(File.ReadAllText(badProfiles) == "{\"Version\":99}", "Invalid profile cache cannot crash startup and retains the original file");
    }
}
