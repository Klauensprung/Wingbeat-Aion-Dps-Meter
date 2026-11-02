namespace AionDpsMeter.Local;

internal static class FightChatSummaryTests
{
    internal static void Run(Action<bool, string> check)
    {
        var fight = FeatureTests.Sample();
        fight.Actors[11] = new(11, "Nearby player", true, false, 0, "Ranger", 0);
        fight.Damage.Add(new() { ActorId = 2, TargetId = 100, Damage = 4000, Timestamp = 10000 });
        fight.Damage.Add(new() { ActorId = 11, TargetId = 100, Damage = 9000, Timestamp = 10000 });
        fight.Damage.Add(new() { ActorId = 100, TargetId = 1, Damage = 90000, Timestamp = 10000 });
        var party = FightChatSummary.Build(fight, false, "Party");
        check(party.Length == 4 && party[2].StartsWith("1. Member 2 |") && party[3].StartsWith("2. Example cleric |") &&
            !party.Any(l => l.Contains("Nearby player") || l.Contains("Test target")) && party[0].Contains("00:30"),
            "Chat summary ranks only participating party damage and uses the captured encounter duration");
        var own = FightChatSummary.Build(fight, false, "Self");
        var players = FightChatSummary.Build(fight, false, "Players");
        check(own.Length == 3 && own[2].Contains("Example cleric") && players.Length == 5 && players[2].Contains("Nearby player"),
            "Chat summary self and nearby-player scopes exclude NPC damage");
        var healing = FightChatSummary.Build(fight, true, "Party");
        check(healing.Length == 4 && healing[1].Contains("healing") && healing.Skip(2).All(l => l.Contains("HPS")) &&
            FightChatSummary.Build(new Fight(), false, "Self").Last() == "No damage recorded in this scope.",
            "Chat summary supports healing and clearly reports empty scopes");
        fight.Label = new string('x', 400) + "\nUnexpected line";
        fight.Actors[1] = fight.Actors[1] with { Name = "Player\r\n\t" + new string('z', 400) };
        var sanitized = FightChatSummary.Build(fight, false, "Self");
        check(sanitized.All(l => !l.Any(char.IsControl) && l.Length < 130) && sanitized[2].Contains("…"),
            "Chat summary flattens embedded control characters and bounds labels without adding extra chat lines");
    }
}
