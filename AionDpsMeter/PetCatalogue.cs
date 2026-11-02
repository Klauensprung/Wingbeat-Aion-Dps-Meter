using System.Collections.Frozen;
using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal sealed record PetLocation(string Region, string Faction);
internal sealed record PetDefinition(int Id, string Name, string Family, int SummonSouls, string SoulSource, string Source)
{
    public int? Level2Souls { get; init; }
    public int? Level3Souls { get; init; }
    public string? DetailSha256 { get; init; }
    public PetLocation[] Locations { get; init; } = [];
}
internal sealed record PetCatalogueData(int Version, string GameVersion, string RetrievedUtc, string Source, string SourceSha256, PetDefinition[] Pets);
internal sealed class PetCatalogue
{
    internal static PetCatalogue Default { get; } = new();
    readonly FrozenDictionary<int, PetDefinition> byId;
    internal PetDefinition[] Pets { get; } = [];
    internal string GameVersion { get; } = "Unavailable";
    internal string? LoadError { get; }
    internal PetCatalogue()
    {
        try {
            using var stream = typeof(PetCatalogue).Assembly.GetManifestResourceStream("Wingbeat.Pets.json") ?? throw new InvalidDataException("Bundled pet catalogue is missing.");
            var data = JsonSerializer.Deserialize<PetCatalogueData>(stream) ?? throw new InvalidDataException("Pet catalogue is empty.");
            Validate(data); Pets = data.Pets; GameVersion = data.GameVersion; byId = Pets.ToFrozenDictionary(p => p.Id);
        } catch (Exception ex) when (ex is IOException or JsonException or ArgumentException) {
            byId = new Dictionary<int, PetDefinition>().ToFrozenDictionary(); LoadError = "Pet catalogue unavailable: " + ex.Message;
        }
    }
    internal PetDefinition? Find(int id) => byId.GetValueOrDefault(id);
    internal static void Validate(PetCatalogueData data)
    {
        if (data.Version != 1 || string.IsNullOrWhiteSpace(data.GameVersion) || data.Source != "https://aion2data.com/pets" ||
            data.SourceSha256?.Length != 64 || !data.SourceSha256.All(Uri.IsHexDigit) || data.Pets == null || data.Pets.Length is 0 or > 10000 ||
            data.Pets.Any(p => p == null || p.Id <= 0 || string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrWhiteSpace(p.Family) || p.SummonSouls is < 1 or > 100000 ||
                p.SoulSource == null || p.Source == null || !p.Source.StartsWith($"https://aion2data.com/pets/{p.Id}-", StringComparison.Ordinal)) ||
            data.Pets.Select(p => p.Id).Distinct().Count() != data.Pets.Length)
            throw new InvalidDataException("Invalid pet catalogue metadata or entries.");
        if (data.Pets.Any(p => p.Level2Souls is < 1 or > 100000 || p.Level3Souls is < 1 or > 100000 ||
            ((p.Level2Souls != null || p.Level3Souls != null) && (p.DetailSha256?.Length != 64 || !p.DetailSha256.All(Uri.IsHexDigit)))))
            throw new InvalidDataException("Invalid pet level costs or detail provenance.");
        if (data.Pets.Any(p => p.Locations == null || p.Locations.Length > 30 || p.Locations.Any(l => l == null ||
            string.IsNullOrWhiteSpace(l.Region) || l.Region.Length > 120 || l.Faction is not ("Elyos" or "Asmodian" or "Abyss" or "Both" or "Unknown")) ||
            p.Locations.Distinct().Count() != p.Locations.Length))
            throw new InvalidDataException("Invalid pet farming locations.");
    }
}
