namespace AionDpsMeter.Local;

// Display-only inference. Keep captured identities unchanged and exclude NPCs
// and summon-only skills, which do not establish a player's class.
internal static class ClassEvidence
{
    static readonly string[] Jobs = ["Gladiator", "Templar", "Assassin", "Ranger", "Sorcerer", "Spiritmaster", "Cleric", "Chanter", "Brawler"];
    static bool CharacterSkill(int code) => code is >= 11000000 and < 20000000 &&
        code / 10000 is not (1528 or 1600 or 1610 or 1611 or 1612 or 1613 or 1615 or 1625 or 1699 or 1715);

    internal static Dictionary<int, string> Infer(Fight fight) => fight.Damage.Concat(fight.Healing)
        .Where(e => CharacterSkill(e.SkillCode) && fight.Actors.GetValueOrDefault(e.ActorId)?.NpcId is not > 0)
        .GroupBy(e => e.ActorId).ToDictionary(g => g.Key, g => Jobs[g.GroupBy(e => e.SkillCode / 1000000)
            .OrderByDescending(c => c.Count()).ThenBy(c => c.Key).First().Key - 11]);

    internal static string Resolve(Actor? actor, int id, IReadOnlyDictionary<int, string> inferred)
    {
        if (actor?.NpcId is > 0) return "Unknown";
        return !string.IsNullOrWhiteSpace(actor?.Job) && !actor.Job.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            ? actor.Job : inferred.GetValueOrDefault(id, "Unknown");
    }
}
