namespace AionDpsMeter.Local;

internal sealed record TrackedCooldownRow(int SkillCode, string Skill, string Remaining, string Status);

internal static class CooldownPresentation
{
    internal static double? DisplaySeconds(double? seconds, Preferences settings) => seconds is double value
        ? Math.Max(0, value - (settings.CooldownOneSecondEarly ? 1 : 0)) : null;

    internal static TrackedCooldownRow[] Rows(Preferences settings, SkillNames names, CooldownTracker tracker, bool capturing, long now) =>
        CooldownSkillChoice.Group(settings.CooldownSkills ?? [], names).Take(24).Select(skill => {
            var reading = capturing ? tracker.Reading(skill.Id, now, names, settings.CooldownFallbackSeconds) : ((double? Seconds, bool Estimated))(null, false);
            double? seconds = DisplaySeconds(reading.Seconds, settings);
            return new TrackedCooldownRow(skill.Id, skill.Name, seconds == null ? "--" : seconds == 0 ? "Ready" :
                (reading.Estimated ? "~" : "") + Math.Ceiling(seconds.Value).ToString() + "s",
                !capturing ? "Start capture" : seconds == null ? "Waiting for a cast or cooldown" : reading.Estimated ? "Cast estimate" : "Server report");
        }).ToArray();
}
