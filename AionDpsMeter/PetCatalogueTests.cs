using System.IO;

namespace AionDpsMeter.Local;

internal static class PetCatalogueTests
{
    internal static void Run(Action<bool, string> check)
    {
        var catalog = PetCatalogue.Default;
        check(catalog.LoadError == null && catalog.GameVersion == "2026.10.28" && catalog.Pets.Length == 211 &&
            catalog.Pets.Select(p => p.Family).Distinct().Count() == 5,
            "Bundled offline pet catalogue contains the complete imported version and five families");
        check(catalog.Find(1002) is { Name: "Krall Laborer", Family: "Cogni", SummonSouls: 5, SoulSource: "Krall Armed Soldier" } && catalog.Find(-1) == null,
            "Pet entity-ID lookup returns factual family and soul-source data without guessing unknown IDs");
        var sample = new PetCatalogueData(1, "2026.10.28", "2026-10-31", "https://aion2data.com/pets", new string('a', 64), [catalog.Pets[0]]);
        bool Reject(PetCatalogueData data) { try { PetCatalogue.Validate(data); return false; } catch (InvalidDataException) { return true; } }
        check(Reject(sample with { Pets = [catalog.Pets[0], catalog.Pets[0]] }) && Reject(sample with { Pets = [catalog.Pets[0] with { SummonSouls = -1 }] }) &&
            Reject(sample with { SourceSha256 = "missing" }), "Pet catalogue validation rejects duplicate IDs, invalid costs and missing provenance hashes");
    }
}
