using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class EffectAnalysisTests
{
    internal static void Run(Action<bool, string> check)
    {
        var fight = FeatureTests.Sample();
        void Apply(long time, int slot, long duration, int source = 1, int effect = 10, string kind = "abnormal-apply") =>
            fight.Network.Add(new(kind, time, "test", JsonSerializer.SerializeToElement(new { TargetId = 2, Slot = slot, AbnormalId = effect, DurationMs = duration, CasterId = source, SourceSkillId = 17010001 })));
        void Remove(long time, int slot) => fight.Network.Add(new("abnormal-remove", time, "test", JsonSerializer.SerializeToElement(new { TargetId = 2, Slot = slot })));
        Apply(1000, 1, 10000); Apply(3000, 1, 10000, kind: "abnormal-refresh"); Remove(6000, 1);
        Apply(2000, 2, 2000);
        var row = EffectAnalysis.Build(fight, code => "Synthetic skill").Single();
        check(row.TrackedSeconds == 5 && Math.Abs(row.TrackedUptime - 100d / 6) < .001 && row.TrackedSegments == 3 && !row.HasUnknownDuration,
            "Effect uptime unions overlapping slots, refreshes durations and ends on removal without double counting");
        Apply(11000, 3, 2000, source: 3); Apply(13000, 3, 1000, source: 3, effect: 11);
        var rows = EffectAnalysis.Build(fight, code => "Synthetic skill");
        check(rows.Length == 3 && rows.Single(r => r.Source == "Member 3" && r.EffectId == 10).TrackedSeconds == 2 &&
            rows.Single(r => r.EffectId == 11).TrackedSeconds == 1,
            "Effect analysis preserves separate sources and expires replaced slots at their recorded duration");
        Apply(0, 4, uint.MaxValue, source: 0, effect: 12); Remove(2000, 4);
        var unknown = EffectAnalysis.Build(fight, code => "Synthetic skill").Single(r => r.EffectId == 12);
        check(unknown.TrackedSeconds == 1 && unknown.HasUnknownDuration && unknown.Source == "Source not captured",
            "Unknown-duration effects are marked and clipped to the combat window without inventing support attribution");
        fight.Network.Reverse();
        check(EffectAnalysis.Build(fight, code => "Synthetic skill").Single(r => r.EffectId == 12) == unknown,
            "Out-of-order captured effect events are processed chronologically");
        fight.Actors[99] = fight.Actors[2] with { Id = 99, OriginalEntityId = 2 };
        check(EffectAnalysis.Build(fight, code => "Synthetic skill").Length == 0,
            "Recycled target IDs do not inherit ambiguous effect uptime");
        fight.Actors.Remove(99); fight.Network.Clear(); Apply(1000, 1, 60000);
        fight.Network.Add(new("entity-removed", 4000, "test", JsonSerializer.SerializeToElement(new { EntityId = 2 })));
        check(EffectAnalysis.Build(fight, code => "Skill").Single().TrackedSeconds == 3,
            "Saved effect uptime ends when its recipient leaves view");
        Apply(5000, 2, 60000);
        fight.Network.Add(new("local-zone", 6000, "test", JsonSerializer.SerializeToElement(new { MapId = 1010 })));
        check(EffectAnalysis.Build(fight, code => "Skill").Single().TrackedSeconds == 4,
            "Scene changes end active effect intervals without extending them to fight end");
    }
}
