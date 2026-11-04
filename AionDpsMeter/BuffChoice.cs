using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal sealed record BuffChoice(int Id, string Name)
{
    sealed record AliasData(string Source, string SourceSha256, Dictionary<int,string> Names);
    static readonly Lazy<Dictionary<int,string>> aliases = new(() => {
        using var stream = typeof(BuffChoice).Assembly.GetManifestResourceStream("Wingbeat.BuffNameAliases.json")
            ?? throw new InvalidDataException("Missing buff aliases.");
        var data = JsonSerializer.Deserialize<AliasData>(stream);
        if (data?.Names == null || data.Names.Count > 1000 || data.Names.Any(p => p.Key <= 0 || string.IsNullOrWhiteSpace(p.Value) || p.Value.Length > 500))
            throw new InvalidDataException("Invalid buff aliases.");
        return data.Names;
    });
    static readonly Lazy<Dictionary<int, BuffChoice>> choices = new(() => {
        var result = new Dictionary<int, BuffChoice>();
        foreach (var group in EffectCatalogue.Default.Buffs.GroupBy(NameFor, StringComparer.OrdinalIgnoreCase)) {
            // Placeholder labels do not identify a shared buff.
            bool named = !group.Key.Contains("DNT", StringComparison.OrdinalIgnoreCase) && !group.Key.Contains("Unknown", StringComparison.OrdinalIgnoreCase);
            var first = group.MinBy(b => b.Id)!;
            foreach (var buff in group) result[buff.Id] = new(named ? first.Id : buff.Id, group.Key);
        }
        return result;
    });
    static string NameFor(EffectDefinition buff)
    {
        if (aliases.Value.TryGetValue(buff.Id, out var name)) return name;
        // Captured mantra ranks use these placeholder catalogue names. Only
        // map the verified families; unrelated CH SKILL placeholders stay apart.
        if (buff.Id / 1000 == 181600 && buff.Name == "CH SKILL 016") return "Sprint Mantra";
        if (buff.Id / 1000 == 181900 && buff.Name == "CH SKILL 019") return "Undefeated Mantra";
        return buff.Name.Trim();
    }
    public override string ToString() => Name;
    internal static BuffChoice Resolve(int id) => choices.Value.GetValueOrDefault(id) ?? new(id, EffectCatalogue.Default.Find(id)?.Name ?? $"Effect {id}");
    internal static BuffChoice[] Group(IEnumerable<int> ids) => ids.Where(id => id > 0).Select(Resolve).DistinctBy(b => b.Id).ToArray();
    internal static BuffChoice[] Search(IEnumerable<int> active, string query)
    {
        var current = Group(active.Where(id => EffectCatalogue.Default.Find(id)?.Kind != "Debuff")).Select(b => b.Id).ToHashSet();
        return Group(active.Concat(EffectCatalogue.Default.Buffs.Select(b => b.Id)).Where(id =>
            EffectCatalogue.Default.Find(id)?.Kind != "Debuff" &&
            (Resolve(id).Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) || id.ToString().Contains(query.Trim()))))
            .OrderByDescending(b => current.Contains(b.Id)).ThenBy(b => b.Name).Take(150).ToArray();
    }
}
