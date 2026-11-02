namespace AionDpsMeter.Local;

internal sealed record TrainingSkillDifference(int SkillCode, string Skill, long DamageA, long DamageB,
    double DpsA, double DpsB, double DpsChange, double? ChangePercent, int HitsA, int HitsB, double CritA, double CritB);
internal sealed record TrainingComparisonResult(TrainingSkillDifference[] Skills, long DamageA, long DamageB,
    double SecondsA, double SecondsB, string CharacterA, string CharacterB)
{
    internal double DpsA => DamageA / SecondsA;
    internal double DpsB => DamageB / SecondsB;
}

internal static class TrainingComparison
{
    internal static TrainingComparisonResult Build(Fight a, Fight b, Func<int, string> skillName)
    {
        if (a.Training == null || b.Training == null || a.ArchiveSummary != null || b.ArchiveSummary != null)
            throw new ArgumentException("Select two fully loaded training attempts.");
        var first = PersonalSkills.Build(a, false).ToDictionary(s => s.Code);
        var second = PersonalSkills.Build(b, false).ToDictionary(s => s.Code);
        var rows = first.Keys.Union(second.Keys).Select(code => {
            first.TryGetValue(code, out var x); second.TryGetValue(code, out var y);
            double before = x?.Rate ?? 0, after = y?.Rate ?? 0;
            return new TrainingSkillDifference(code, skillName(code), x?.Amount ?? 0, y?.Amount ?? 0,
                before, after, after - before, before == 0 ? null : 100 * (after - before) / before,
                x?.Hits ?? 0, y?.Hits ?? 0, x?.Crit ?? 0, y?.Crit ?? 0);
        }).OrderByDescending(r => Math.Abs(r.DpsChange)).ThenBy(r => r.SkillCode).ToArray();
        return new(rows, first.Values.Sum(s => s.Amount), second.Values.Sum(s => s.Amount), a.Seconds, b.Seconds,
            a.Name(a.LocalPlayer), b.Name(b.LocalPlayer));
    }
}
