namespace AionDpsMeter.Local;

internal static class BossPersonalBestTests
{
    internal static void Run(Action<bool, string> check)
    {
        Fight Attempt(int damage) {
            var f = FeatureTests.Sample(); f.Actors[100] = f.Actors[100] with { NpcId = 2300371 };
            f.Damage[0] = f.Damage[0] with { Damage = damage }; f.Damage[1] = f.Damage[1] with { Damage = damage };
            f.Deaths.Add(new(100, 31000)); return f;
        }
        var baseline = Attempt(1000); var improved = Attempt(2000);
        var record = BossPersonalBest.Records(improved).Single();
        check(record.Damage == 4000 && record.Seconds == 30 && record.Dps == 4000d / 30,
            "Boss personal records count local damage through a captured boss death and use the observed boss duration");
        var summary = FightArchiveSummary.Create(baseline);
        var result = BossPersonalBest.Improvements(FightArchiveSummary.Create(improved), [summary]);
        check(summary.Damage.Count == 0 && summary.Network.Count == 0 && result.Single().PreviousDps == 2000d / 30 && result[0].Current.Dps == 4000d / 30,
            "Personal-best comparison works entirely from compact archive records without retained event histories");
        check(BossPersonalBest.Improvements(baseline, [summary]).Length == 0 && BossPersonalBest.Improvements(improved, []).Length == 0 &&
            BossPersonalBest.Improvements(Attempt(1000), [summary]).Length == 0,
            "First observations, same-fight autosaves and tied DPS do not manufacture a new personal best");
        var other = improved.Copy(); other.Actors[1] = other.Actors[1] with { Server = 999 };
        check(BossPersonalBest.Improvements(other, [summary]).Length == 0,
            "Boss records do not compare different character servers");
        other = improved.Copy(); other.Demo = true;
        var training = improved.Copy(); training.Training = new(1000, 31000, 30, "", "");
        var wipe = improved.Copy(); wipe.Deaths.Clear();
        check(BossPersonalBest.Records(other).Length == 0 && BossPersonalBest.Records(training).Length == 0 && BossPersonalBest.Records(wipe).Length == 0,
            "Demos, training and fights without a captured boss death cannot establish a boss personal best");
        improved.Damage.Add(new() { ActorId = 1, TargetId = 100, Damage = 90000, Timestamp = 40000 });
        check(BossPersonalBest.Records(improved).Single().Damage == 4000,
            "Damage after the recorded boss death cannot inflate a personal best");
    }
}
