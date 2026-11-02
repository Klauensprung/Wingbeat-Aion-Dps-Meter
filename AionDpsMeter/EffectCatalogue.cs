using System.Collections.Frozen;
using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal sealed record EffectDefinition(int Id, string Name, string Kind);
internal sealed record EffectCatalogueData(string Source, string NamesSha256, string TypesSha256, EffectDefinition[] Rows);
internal sealed class EffectCatalogue
{
    internal static readonly EffectCatalogue Default = new();
    readonly FrozenDictionary<int, EffectDefinition> byId;
    internal int Count => byId.Count;
    internal string? LoadError { get; }
    internal EffectDefinition? Find(int id) => byId.GetValueOrDefault(id);
    internal EffectCatalogue()
    {
        try {
            using var stream = typeof(EffectCatalogue).Assembly.GetManifestResourceStream("Wingbeat.Effects.json") ?? throw new InvalidDataException("Effect catalogue missing.");
            var data = JsonSerializer.Deserialize<EffectCatalogueData>(stream) ?? throw new InvalidDataException("Empty effect catalogue.");
            Validate(data); byId = data.Rows.ToFrozenDictionary(r => r.Id);
        } catch (Exception ex) when (ex is IOException or JsonException or ArgumentException) {
            byId = new Dictionary<int, EffectDefinition>().ToFrozenDictionary(); LoadError = "Effect names unavailable.";
        }
    }
    internal static void Validate(EffectCatalogueData data)
    {
        bool Hash(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);
        if (!Hash(data.NamesSha256) || !Hash(data.TypesSha256) || data.Rows == null || data.Rows.Length is 0 or > 100000 ||
            data.Rows.Any(r => r == null || r.Id is <= 0 or > 1000000000 || string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 500 ||
                r.Kind is not ("Buff" or "Debuff" or "Passive" or "Unknown")) || data.Rows.Select(r => r.Id).Distinct().Count() != data.Rows.Length)
            throw new InvalidDataException("Invalid effect catalogue.");
    }
    internal static string? ControlName(EffectDefinition? effect) => effect?.Kind == "Debuff" &&
        effect.Name is "Stun" or "Sleep" or "Fear" or "Knockdown" or "Paralysis" ? effect.Name : null;
}
