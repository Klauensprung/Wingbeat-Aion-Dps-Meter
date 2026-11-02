using System.Collections.Frozen;
using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class SkillChargeCapacity
{
    sealed record Rule(int slot, int level, int uses);
    sealed record Catalogue(string SourceSha256, Dictionary<int, Rule[]> Rows);
    static readonly FrozenDictionary<int, Rule[]> rows = Load();
    internal static string? LoadError { get; private set; }
    internal static int Capacity(int skill, int level, int[] choices)
    {
        // Existing exact/tens entries without rules deliberately stop fallback.
        if (!rows.TryGetValue(skill, out var rules) && !rows.TryGetValue(skill / 10 * 10, out rules))
            rows.TryGetValue(skill / 10000 * 10000, out rules);
        int capacity = 1;
        foreach (var rule in rules ?? [])
            if (rule.level <= level && choices.Contains(rule.slot)) capacity = Math.Max(capacity, rule.uses);
        return capacity;
    }
    static FrozenDictionary<int, Rule[]> Load()
    {
        try {
            using var stream = typeof(SkillChargeCapacity).Assembly.GetManifestResourceStream("Wingbeat.SkillCharges.json") ?? throw new InvalidDataException("Missing charge rules.");
            var data = JsonSerializer.Deserialize<Catalogue>(stream) ?? throw new InvalidDataException("Empty charge rules.");
            if (data.SourceSha256 is not { Length: 64 } || !data.SourceSha256.All(Uri.IsHexDigit) || data.Rows is not { Count: > 0 and <= 4096 } ||
                data.Rows.Any(p => p.Key <= 0 || p.Value == null || p.Value.Length > 32 || p.Value.Any(r => r == null || r.slot is < 0 or > 255 || r.level is < 0 or > 255 || r.uses is < 1 or > 255)))
                throw new InvalidDataException("Invalid charge rules.");
            return data.Rows.ToFrozenDictionary();
        } catch (Exception ex) when (ex is IOException or JsonException or ArgumentException) {
            LoadError = "Charge capacity catalogue unavailable.";
            return FrozenDictionary<int, Rule[]>.Empty;
        }
    }
}
