namespace AionDpsMeter.Local;

internal sealed record PvpExchangeSkill(string Direction, string Character, int SkillCode, string Skill, int Hits, long Damage, double DPS, double CritPercent);
internal sealed record PvpExchange(Fight ToOpponent, Fight FromOpponent, PvpExchangeSkill[] Skills)
{
    internal long DamageToOpponent => ToOpponent.Damage.Sum(h => (long)h.Damage);
    internal long DamageFromOpponent => FromOpponent.Damage.Sum(h => (long)h.Damage);
}
internal static class PvpOpponentAnalysis
{
    internal static PvpExchange Build(Fight source, int opponent, bool includeParty, Func<int, string> skillName)
    {
        if (!PvpAnalysis.Build(source).Opponents.Any(o => o.Id == opponent)) throw new ArgumentException("Choose an observed PvP opponent.");
        bool Friendly(int id) => id > 0 && (id == source.LocalPlayer || includeParty && source.Actors.GetValueOrDefault(id)?.Party == true);
        var hits = source.Damage.Where(h => h.Damage > 0 && !h.IsHeal &&
            (h.TargetId == opponent && Friendly(h.ActorId) || h.ActorId == opponent && Friendly(h.TargetId))).ToArray();
        long start = hits.Select(h => h.Timestamp).DefaultIfEmpty(0).Min(), end = hits.Select(h => h.Timestamp).DefaultIfEmpty(0).Max();
        Fight View(bool outgoing) => new() { Id = source.Id, Label = source.Label, LocalPlayer = source.LocalPlayer,
            Actors = source.Actors, DisplayWindow = (start, end),
            Damage = hits.Where(h => outgoing ? h.TargetId == opponent : h.ActorId == opponent).ToList() };
        var to = View(true); var from = View(false);
        var rows = hits.GroupBy(h => (Outgoing: h.TargetId == opponent, h.ActorId, h.SkillCode)).Select(g => new PvpExchangeSkill(
            g.Key.Outgoing ? "To opponent" : "From opponent", source.Name(g.Key.ActorId), g.Key.SkillCode, skillName(g.Key.SkillCode),
            g.Count(), g.Sum(h => (long)h.Damage), g.Sum(h => (long)h.Damage) / to.Seconds, 100d * g.Count(h => h.IsCritical) / g.Count()))
            .OrderByDescending(r => r.Damage).ThenBy(r => r.SkillCode).ToArray();
        return new(to, from, rows);
    }
}
