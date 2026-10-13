using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal sealed record BossAbility(int SkillCode, string Skill, int Casts, int Hits, long Damage, double DamageShare,
    int Targets, int BiggestHit, double AverageHit, double? CastGap, int Blocks, int Parries, int Avoids);
internal sealed record BossParticipant(int Id, string Player, string Class, long Damage, double DPS, double Share,
    long Taken, int HitsTaken, int Deaths);
internal sealed record BossAbilityTarget(int SkillCode, int EntityId, string Target, int Hits, long Damage, double Share, int BiggestHit);
internal sealed record BossMoment(long Timestamp, int Sequence, string Kind, int SkillCode, int TargetId, long Amount, string Detail);
internal sealed record BossHp(long Timestamp, long Value);
internal sealed record BossDamageBin(double Seconds, double Width, long ToBoss, long FromBoss);
internal sealed record BossEncounterData(string FightId, int EntityId, int NpcId, string Name, string Location, string Identification,
    long Start, long End, bool DeathObserved, BossAbility[] Abilities, BossParticipant[] Participants,
    BossMoment[] Moments, BossHp[] Hp, BossDamageBin[] Bins, BossAbilityTarget[] Targets)
{
    public int OriginalEntityId { get; init; }
    public bool AmbiguousNetworkIdentity { get; init; }
    public int DisplayEntityId => OriginalEntityId > 0 ? OriginalEntityId : EntityId;
    public double Seconds => Math.Max(1, (End - Start) / 1000d);
    public long DamageToBoss => Bins.Sum(b => b.ToBoss);
    public long DamageFromBoss => Bins.Sum(b => b.FromBoss);
    public int Casts => Abilities.Sum(a => a.Casts);
    public string Label => $"{Name}  /  Entity {DisplayEntityId}" + (OriginalEntityId > 0 ? " (earlier identity)" : "");
}

// Analysis of recorded evidence. Cast signals, impact packets and defensive outcomes
// are deliberately separate: an AoE or multi-hit skill is not multiple casts.
internal static class BossEncounterAnalysis
{
    sealed record Signal(int Entity, NetworkEvent Event, int Skill, long Value);
    static long? Number(JsonElement fields, string key) => fields.ValueKind == JsonValueKind.Object &&
        fields.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long n) ? n : null;
    static Signal? Read(NetworkEvent e)
    {
        if (e.Timestamp < 0) return null;
        string? key = e.Kind switch { "boss-cast" => "CasterId", "boss-enrage" or "hp" => "EntityId", _ => null };
        if (key == null || Number(e.Fields, key) is not long entity || entity <= 0 || entity > int.MaxValue) return null;
        if (e.Kind == "boss-cast" && Number(e.Fields, "SkillCode") is > 0 and <= int.MaxValue and var skill)
            return new((int)entity, e, (int)skill, 0);
        if (e.Kind == "hp" && Number(e.Fields, "CurrentHp") is >= 0 and var hp) return new((int)entity, e, 0, hp);
        if (e.Kind == "boss-enrage") return new((int)entity, e, 0, Number(e.Fields, "AbnormalId") ?? 0);
        return null;
    }
    internal static BossEncounterData[] Build(Fight fight, Func<int, string> names)
    {
        var catalog = BossCatalogue.Default;
        var hits = fight.Damage.Where(h => !h.IsHeal && h.Damage > 0 && h.Timestamp >= 0).ToArray();
        var to = hits.ToLookup(h => h.TargetId); var from = hits.ToLookup(h => h.ActorId);
        var defense = fight.Defense.Where(d => d.Timestamp >= 0).ToLookup(d => d.ActorId);
        // Damage and deaths are remapped when the engine archives an identity.
        // Raw network diagnostics intentionally retain their original IDs. Old
        // archives have no reassignment timestamp, so they cannot be disambiguated.
        var reusedIds = fight.Actors.Values.Where(a => a.OriginalEntityId > 0).Select(a => a.OriginalEntityId).ToHashSet();
        var signals = fight.Network.Select(Read).OfType<Signal>().Where(s => !reusedIds.Contains(s.Entity)).ToArray();
        var byEntity = signals.ToLookup(s => s.Entity);
        var castIds = signals.Where(s => s.Event.Kind == "boss-cast").Select(s => s.Entity).ToHashSet();
        var engaged = hits.SelectMany(h => new[] { h.ActorId, h.TargetId }).Concat(fight.Defense.SelectMany(d => new[] { d.ActorId, d.TargetId })).Concat(castIds).ToHashSet();
        var ids = fight.Actors.Values.Where(a => !a.Player && a.Id != fight.LocalPlayer && catalog.IsEncounterBoss(a.NpcId) && engaged.Contains(a.Id)).Select(a => a.Id)
            .Concat(castIds.Where(id => id != fight.LocalPlayer && fight.Actors.GetValueOrDefault(id)?.Player != true)).Distinct();
        var deaths = fight.Deaths.ToLookup(d => d.EntityId);
        var result = new List<BossEncounterData>();
        foreach (int id in ids) {
            var actor = fight.Actors.GetValueOrDefault(id); var known = catalog.Find(actor?.NpcId ?? 0);
            var dealt = to[id].ToArray(); var taken = from[id].ToArray(); var defenses = defense[id].ToArray();
            var casts = byEntity[id].Where(s => s.Event.Kind == "boss-cast").DistinctBy(s => (s.Event.Timestamp, s.Skill)).ToArray();
            var enrages = byEntity[id].Where(s => s.Event.Kind == "boss-enrage").DistinctBy(s => (s.Event.Timestamp, s.Value)).ToArray();
            var times = dealt.Concat(taken).Select(h => h.Timestamp).Concat(casts.Select(c => c.Event.Timestamp)).Concat(defenses.Select(d => d.Timestamp)).Concat(enrages.Select(e => e.Event.Timestamp)).ToArray();
            if (times.Length == 0) continue;
            long start = times.Min(), end = times.Max();
            var observedDeaths = deaths[id].Where(d => d.Timestamp >= start).ToArray();
            if (observedDeaths.Length > 0) end = Math.Max(end, observedDeaths.Max(d => d.Timestamp));
            double seconds = Math.Max(1, (end - start) / 1000d);
            long total = taken.Sum(h => (long)h.Damage);
            var hitsBySkill = taken.ToLookup(h => h.SkillCode); var castsBySkill = casts.ToLookup(c => c.Skill);
            var defenseBySkill = defenses.ToLookup(d => d.SkillCode);
            var abilities = hitsBySkill.Select(g => g.Key).Concat(castsBySkill.Select(g => g.Key)).Concat(defenseBySkill.Select(g => g.Key)).Distinct().Select(skill => {
                var impacts = hitsBySkill[skill].ToArray(); var announcements = castsBySkill[skill].OrderBy(c => c.Event.Timestamp).ToArray();
                var gaps = announcements.Zip(announcements.Skip(1), (a, b) => (b.Event.Timestamp - a.Event.Timestamp) / 1000d).Where(s => s > 0).Order().ToArray();
                var outcomes = defenseBySkill[skill]; long damage = impacts.Sum(h => (long)h.Damage);
                return new BossAbility(skill, skill > 0 ? names(skill) : "Unspecified skill", announcements.Length, impacts.Length, damage,
                    total == 0 ? 0 : damage * 100d / total, impacts.Select(h => h.TargetId).Distinct().Count(), impacts.Select(h => h.Damage).DefaultIfEmpty(0).Max(),
                    impacts.Length == 0 ? 0 : damage / (double)impacts.Length, gaps.Length == 0 ? null : (gaps[(gaps.Length - 1) / 2] + gaps[gaps.Length / 2]) / 2,
                    outcomes.Count(d => d.Outcome == DefenseOutcome.Block), outcomes.Count(d => d.Outcome == DefenseOutcome.Parry),
                    outcomes.Count(d => d.Outcome is DefenseOutcome.Evade or DefenseOutcome.Resist or DefenseOutcome.TargetAvoided));
            }).OrderByDescending(a => a.Damage).ThenByDescending(a => a.Casts).ThenBy(a => a.SkillCode).ToArray();
            var outgoing = dealt.ToLookup(h => h.ActorId); var incoming = taken.ToLookup(h => h.TargetId);
            bool Participant(int other) => other > 0 && other != id && (fight.Actors.GetValueOrDefault(other) is not { Player: false, NpcId: > 0 });
            long allDealt = dealt.Sum(h => (long)h.Damage);
            var participants = outgoing.Select(g => g.Key).Concat(incoming.Select(g => g.Key)).Distinct().Where(Participant).Select(other => {
                long damage = outgoing[other].Sum(h => (long)h.Damage); var target = fight.Actors.GetValueOrDefault(other);
                string job = target is { Job: not "Unknown" } ? target.Job : target?.CachedJob ?? "Unknown";
                return new BossParticipant(other, fight.Name(other), job, damage, damage / seconds, allDealt == 0 ? 0 : damage * 100d / allDealt,
                    incoming[other].Sum(h => (long)h.Damage), incoming[other].Count(), deaths[other].Count(d => d.Timestamp >= start && d.Timestamp <= end));
            }).OrderByDescending(p => p.Damage).ThenBy(p => p.Id).ToArray();
            var moments = new List<BossMoment>(); int sequence = 0;
            foreach (var c in casts) moments.Add(new(c.Event.Timestamp, sequence++, "Cast signal", c.Skill, 0, 0, "Recorded cast signal; completion is not confirmed."));
            foreach (var h in taken) moments.Add(new(h.Timestamp, sequence++, "Hit", h.SkillCode, h.TargetId, h.Damage, h.IsCritical ? "Critical damage" : "Damage"));
            foreach (var d in defenses) moments.Add(new(d.Timestamp, sequence++, "Defense", d.SkillCode, d.TargetId, 0, d.Outcome.ToString()));
            foreach (var e in enrages.Where(e => e.Event.Timestamp >= start && e.Event.Timestamp <= end)) moments.Add(new(e.Event.Timestamp, sequence++, "Enrage", 0, 0, 0, $"Recorded enrage signal / effect {e.Value}"));
            foreach (var d in observedDeaths) moments.Add(new(d.Timestamp, sequence++, "Boss death", 0, id, 0, "Recorded entity-death event"));
            var hp = byEntity[id].Where(s => s.Event.Kind == "hp" && s.Event.Timestamp >= start && s.Event.Timestamp <= end)
                .Select(s => new BossHp(s.Event.Timestamp, s.Value)).Distinct().OrderBy(p => p.Timestamp).ToArray();
            var skillTotals = abilities.ToDictionary(a => a.SkillCode, a => a.Damage);
            var targets = taken.GroupBy(h => (h.SkillCode, h.TargetId)).Select(g => {
                long damage = g.Sum(h => (long)h.Damage);
                return new BossAbilityTarget(g.Key.SkillCode, g.Key.TargetId, fight.Name(g.Key.TargetId), g.Count(), damage,
                    damage * 100d / skillTotals[g.Key.SkillCode], g.Max(h => h.Damage));
            }).OrderByDescending(t => t.Damage).ToArray();
            // At most 120 time bins; the final bin uses its actual duration.
            double binWidth = Math.Max(1, Math.Ceiling(seconds / 120)); int count = Math.Max(1, (int)Math.Ceiling(seconds / binWidth));
            var dealtBins = new long[count]; var takenBins = new long[count];
            int Bin(long time) => Math.Clamp((int)((time - start) / 1000d / binWidth), 0, count - 1);
            foreach (var h in dealt) dealtBins[Bin(h.Timestamp)] += h.Damage;
            foreach (var h in taken) takenBins[Bin(h.Timestamp)] += h.Damage;
            var bins = Enumerable.Range(0, count).Select(i => new BossDamageBin(i * binWidth, Math.Min(binWidth, seconds - i * binWidth), dealtBins[i], takenBins[i])).ToArray();
            result.Add(new(fight.Id, id, actor?.NpcId ?? 0, known?.Name ?? fight.Name(id), known == null ? "Location not identified" : catalog.PossibleInstances(known),
                known == null ? "Identified by a captured boss-cast signal" : "Identified by NPC template; locations are catalogue candidates",
                start, end, observedDeaths.Length > 0, abilities, participants, moments.OrderBy(m => m.Timestamp).ThenBy(m => m.Sequence).ToArray(), hp, bins, targets) {
                    OriginalEntityId = actor?.OriginalEntityId ?? 0,
                    AmbiguousNetworkIdentity = reusedIds.Contains(actor?.OriginalEntityId > 0 ? actor.OriginalEntityId : id)
                });
        }
        return result.OrderBy(b => b.Start).ThenBy(b => b.EntityId).ToArray();
    }
}
