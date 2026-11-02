namespace AionDpsMeter.Local;

internal sealed record TrackedCooldownRow(int SkillCode, string Skill, string Remaining, string Status);

internal static class CooldownPresentation
{
    internal static string Style(string? value) => value is "Round" or "Bars" ? value : "Square";
    internal static double RemainingFraction(double? remaining, double maximum) => remaining is double seconds &&
        double.IsFinite(seconds) && double.IsFinite(maximum) && maximum > 0 ? Math.Clamp(seconds / maximum, 0, 1) : 0;
    internal static double? DisplaySeconds(double? seconds, Preferences settings) => seconds is double value
        ? Math.Max(0, value - (settings.CooldownOneSecondEarly ? 1 : 0)) : null;

    internal static TrackedCooldownRow[] Rows(Preferences settings, SkillNames names, CooldownTracker tracker, bool capturing, long now) =>
        CooldownSkillChoice.Group(settings.CooldownSkills ?? [], names).Take(24).Select(skill => {
            var reading = capturing ? tracker.Reading(skill.Id, now, names, settings.CooldownFallbackSeconds) : ((double? Seconds, bool Estimated))(null, false);
            double? seconds = DisplaySeconds(reading.Seconds, settings);
            if (capturing && tracker.ChargeDisplay(skill.Id, now) is { } charge)
                return new TrackedCooldownRow(skill.Id, skill.Name,
                    seconds > 0 ? Math.Ceiling(seconds.Value) + "s recharge" : "Recharged",
                    charge.Estimated ? $"Estimated: {charge.Count} charge(s)" : $"Last reported: {charge.Count} charge(s)");
            return new TrackedCooldownRow(skill.Id, skill.Name, seconds == null ? "--" : seconds == 0 ? "Ready" :
                (reading.Estimated ? "~" : "") + Math.Ceiling(seconds.Value).ToString() + "s",
                !capturing ? "Start capture" : seconds == null ? "Waiting for a cast or cooldown" : reading.Estimated ? "Cast estimate" : "Server report");
        }).ToArray();
}
