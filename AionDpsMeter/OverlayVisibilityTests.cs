using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class OverlayVisibilityTests
{
    internal static void Run(Action<bool, string> check)
    {
        check(CooldownVisibility.MeterOpacity(false,false,true,false,false)==1,
            "Party health stays fully readable without recent damage or healing");
        check(CooldownVisibility.MeterOpacity(false,false,false,true,false)==1,
            "Healing meters stay fully readable during healing-free boss phases");
        check(CooldownVisibility.MeterOpacity(false,false,false,false,false)==.4 &&
            CooldownVisibility.MeterOpacity(true,false,false,false,false)==1 &&
            CooldownVisibility.MeterOpacity(false,true,false,false,false)==1 &&
            CooldownVisibility.MeterOpacity(false,false,false,false,true)==1,
            "Only inactive damage meters fade; activity, hover and training restore full opacity");
        var menus=new GameMenuVisibility();
        var encounter=new CombatEngine();
        encounter.Apply(new("local-player",Actor:new(1,"Local",true,true,0,"Cleric",1)));
        encounter.Apply(new("actor",Actor:new Actor(10,"Boss",false,false,0,"Unknown",0){CapturedBoss=true}));
        encounter.RecordDamage(new DamageEvent{ActorId=1,TargetId=10,Timestamp=1000,Damage=100},false);
        check(encounter.OverlayActive(17000),"Engaged boss keeps DPS bright after the fifteen-second damage gap without any warning banner");
        encounter.AdvanceTime(61000);
        check(encounter.OverlayActive(62000),"Automatic idle fight splits do not fade an ongoing boss encounter");
        encounter.NewFight("Manual reset");
        check(!encounter.OverlayActive(63000),"Manual fight reset clears retained boss brightness");
        var activity=new OverlayBossActivity();
        NetworkEvent BossEvent(string kind,long time,object fields)=>new(kind,time,"test",JsonSerializer.SerializeToElement(fields));
        foreach(string end in new[]{"entity-death","entity-removed","combat-state","hp","local-zone","account-login"}){
            activity.Engage(10,1000);
            activity.Apply(BossEvent(end,2000,new{EntityId=10,State=0,CurrentHp=0}));
            check(!activity.Active(3000),"Boss brightness ends on "+end);
        }
        activity.Apply(BossEvent("hp",1000,new{EntityId=10,CurrentHp=500}));
        check(!activity.Active(2000),"An unengaged nearby boss cannot keep DPS bright");
        activity.Engage(10,1000);
        activity.Apply(BossEvent("entity-death",500,new{EntityId=10}));
        check(activity.Active(2000),"Stale boss death does not clear a newer engagement");
        activity.Apply(BossEvent("hp",250000,new{EntityId=10,CurrentHp=500}));
        check(activity.Active(400000)&&!activity.Active(550001),"Fresh boss reports extend visibility while lost capture eventually expires");
        menus.Observe(0,true,true,false,false,false);
        menus.Observe(100,true,true,true,true,false);
        menus.Observe(200,false,false,false,false,false);
        menus.Observe(300,true,true,true,false,false);
        check(!menus.Observe(2299,true,true,true,false,false)&&menus.Observe(2300,true,true,true,false,false),
            "An Alt press before switching focus cannot keep menu hiding disabled after the current key state is released");
        menus.Reset();
        check(!menus.Observe(0,true,true,true,false,false)&&!menus.Observe(10000,true,true,true,false,false)&&!menus.Armed,
            "Always-visible classic-style cursors do not arm automatic menu hiding");
        menus.Observe(11000,true,true,false,false,false);
        menus.Observe(12000,true,true,true,false,false);
        check(!menus.Observe(13999,true,true,true,false,false)&&menus.Observe(14000,true,true,true,false,false),
            "Menu cursor gives the user two seconds to reach an overlay");
        check(!menus.Observe(14100,true,true,true,false,false,overOverlay:true)&&!menus.Observe(90000,true,true,true,false,false,overOverlay:true),
            "Hover keeps overlays visible indefinitely even when Alt cannot be read");
        menus.Observe(91000,true,true,true,false,false);
        check(!menus.Observe(92999,true,true,true,false,false)&&menus.Observe(93000,true,true,true,false,false),
            "Leaving an overlay gives a fresh two seconds to reach another overlay or submenu");
        check(!menus.Observe(93100,true,true,true,true,false)&&!menus.Observe(100000,true,true,true,true,false),
            "Detectable Alt still overrides menu hiding indefinitely");
        menus.Observe(101000,true,true,true,false,false);
        check(!menus.Observe(102999,true,true,true,false,false)&&menus.Observe(103000,true,true,true,false,false),
            "Releasing Alt restarts the two-second grace period");
        check(menus.Observe(104000,true,true,false,false,false)&&!menus.Observe(104400,true,true,false,false,false),
            "Brief cursor flicker is tolerated and closing the menu restores overlays");
        menus.Observe(105000,true,true,true,false,false);menus.Observe(107000,true,true,true,false,false);
        menus.Observe(107100,true,true,false,false,true);
        check(menus.Observe(107600,true,true,false,false,false)&&!menus.Observe(108000,true,true,false,false,false),
            "Mouse-click cursor swaps have a bounded linger");
        check(!menus.Observe(109000,false,true,true,false,false)&&!menus.Observe(110000,true,false,true,false,false),
            "Losing game focus or cursor-read failure releases automatic hiding");
        check(!menus.Observe(111000,true,true,true,false,false,true)&&!menus.Armed,
            "Classic control packets disarm cursor-based hiding");
        var snooze = new OverlaySnooze();
        snooze.HideForMinute(1000);
        check(!snooze.Allows(1000)&&!snooze.Allows(60999)&&snooze.Allows(61000),"Overlay snooze lasts exactly sixty seconds on the monotonic clock");
        snooze.HideForMinute(62000);
        check(!snooze.Allows(121999)&&snooze.Allows(122000),"Repeated overlay snooze starts a fresh minute");
        var hotkey = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(new Preferences { OverlayHideKey=0x78,OverlayHideModifiers=3 }))!;
        check(hotkey.OverlayHideKey==0x78&&hotkey.OverlayHideModifiers==3&&OverlayHotkey.Label(0x79,6)=="Ctrl + Shift + F10"&&OverlayHotkey.Label(0,6)=="Disabled",
            "Configurable global overlay hotkey persists with a readable default and disabled state");
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
