using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class EffectDurationTests
{
    internal static void Run(Action<bool, string> check)
    {
        JsonElement Fields(object value) => JsonSerializer.SerializeToElement(value);
        check(EffectDuration.TryRead(Fields(new { DurationMs = uint.MaxValue }), out var old, out _) && old == null &&
            EffectDuration.TryRead(Fields(new { DurationMs = uint.MaxValue, RawDurationMs = (long)uint.MaxValue }), out var modern, out _) && modern == uint.MaxValue,
            "Legacy unknown durations remain compatible while the same positive wire duration stays finite");
        check(!EffectDuration.TryRead(Fields(new { DurationMs = "invalid" }), out _, out _) &&
            !EffectDuration.TryRead(Fields(new { DurationMs = -1 }), out _, out _),
            "Malformed duration fields are ignored without exceptions");
        check(EffectDuration.Deadline(long.MaxValue, 100, 200, 1000) == long.MaxValue &&
            EffectDuration.Deadline(500, 100, 700, 1000) == 1000,
            "Duration conversion saturates safely and expired effects do not gain new lifetime");
        var fight = FeatureTests.Sample(); fight.Network.Clear();
        fight.Network.Add(new("abnormal-apply", fight.Start, "test", Fields(new {
            TargetId = fight.LocalPlayer, Slot = 1, AbnormalId = 10, DurationMs = long.MaxValue, RawDurationMs = long.MaxValue })));
        var tracker = new LiveEffectTracker(); tracker.Observe(fight, fight.Start, 1000);
        check(tracker.Active(1000) is [{ RemainingSeconds: > 86400, UntilRemoved: false }] &&
            EffectAnalysis.Intervals(fight) is [{ DurationKnown: true }],
            "Long finite effects survive both live overlay tracking and saved effect analysis");
    }
}
