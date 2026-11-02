using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class ShieldAnalysisTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var fight = FeatureTests.Sample(); fight.Network.Clear();
        long at = fight.Start;
        void Shield(long offset, long amount) => fight.Network.Add(new("barrier", at + offset, "test", JsonSerializer.SerializeToElement(new {
            TargetId = 1, ActorId = 2, SkillCode = 17090000, AbnormalId = 1000, Uid = 7, Remaining = amount })));
        Shield(0, 5000000000); Shield(1, 4000000000); Shield(2, 4000000000); Shield(3, 4500000000);
        fight.Network.Add(new("abnormal-remove", at + 4, "test", JsonSerializer.SerializeToElement(new { TargetId = 1, Slot = 7 })));
        Shield(5, 100);
        var summary = ShieldAnalysis.Summarize(fight);
        check(summary is [{ Added: 5500000100, Consumed: 1000000000, Updates: 4 }],
            "Shield summaries track 64-bit increases/decreases, ignore repeats and do not count removed capacity as absorption");
        var compact = fight.ForStorage();
        check(compact.Network.All(n => n.Kind != "barrier") && compact.SavedShields!.SequenceEqual(summary) &&
            ShieldAnalysis.Summarize(compact.Copy()).SequenceEqual(summary),
            "Shield summaries survive compact storage and copies without retaining raw capacity updates");
        var store = new FightStore(Path.Combine(temp, "shields")); store.Save(fight);
        var restored = store.Load(Path.Combine(store.Root, fight.Id + ".json"));
        check(ShieldAnalysis.Summarize(restored).SequenceEqual(summary), "Shield totals roundtrip through saved fight files");
        var preexisting = fight.Copy(); preexisting.Network.Clear();
        preexisting.Network.Add(new("barrier", at - 1, "test", JsonSerializer.SerializeToElement(new { TargetId = 1, Uid = 1, Remaining = 100 })));
        preexisting.Network.Add(new("barrier", at + 1, "test", JsonSerializer.SerializeToElement(new { TargetId = 1, Uid = 1, Remaining = 60 })));
        check(ShieldAnalysis.Summarize(preexisting) is [{ Added: 0, Consumed: 40, CasterId: 0 }],
            "Pre-fight shield capacity establishes a baseline without adding it to fight applications or inventing a caster");
    }
}
