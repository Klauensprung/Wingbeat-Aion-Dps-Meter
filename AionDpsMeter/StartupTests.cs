using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class StartupTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var settings = JsonSerializer.Deserialize<Preferences>("{}")!;
        var fresh = StartupPlan.Create(settings, ["fixture-adapter"], false);
        check(settings.AutoStartConfigured && !fresh.StartCapture && !fresh.OpenMeter && !fresh.OpenCooldowns && fresh.SetupMessage != null,
            "First launch defaults to automatic setup but never captures an arbitrary adapter");
        settings.Adapter = "fixture-adapter"; settings.OverlayLeft = -120; settings.OverlayTop = 50; settings.CooldownSkills = [17060000];
        var configured = StartupPlan.Create(settings, ["unrelated-adapter", "fixture-adapter"], false);
        check(configured is { StartCapture: true, OpenMeter: true, OpenCooldowns: true, SetupMessage: null },
            "Valid saved configuration resumes capture and both configured overlays");
        var missing = StartupPlan.Create(settings, ["unrelated-adapter"], false);
        check(!missing.StartCapture && missing.SetupMessage != null && missing.OpenMeter && missing.OpenCooldowns,
            "Missing saved adapter requests setup without substituting another device or losing overlay choices");
        check(!StartupPlan.Create(settings, ["fixture-adapter"], true).StartCapture, "Missing capture driver prevents automatic capture");
        settings.Filter = " "; check(!StartupPlan.Create(settings, ["fixture-adapter"], false).StartCapture, "Empty saved filters require setup");
        settings.Filter = "tcp"; settings.AutoStartConfigured = false;
        var disabled = StartupPlan.Create(settings, ["fixture-adapter"], false);
        check(disabled == new StartupPlan(false, false, false, null), "Opting out suppresses automatic capture and overlay opening");
        settings.AutoStartConfigured = true;
        check(StartupPlan.Create(settings, ["fixture-adapter"], false, demo: true) == disabled, "Demo and smoke-test startup cannot open capture or restore private overlays");
        settings.OpenMeterOnStartup = settings.OpenCooldownsOnStartup = false;
        check(StartupPlan.Create(settings, ["fixture-adapter"], false) is { StartCapture: true, OpenMeter: false, OpenCooldowns: false },
            "Each overlay can be excluded while automatic capture stays enabled");
        settings.OpenMeterOnStartup = settings.OpenCooldownsOnStartup = true;
        settings.OverlayLeft = double.NaN; settings.CooldownSkills = [0, -1];
        check(StartupPlan.Create(settings, ["fixture-adapter"], false) is { OpenMeter: false, OpenCooldowns: false }, "Unconfigured overlays are not reopened");
        settings.OverlayLeft = null; settings.AutoStartConfigured = false; settings.DecodeEu = false;
        var restored = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(settings))!;
        check(!restored.AutoStartConfigured && !restored.DecodeEu && restored.Adapter == "fixture-adapter", "Startup and decoder choices survive settings reload");

        var names = new SkillNames(System.IO.Path.Combine(temp, "no-startup-name-overrides.json"), true);
        var tracker = new CooldownTracker(); var fight = new Fight { LocalPlayer = 1 };
        fight.Network.Add(new("skill-use", 10000, "02 38", JsonSerializer.SerializeToElement(new { ActorId = 1, SkillCode = 17060343, Sequence = 1 })));
        tracker.Observe(fight, 12000, 20000);
        settings.CooldownSkills = [17060000, 17060343, 17100000];
        var rows = CooldownPresentation.Rows(settings, names, tracker, true, 20000);
        check(rows.Length == 2 && rows[0].Remaining == "~42s" && rows[1].Remaining == "--", "Cooldown page groups charge tiers and shares overlay timing without inventing unknown timers");
        var later = CooldownPresentation.Rows(settings, names, tracker, true, 21000);
        check(later[0].Remaining == "~41s" && later[0].SkillCode == rows[0].SkillCode, "Cooldown page ticks between packet updates with stable row identity");
        check(CooldownPresentation.Rows(settings, names, tracker, false, 21000).All(r => r.Remaining == "--" && r.Status == "Start capture"),
            "Stopping capture removes stale countdowns from the cooldown page");
    }
}
