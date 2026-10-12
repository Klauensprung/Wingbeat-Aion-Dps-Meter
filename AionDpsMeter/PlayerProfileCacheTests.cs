using System.IO;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class PlayerProfileCacheTests
{
    public static void Run(Action<bool, string> check, string temp)
    {
        string path = Path.Combine(temp, "profile-cache.json");
        var cache = new PlayerProfileCache(path); var engine = new CombatEngine(profiles: cache);
        var member = new Actor(1, "Example", true, true, 42000, "Unknown", 2301) { CpSource = "Party packet" };
        engine.Apply(new("actor", Actor: member with { CpSource = "Local character packet" }));
        engine.RecordDamage(new() { ActorId = 1, TargetId = 9, SkillCode = 17010001, Timestamp = 1000, Damage = 50 }, false);
        var learned = engine.Snapshot(); cache.Save();
        // Captured Job remains untouched; display evidence is persisted separately.
        check(learned.Actors[1].Job == "Unknown" && ClassEvidence.Resolve(learned.Actors[1], 1, ClassEvidence.Infer(learned)) == "Cleric",
            "Cache learns a player's class from combat without relabelling raw identity metadata");
        var restart = new CombatEngine(profiles: new PlayerProfileCache(path));
        restart.Apply(new("actor", Actor: member with { Id = 77, Cp = 0, Job = "Unknown" }));
        var restored = restart.Snapshot();
        check(restored.Actors[77] is { Cp: 42000, CpSource: PlayerProfileCache.CachedSource, CachedJob: "Cleric" } &&
            ClassEvidence.Resolve(restored.Actors[77], 77, ClassEvidence.Infer(restored)) == "Cleric",
            "CP and inferred class survive restart and changed entity IDs using name plus server");
        restart.Apply(new("local-player", Actor: member with { Id = 77, Cp = 0 }));
        check(restart.Snapshot().Actors[77].Cp == 42000 && restart.Fight.Actors[77].CachedJob == "Cleric",
            "The local CP packet reset retains last-known metadata when new values are missing");
        restart.Apply(new("actor", Actor: member with { Id = 77, Cp = 46000, CpSource = "Local character packet", Job = "Chanter" }));
        check(restart.Snapshot().Actors[77] is { Cp: 46000, Job: "Chanter", CachedJob: "Chanter" },
            "Fresh positive CP and explicit class replace cached values");
        restart.Apply(new("actor", Actor: member with { Id = 77, Cp = 0, Server = 2302 }));
        check(restart.Snapshot().Actors[77] is { Cp: 0, CachedJob: "Unknown" },
            "Same-name players on different servers never inherit CP or class, including reused entity IDs");
        var unnamed = new CombatEngine(profiles: new PlayerProfileCache(path));
        unnamed.Apply(new("actor", Actor: member with { Cp = 0, Server = 0 }));
        check(unnamed.Snapshot().Actors[1] is { Cp: 0, CachedJob: "Unknown" }, "Missing server identity does not guess a cached player match");
        var current = new CombatEngine(profiles: new PlayerProfileCache(path));
        current.Apply(new("actor", Actor: member with { Cp = 0 }));
        current.RecordDamage(new() { ActorId = 1, TargetId = 9, SkillCode = 14010001, Timestamp = 1000, Damage = 50 }, false);
        check(ClassEvidence.Resolve(current.Snapshot().Actors[1], 1, ClassEvidence.Infer(current.Fight)) == "Ranger",
            "New class evidence overrides cached skill inference");
        check(MeterPresentation.CombatPowerColor(9000, 10000) == "#65D6AF" &&
            MeterPresentation.CombatPowerColor(10000, 10000) == "#E7CF7D" &&
            MeterPresentation.CombatPowerColor(11000, 10000) == "#FF9292" &&
            MeterPresentation.CombatPowerColor(1, 10000) == "#65D6AF" &&
            MeterPresentation.CombatPowerColor(int.MaxValue, 10000) == "#FF9292",
            "CP gradient reaches its endpoints at +/-10 percent and clamps beyond them");
        check(MeterPresentation.CombatPowerColor(9500, 10000) == "#A6D396" &&
            MeterPresentation.CombatPowerColor(10500, 10000) == "#F3B188",
            "CP gradient blends intermediate values on both sides of equal power");
        check(MeterPresentation.CombatPowerColor(42000, 0) == "#CED5E1" &&
            MeterPresentation.CombatPowerColor(0, 42000) == "#CED5E1" &&
            MeterPresentation.CombatPowerColor(42000, 42000, true) == "#CED5E1",
            "Unknown CP and the local row keep neutral comparison colours");
        var cachedFight = restored.Copy(); cachedFight.LocalPlayer = 77;
        cachedFight.Actors[88] = new Actor(88, "Example opponent", true, false, 46200, "Unknown", 2301);
        cachedFight.Damage.Add(new() { ActorId = 88, TargetId = 77, SkillCode = 14010001, Timestamp = 2000, Damage = 50 });
        var compared = MeterPresentation.Build(cachedFight, Analysis.Totals(cachedFight), allActors: true).Rows.Single(r => r.Id == 88);
        check(compared.LocalCombatPower == 42000 && MeterPresentation.CombatPowerColor(compared.CombatPower, compared.LocalCombatPower) == "#FF9292",
            "Restart-restored local CP supplies the comparison baseline for other players");
        var identityPath = Path.Combine(temp, "local-identity-cache.json");
        var identities = new PlayerProfileCache(identityPath);
        var source = new CombatEngine(profiles: identities);
        source.Apply(new("local-player", Actor: member)); identities.Save();
        var nextSession = new CombatEngine(profiles: new PlayerProfileCache(identityPath));
        check(nextSession.Fight.LocalPlayer == 0, "Restart never reuses a cached entity ID");
        nextSession.Apply(new("actor", Actor: member with { Id = 81, Server = 2302 }));
        check(nextSession.Fight.LocalPlayer == 0, "Same name on another server cannot become the local player");
        nextSession.Apply(new("actor", Actor: member with { Id = 82 }));
        check(nextSession.Fight.LocalPlayer == 82, "A fresh unique name/server match restores local identity across restarts");
        nextSession.Apply(new("actor", Actor: member with { Id = 83 }));
        check(nextSession.Fight.LocalPlayer == 0, "Ambiguous identities revoke a cached local-player match");
        nextSession.Apply(new("local-player", Actor: member with { Id = 84, Name = "Another character" }));
        check(nextSession.Fight.LocalPlayer == 84, "A current local character packet overrides cached identity");
        File.WriteAllText(identityPath, "[]");
        var legacy = new CombatEngine(profiles: new PlayerProfileCache(identityPath));
        legacy.Apply(new("actor", Actor: member));
        check(legacy.Fight.LocalPlayer == 0, "Legacy profile caches migrate without guessing local identity");
        File.WriteAllText(path, "{broken");
        var broken = new CombatEngine(profiles: new PlayerProfileCache(path)); broken.Apply(new("actor", Actor: member with { Cp = 0 }));
        check(broken.Snapshot().Actors[1].Cp == 0, "Corrupt caches fail safely without fabricating player metadata");
    }
}
