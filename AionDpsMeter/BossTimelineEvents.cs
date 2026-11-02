namespace AionDpsMeter.Local;

internal static class BossTimelineEvents
{
    // Skill activity belongs to its own lane, including skill ID zero when the
    // packet did not identify a skill. Other observations describe the encounter.
    internal static bool IsMarker(BossMoment moment) => moment.Kind is not ("Cast signal" or "Hit" or "Defense" or "Telegraph");
    internal static bool Includes(BossMoment moment, int? skill) => skill == null || moment.SkillCode == skill || IsMarker(moment);
}
