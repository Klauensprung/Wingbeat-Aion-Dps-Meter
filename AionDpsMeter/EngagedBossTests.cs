namespace AionDpsMeter.Local;

internal static class EngagedBossTests
{
    internal static void Run(Action<bool, string> check)
    {
        var fight = FeatureTests.Sample();
        fight.Actors[100] = fight.Actors[100] with { NpcId = 2300371 };
        fight.Actors[101] = fight.Actors[100] with { Id = 101, Name = "Other boss" };
        fight.Actors[50] = new(50, "Nearby player", true, false, 0, "Ranger", 1);
        fight.Damage.Add(new() { ActorId = 50, TargetId = 101, Timestamp = 50000, Damage = 100 });
        check(EngagedBoss.Latest(fight) == 100, "Automatic boss health ignores nearby players fighting an unrelated boss");
        fight.Damage.Add(new() { ActorId = 101, TargetId = 2, Timestamp = 40000, Damage = 100 });
        check(EngagedBoss.Latest(fight) == 101, "Automatic boss health follows the latest recognized boss attacking your party");
        fight.Defense.Add(new() { ActorId = 100, TargetId = 1, Timestamp = 45000, Outcome = AionDpsMeter.Models.DefenseOutcome.Block });
        fight.Damage.Reverse();
        check(EngagedBoss.Latest(fight) == 100, "Defensive engagement and out-of-order hits preserve chronological boss selection");
        fight.Actors[100] = fight.Actors[100] with { NpcId = 0 };
        check(EngagedBoss.Latest(fight) == 101 && EngagedBoss.Latest(new Fight()) == 0,
            "Unclassified creatures cannot become automatic bosses and reset clears boss selection");
    }
}
