using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

public sealed record EncounterParticipant(int Id, string Participant, string Class, long Damage, double DPS, long Healing, double HPS, int Deaths)
{
    public double DamagePercentOfLeader { get; init; }
}
public sealed record EncounterSkill(int ActorId, string Participant, string Skill, int SkillCode, int Hits, long Damage, double DPS, double DamageShare, double CritPercent);
internal static class EncounterOverview
{
    internal static string Title(Fight fight)
    {
        string bosses = BossAnalysis.Summary(fight);
        return bosses.Length > 0 ? bosses : string.IsNullOrWhiteSpace(fight.Label) ? "Unclassified encounter" : fight.Label;
    }
    internal static EncounterParticipant[] Participants(Fight fight)
    {
        var classes = ClassEvidence.Infer(fight);
        var deaths = fight.Deaths.GroupBy(d => d.EntityId).ToDictionary(g => g.Key, g => g.Count());
        var rows = Analysis.Totals(fight).Where(r => IsNamedPlayer(fight, r.Id))
            .Select(r => new EncounterParticipant(r.Id, fight.Name(r.Id), ClassEvidence.Resolve(fight.Actors.GetValueOrDefault(r.Id), r.Id, classes),
                r.Damage, r.DPS, r.Healing, r.HPS, deaths.GetValueOrDefault(r.Id)))
            .OrderByDescending(r => r.DPS).ThenByDescending(r => r.Damage).ThenBy(r => r.Id).ToArray();
        long leader = rows.Select(r => r.Damage).DefaultIfEmpty(0).Max();
        return rows.Select(r => r with { DamagePercentOfLeader = leader > 0 ? 100d * r.Damage / leader : 0 }).ToArray();
    }
    static bool IsNamedPlayer(Fight fight, int id) =>
        fight.Actors.GetValueOrDefault(id) is { Player: true, NpcId: 0 } actor &&
        !string.IsNullOrWhiteSpace(actor.Name) && !actor.Name.Trim().StartsWith("Entity ", StringComparison.OrdinalIgnoreCase);

    internal static EncounterSkill[] Skills(Fight fight, IEnumerable<int> actors, Func<int, string> name)
    {
        var included = actors.Where(id => IsNamedPlayer(fight, id)).ToHashSet();
        var hits = fight.Damage.Where(h => included.Contains(h.ActorId)).ToArray();
        double total = hits.Sum(h => (long)h.Damage), seconds = fight.Seconds;
        return hits.GroupBy(h => (h.ActorId, h.SkillCode)).Select(g => {
            long damage = g.Sum(h => (long)h.Damage);
            return new EncounterSkill(g.Key.ActorId, fight.Name(g.Key.ActorId), name(g.Key.SkillCode), g.Key.SkillCode, g.Count(), damage,
                damage / seconds, total == 0 ? 0 : 100d * damage / total, 100d * g.Count(h => h.IsCritical) / g.Count());
        }).OrderByDescending(r => r.DPS).ThenByDescending(r => r.Damage).ThenBy(r => r.ActorId).ThenBy(r => r.SkillCode).ToArray();
    }
}
