using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class ControlEffectTests
{
    internal static void Run(Action<bool, string> check)
    {
        EffectDefinition? Lookup(int id) => id switch { 1 => new(1, "Stun", "Debuff"), 2 => new(2, "Fear", "Debuff"),
            3 => new(3, "Stun Immunity", "Buff"), 4 => new(4, "Sleep", "Unknown"), _ => null };
        var rows = ControlEffectPresentation.Build([new(1, 1, 0, 9, 1.5), new(2, 1, 0, 9, 3), new(3, 2, 0, 9, 2),
            new(4, 3, 0, 9, 6), new(5, 4, 0, 9, 6), new(6, 99, 0, 9, 6)], Lookup);
        check(rows is [{ Name: "Fear", Seconds: 2 }, { Name: "Stun", Seconds: 3 }],
            "Control display groups matching debuffs by longest remaining duration and excludes immunity, unknown types and unknown effects");
        check(ControlEffectPresentation.Build([new(1, 1, 0, 9, 2), new(2, 1, 0, 9, null)], Lookup).Single().Seconds == null,
            "An unknown-duration overlapping control effect prevents a fabricated end time");
        check(ControlEffectPresentation.Build([new(1, 1, 0, 9, 0), new(2, 1, 0, 9, -1), new(3, 1, 0, 9, double.NaN)], Lookup).Length == 0,
            "Expired and invalid control-effect durations do not produce visible alerts");
        check(!ControlEffectPresentation.ShouldShow(true, true, false, 1, false) && !ControlEffectPresentation.ShouldShow(true, false, true, 1, false) &&
            !ControlEffectPresentation.ShouldShow(false, true, true, 1, true) && ControlEffectPresentation.ShouldShow(true, true, false, 0, true) &&
            ControlEffectPresentation.ShouldShow(true, true, true, 1, false),
            "Control alerts respect capture, foreground and enablement while allowing an explicit placement preview");
        var fight = FeatureTests.Sample(); var tracker = new LiveEffectTracker();
        fight.Network.Add(new("abnormal-apply", 10000, "synthetic", JsonSerializer.SerializeToElement(new { TargetId = 1, Slot = 1, AbnormalId = 100, DurationMs = 3000 })));
        tracker.Observe(fight, 11000, 1000);
        check(ControlEffectPresentation.Build(tracker.Active(1000), EffectCatalogue.Default.Find) is [{ Name: "Stun", Seconds: 2 }] &&
            ControlEffectPresentation.Build(tracker.Active(3000), EffectCatalogue.Default.Find).Length == 0,
            "Captured local stun countdown includes packet age and disappears at expiration");
        fight.Network.Add(new("abnormal-remove", 11500, "synthetic", JsonSerializer.SerializeToElement(new { TargetId = 1, Slot = 1 })));
        tracker.Observe(fight, 11500, 1500);
        check(ControlEffectPresentation.Build(tracker.Active(1500), EffectCatalogue.Default.Find).Length == 0,
            "Captured removal clears a control notice before its original deadline");
        check(!new Preferences().ControlAlertsEnabled && JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(new Preferences {
            ControlAlertsEnabled = true, ControlLocked = true, ControlLeft = 125, ControlTop = 250 })) is { ControlAlertsEnabled: true, ControlLocked: true, ControlLeft: 125, ControlTop: 250 },
            "Control alerts are opt-in and retain placement and locking preferences locally");
    }
}
