using System.IO;

namespace AionDpsMeter.Local;

public sealed record PartyRoster(int[] Members, string Source = "Imported");
public sealed record ActorBuild(int ActorId, Spec[] Skills, bool Complete = false, string Source = "Imported")
{
    public Dictionary<string, string> Equipment { get; init; } = new();
}

public static class FightMetadata
{
    public static readonly string[] Jobs = ["Unknown", "Gladiator", "Templar", "Assassin", "Ranger", "Sorcerer", "Spiritmaster", "Cleric", "Chanter", "Brawler"];
    public static void Validate(ActorBuild build)
    {
        if (build.ActorId <= 0 || build.Skills == null || build.Skills.Length > 256 || (build.Complete && build.Skills.Length == 0) ||
            build.Skills.Any(s => s == null || s.Skill <= 0 || s.Level is < 0 or > 100 || s.Choices == null || s.Choices.Length > 32 || s.Choices.Any(c => c is < 0 or > 1000)) ||
            build.Skills.Select(s => s.Skill).Distinct().Count() != build.Skills.Length ||
            string.IsNullOrWhiteSpace(build.Source) || build.Source.Length > 80 || build.Equipment == null || build.Equipment.Count > 64 ||
            build.Equipment.Any(e => string.IsNullOrWhiteSpace(e.Key) || e.Key.Length > 80 || string.IsNullOrWhiteSpace(e.Value) || e.Value.Length > 240))
            throw new InvalidDataException("Invalid character loadout. Use distinct positive skill IDs, levels 0–100 and numeric choices.");
    }
    public static void Validate(PartyRoster roster)
    {
        if (roster.Members == null || roster.Members.Length > 10 || roster.Members.Any(id => id <= 0) ||
            roster.Members.Distinct().Count() != roster.Members.Length || string.IsNullOrWhiteSpace(roster.Source) || roster.Source.Length > 80)
            throw new InvalidDataException("A party can contain up to ten distinct players.");
    }
    public static void SetRoster(Fight fight, PartyRoster roster)
    {
        Validate(roster);
        if (roster.Members.Any(id => fight.Actors.GetValueOrDefault(id)?.Player != true) || (fight.LocalPlayer > 0 && fight.Actors.GetValueOrDefault(fight.LocalPlayer)?.Player != true))
            throw new InvalidDataException("Identify party members as players before assigning the roster.");
        var members = roster.Members.ToHashSet();
        if (fight.LocalPlayer > 0) members.Add(fight.LocalPlayer);
        if (members.Count > 10) throw new InvalidDataException("The ten-player roster includes your own character.");
        foreach (var (id, actor) in fight.Actors.ToArray()) fight.Actors[id] = actor with { Party = members.Contains(id) };
        fight.PartyKnown = true; fight.PartySource = roster.Source;
    }
    public static void SetBuild(Fight fight, ActorBuild build)
    {
        Validate(build);
        if (fight.Actors.GetValueOrDefault(build.ActorId)?.Player != true) throw new InvalidDataException("Loadouts must belong to an identified player.");
        fight.Builds[build.ActorId] = Copy(build);
    }
    public static ActorBuild Copy(ActorBuild build) => build with {
        Skills = build.Skills.Select(s => s with { Choices = s.Choices.ToArray() }).ToArray(), Equipment = new(build.Equipment)
    };
    public static ActorBuild? BuildFor(Fight fight, int actor)
        => fight.Builds.GetValueOrDefault(actor) ?? (actor == fight.LocalPlayer && fight.Specs.Count > 0
            ? new(actor, fight.Specs.Values.ToArray(), false, "Legacy captured/imported") : null);
    public static bool InParty(Fight fight, int id) => id == fight.LocalPlayer || fight.Actors.GetValueOrDefault(id)?.Party == true;
}
