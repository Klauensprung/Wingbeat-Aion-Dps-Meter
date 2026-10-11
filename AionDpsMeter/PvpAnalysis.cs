namespace AionDpsMeter.Local;

public sealed record PvpCombatant(int Id, string Name, string Job, string Identity, bool Friendly, long Damage, long Healing);
public sealed record PvpData(long Start, long End, int LocalPlayer, PvpCombatant[] Combatants, OpponentRow[] Opponents)
{
    public double Seconds => Math.Max(1, (End - Start) / 1000d);
    public long YourDamage => Combatants.FirstOrDefault(c => c.Id == LocalPlayer)?.Damage ?? 0;
    public long EnemyDamage => Combatants.Where(c => !c.Friendly).Sum(c => c.Damage);
    public bool HasOpponents => Opponents.Length > 0;
}

public static class PvpAnalysis
{
    // Display evidence only, never a fabricated identity or a change to captured hits.
    // Class prefixes and summon-only families are documented in THIRD-PARTY-NOTICES.txt.
    public static PvpData Build(Fight fight)
    {
        bool Friendly(int id) => id > 0 && (id == fight.LocalPlayer || fight.Actors.GetValueOrDefault(id)?.Party == true);
        var jobs = ClassEvidence.Infer(fight);
        bool PlayerLike(int id) => id > 0 && fight.Actors.GetValueOrDefault(id)?.NpcId is not > 0 &&
            (fight.Actors.GetValueOrDefault(id)?.Player == true || jobs.ContainsKey(id));
        string Job(int id) => ClassEvidence.Resolve(fight.Actors.GetValueOrDefault(id), id, jobs);
        string Name(int id) => fight.Name(id);
        string Identity(int id) => fight.Actors.GetValueOrDefault(id)?.Player == true && fight.Name(id) != $"Entity {id}" ? "Named" : "Name pending";

        // Only exchanges with your side establish hostility. Other nearby fights and
        // PvE damage cannot inflate the PvP totals or extend the shared DPS duration.
        var hits = fight.Damage.Where(e => e.Damage > 0 && e.ActorId != e.TargetId &&
            ((Friendly(e.ActorId) && !Friendly(e.TargetId) && PlayerLike(e.TargetId)) ||
             (Friendly(e.TargetId) && !Friendly(e.ActorId) && PlayerLike(e.ActorId)))).ToArray();
        long start = hits.Select(e => e.Timestamp).DefaultIfEmpty(0).Min();
        long end = hits.Select(e => e.Timestamp).DefaultIfEmpty(0).Max();
        double seconds = Math.Max(1, (end - start) / 1000d);
        var outgoing = hits.ToLookup(e => e.ActorId);
        var incoming = hits.ToLookup(e => e.TargetId);
        var healing = fight.Healing.Where(e => hits.Length > 0 && e.Timestamp >= start && e.Timestamp <= end).ToLookup(e => e.ActorId);
        var ids = hits.SelectMany(e => new[] { e.ActorId, e.TargetId }).ToHashSet();
        if (fight.LocalPlayer > 0) ids.Add(fight.LocalPlayer);
        var combatants = ids.Select(id => new PvpCombatant(id, Name(id), Job(id), Identity(id), Friendly(id),
            outgoing[id].Sum(e => (long)e.Damage), healing[id].Sum(e => (long)e.Damage)))
            .OrderByDescending(c => c.Id == fight.LocalPlayer).ThenByDescending(c => c.Damage).ThenBy(c => c.Id).ToArray();
        var opponents = combatants.Where(c => !c.Friendly).Select(c => new OpponentRow(c.Id, c.Name, c.Identity,
            c.Damage, incoming[c.Id].Sum(e => (long)e.Damage), Math.Round(c.Damage / seconds, 1), c.Healing)).ToArray();
        return new(start, end, fight.LocalPlayer, combatants, opponents);
    }

    public static MeterData Meter(Fight fight, PvpData pvp, bool healing = false)
    {
        long Value(PvpCombatant actor) => healing ? actor.Healing : actor.Damage;
        long total = pvp.Combatants.Sum(Value), maximum = pvp.Combatants.Select(Value).DefaultIfEmpty(0).Max();
        // Keep every opponent, including outgoing-only encounters and zero-damage
        // opponents in healing mode. There is no top-ten cutoff for PvP.
        var rows = pvp.Combatants.OrderByDescending(c => c.Id == fight.LocalPlayer).ThenBy(c => c.Friendly).ThenByDescending(Value).ThenBy(c => c.Id).Select(c => {
            long amount = Value(c);
            string color = c.Friendly ? MeterPresentation.ColorFor(new Actor(c.Id, c.Name, true, false, 0, c.Job, 0)) : "#EF8792";
            return new MeterRow(c.Id, c.Name, c.Job, amount, amount / pvp.Seconds,
                total == 0 ? 0 : 100d * amount / total, maximum == 0 ? 0 : (double)amount / maximum,
                color, c.Id == fight.LocalPlayer, true) { Detail = (c.Friendly ? "Your side" : "Opponent") + (c.Identity == "Named" ? "" : " · name pending; class inferred from skills") };
        }).ToArray();
        return new(rows, total, total / pvp.Seconds, "PvP");
    }
}
