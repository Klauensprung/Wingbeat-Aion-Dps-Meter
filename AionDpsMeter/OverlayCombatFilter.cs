namespace AionDpsMeter.Local;

internal static class OverlayCombatFilter
{
    // These views share immutable snapshot metadata and event objects. They are
    // never saved and keep the full encounter's duration for comparable DPS.
    internal static Fight Apply(Fight source, bool bossesOnly, int target)
    {
        if (!bossesOnly && target == 0) return source;
        bool Matches(int id) => target != 0 ? id == target : source.Actors.GetValueOrDefault(id) is { } actor && CapturedBoss.IsBoss(actor);
        return new Fight {
            Id = source.Id, Label = source.Label, Created = source.Created, Demo = source.Demo, LocalPlayer = source.LocalPlayer,
            DisplayWindow = (source.Start, source.End), Actors = source.Actors, Specs = source.Specs, Builds = source.Builds,
            Damage = source.Damage.Where(h => Matches(h.TargetId)).ToList(),
            Healing = source.Healing.Where(h => Matches(h.TargetId)).ToList(),
            Defense = source.Defense.Where(h => Matches(h.ActorId) || Matches(h.TargetId)).ToList(),
            Deaths = source.Deaths, Network = source.Network, PartyKnown = source.PartyKnown, PartySource = source.PartySource,
            PartyProfiles = source.PartyProfiles, LocalStats = source.LocalStats, LocalPing = source.LocalPing, Energy = source.Energy
        };
    }
    internal static int[] Targets(Fight fight) => fight.Damage.Select(h => h.TargetId).Concat(fight.Healing.Select(h => h.TargetId))
        .Concat(fight.Defense.Select(h => h.TargetId)).Where(id => id > 0).Distinct().OrderBy(fight.Name).ThenBy(id => id).ToArray();
}
