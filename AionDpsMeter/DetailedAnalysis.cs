namespace AionDpsMeter.Local;

public sealed record HealingRow(string Healer, string Skill, int SkillCode, string Target, int Ticks, long Healing, double HPS, double Average, int Largest, double HealerShare);
public sealed record OpenerRow(int ActorId, string Character, int Order, double Seconds, string Event, string Skill, int SkillCode, string Target, int Amount, bool Critical);
public sealed record SpecRow(string Character, string Skill, int SkillCode, int? Level, string Choices, string Loadout, string Source, int RecordedEvents);
public sealed record EquipmentRow(string Character, string Slot, string Item, string Source);

public static class DetailedAnalysis
{
    public static int Family(int skill) => skill is >= 11000000 and < 20000000 ? skill / 10000 * 10000 : skill;
    public static HealingRow[] Healing(Fight fight, Func<int, string> name)
    {
        double seconds = fight.Seconds;
        var amounts = fight.Healing.GroupBy(e => e.ActorId).ToDictionary(g => g.Key, g => g.Sum(e => (long)e.Damage));
        return fight.Healing.GroupBy(e => (e.ActorId, e.SkillCode, e.TargetId)).Select(g => {
            long total = g.Sum(e => (long)e.Damage);
            return new HealingRow(fight.Name(g.Key.ActorId), name(g.Key.SkillCode), g.Key.SkillCode, fight.Name(g.Key.TargetId), g.Count(), total,
                Math.Round(total / seconds, 1), Math.Round(total / (double)g.Count(), 1), g.Max(e => e.Damage),
                amounts[g.Key.ActorId] == 0 ? 0 : Math.Round(100d * total / amounts[g.Key.ActorId], 1));
        }).OrderByDescending(r => r.Healing).ToArray();
    }
    public static OpenerRow[] Openers(Fight fight, Func<int, string> name, double windowSeconds = 15)
        => fight.Damage.Select(e => (Hit: e, Healing: false)).Concat(fight.Healing.Select(e => (Hit: e, Healing: true)))
            .OrderBy(e => e.Hit.Timestamp).GroupBy(e => e.Hit.ActorId).SelectMany(g => {
                long first = g.First().Hit.Timestamp;
                return g.Where(e => e.Hit.Timestamp - first < windowSeconds * 1000).Select((e, index) =>
                    new OpenerRow(g.Key, fight.Name(g.Key), index + 1, (e.Hit.Timestamp - first) / 1000d,
                        e.Healing ? "Heal" : "Damage", name(e.Hit.SkillCode), e.Hit.SkillCode, fight.Name(e.Hit.TargetId), e.Hit.Damage, e.Hit.IsCritical));
            }).OrderBy(r => r.Character).ThenBy(r => r.Order).ToArray();
    public static SpecRow[] Specs(Fight fight, Func<int, string> name)
    {
        var events = fight.Damage.Concat(fight.Healing).ToLookup(e => e.ActorId);
        return fight.Actors.Values.Where(a => a.Player).SelectMany(actor => {
            var build = FightMetadata.BuildFor(fight, actor.Id);
            var configured = build?.Skills.ToDictionary(s => s.Skill) ?? new();
            var counts = events[actor.Id].GroupBy(e => Family(e.SkillCode)).ToDictionary(g => g.Key, g => g.Count());
            var configuredFamilies = configured.Keys.Select(Family).ToHashSet();
            var codes = configured.Keys.Concat(counts.Keys.Where(code => !configuredFamilies.Contains(code))).Distinct().Order().ToArray();
            if (codes.Length == 0) return new[] { new SpecRow(actor.Name, "No loadout supplied", 0, null, "Unknown", "Not captured", "Not captured", 0) };
            return codes.Select(code => {
                var spec = configured.GetValueOrDefault(code);
                return new SpecRow(actor.Name, name(code), code, spec?.Level, spec == null ? "Unknown" : string.Join(", ", spec.Choices),
                    spec != null && build?.Complete == true ? "Complete (supplied)" : "Partial / unknown", spec == null ? "Observed skill only" : build!.Source,
                    counts.GetValueOrDefault(Family(code)));
            }).ToArray();
        }).ToArray();
    }
    public static EquipmentRow[] Equipment(Fight fight) => fight.Builds.Values.SelectMany(build => build.Equipment.Select(e =>
        new EquipmentRow(fight.Name(build.ActorId), e.Key, e.Value, build.Source))).ToArray();
}
