namespace AionDpsMeter.Local;

internal static class CooldownDurations
{
    // Base values: https://aion2data.com/classes/cleric, client 2.0.6.0 hotfix 1.
    // Checked 2026-10-12. Estimates only; equipment and specializations can differ.
    static readonly Dictionary<int, double> Base = new() {
        [17150000] = 30, [17060000] = 45
    };
    internal static double Seconds(int skill, SkillNames names, IReadOnlyDictionary<int, double>? overrides)
    {
        var choice = CooldownSkillChoice.Resolve(skill, names);
        // Old settings may contain several tiers. The lowest ID (base first) wins
        // consistently until the user sets a new shared duration. Zero disables it.
        int first = int.MaxValue; double seconds = 0;
        if (overrides != null) foreach (var pair in overrides) {
            if (pair.Key >= first || !choice.Matches(CooldownSkillChoice.Resolve(pair.Key, names))) continue;
            first = pair.Key; seconds = pair.Value;
        }
        if (first != int.MaxValue) return seconds;
        return Base.GetValueOrDefault(choice.Id);
    }

    internal static void Set(int skill, double seconds, SkillNames names, Dictionary<int, double> overrides)
    {
        var choice = CooldownSkillChoice.Resolve(skill, names);
        foreach (int id in overrides.Keys.Where(id => choice.Matches(CooldownSkillChoice.Resolve(id, names))).ToArray()) overrides.Remove(id);
        overrides[choice.Id] = seconds;
    }
}
