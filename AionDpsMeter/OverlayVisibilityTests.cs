using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class OverlayVisibilityTests
{
    internal static void Run(Action<bool, string> check)
    {
        check(GameForeground.IsAllowedProcess(12, 34, "AION2") && GameForeground.IsAllowedProcess(12, 34, "aion2-win64-shipping") &&
            GameForeground.IsAllowedProcess(34, 34, null),
            "Foreground visibility accepts both known game executables and Wingbeat's own windows");
        check(!GameForeground.IsAllowedProcess(12, 34, "AION2 Launcher") && !GameForeground.IsAllowedProcess(12, 34, "browser") &&
            !GameForeground.IsAllowedProcess(12, 34, null) && !GameForeground.IsAllowedProcess(0, 0, "AION2"),
            "Foreground visibility rejects unrelated, unknown and invalid processes without fuzzy title matching");
        check(CooldownVisibility.ForegroundAllows(false, false) && CooldownVisibility.ForegroundAllows(true, true) &&
            !CooldownVisibility.ForegroundAllows(true, false),
            "Game-only mode hides disallowed foreground apps while the default mode preserves desktop previews");
        check(!JsonSerializer.Deserialize<Preferences>("{}")!.OverlaysGameOnly &&
            JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(new Preferences { OverlaysGameOnly = true }))!.OverlaysGameOnly,
            "Game-aware visibility remains opt-in and persists locally");
        var petSettings = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(new Preferences {
            PetTrackerGoal = 3, PetTrackerLocked = true, PetTrackerLeft = 30, PetTrackerTop = 40,
            PetMapLocked = true, PetMapLeft = 70, PetMapTop = 80
        }))!;
        check(petSettings is { PetTrackerGoal: 3, PetTrackerLocked: true, PetTrackerLeft: 30, PetTrackerTop: 40,
            PetMapLocked: true, PetMapLeft: 70, PetMapTop: 80 } && JsonSerializer.Deserialize<Preferences>("{}")!.PetTrackerGoal == 1,
            "Pet overlays preserve separate positions and locks plus a shared farming phase with backwards-compatible defaults");
        var engine = new CombatEngine();
        engine.Apply(new("local-player", Actor: new(1, "Local example", true, true, 0, "Cleric", 1)));
        engine.Apply(new("actor", Actor: new(2, "Party example", true, true, 0, "Ranger", 1)));
        engine.Apply(new("actor", Actor: new(3, "Nearby example", true, false, 0, "Ranger", 1)));
        void Hit(int actor, long time, bool heal = false) => engine.Apply(new(heal ? "heal" : "damage",
            Damage: new DamageEvent { ActorId = actor, TargetId = 100, Timestamp = time, Damage = 100 }));
        Hit(3, 500);
        check(!engine.HasRecentPartyCombat(500), "Nearby unrelated combat does not reveal a combat-only overlay");
        Hit(2, 1000);
        check(engine.HasRecentPartyCombat(10999) && !engine.HasRecentPartyCombat(11000) && !engine.HasRecentPartyCombat(999),
            "Party damage enables the combat overlay for ten seconds and never uses future events");
        Hit(1, 12000, true);
        check(!engine.HasRecentPartyCombat(12000), "Out-of-combat healing alone does not reveal the combat overlay");
        engine.Apply(new("defense", Defense: new DefenseEvent { ActorId = 100, TargetId = 1, Timestamp = 13000, Outcome = DefenseOutcome.Block }));
        check(engine.HasRecentPartyCombat(13000), "Incoming defensive events keep the party combat overlay active");
        engine.NewFight("Reset");
        check(!engine.HasRecentPartyCombat(13000), "New fight clears stale combat visibility");
        check(CooldownVisibility.ShouldShow(true, false, false, false, false) &&
            !CooldownVisibility.ShouldShow(true, true, true, false, false) && CooldownVisibility.ShouldShow(true, true, true, false, true) &&
            !CooldownVisibility.ShouldShow(false, true, true, true, true) && !CooldownVisibility.ShouldShow(true, true, false, true, false),
            "Combat-only visibility preserves ordinary previews, includes training, and respects manual hiding and stopped capture");
        check(!JsonSerializer.Deserialize<Preferences>("{}")!.CooldownCombatOnly &&
            JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(new Preferences { CooldownCombatOnly = true }))!.CooldownCombatOnly,
            "Combat-only visibility is opt-in and persists across restarts");
    }
}
