namespace AionDpsMeter.Local;

internal static class PetFarmingTests
{
    internal static void Run(Action<bool, string> check)
    {
        check(PetCatalogue.Default.Pets.All(p => p.Level2Souls > 0 && p.Level3Souls > 0 && p.DetailSha256?.Length == 64) &&
            PetCatalogue.Default.Find(1002) is { Level2Souls: 25, Level3Souls: 75 },
            "Bundled pet level costs include all 211 detail-page provenance hashes and the checked Krall example");
        var pet = new PetDefinition(1002, "Example", "Cogni", 5, "Example monster", "https://aion2data.com/pets/1002-krall-laborer") { Level2Souls = 25, Level3Souls = 75 };
        check(PetFarming.For(pet, new(1002, 1, 10, 0), 2).SoulsNeeded == 15 &&
            PetFarming.For(pet, new(1002, 1, 10, 0), 3).SoulsNeeded == 90 && PetFarming.For(pet, new(1002, 2, 10, 0), 3).SoulsNeeded == 65,
            "Pet farming sums only remaining level costs and subtracts known souls once");
        check(PetFarming.For(pet, new(1002, 1, null, 20), 1).Complete &&
            PetFarming.For(pet, new(1002, 3, null, 0), 3).SoulsNeeded == 0,
            "Confirmed levels complete earlier phases even when souls are unknown");
        check(PetFarming.For(pet, null, 1).SoulsNeeded == null && !PetFarming.For(pet, null, 1).Complete &&
            PetFarming.For(pet, new(1002, null, 15, 15), 2).SoulsNeeded == null &&
            PetFarming.For(pet, new(1002, 1, null, 15), 2).SoulsNeeded == null,
            "Partial capture and observed gains cannot fabricate ownership or remaining soul counts");
        check(PetFarming.For(pet, new(1002, 1, 25, 0), 2) is { Complete: false, SoulsNeeded: 0, Status: "Ready to level" },
            "Enough souls means ready to level, not a completed level without its confirmation");
        check(PetFarming.For(pet with { Level3Souls = null }, new(1002, 1, 1, 0), 3).SoulsNeeded == null,
            "Missing level costs stay unknown instead of undercounting a farming goal");
        var collection = new PetCollectionSnapshot(1000, 0, true, [new(1002, 2, 10, 0)]);
        check(PetCollectionView.Build(PetCatalogue.Default, collection, 2).Single(p => p.Id == 1002).GoalComplete &&
            !PetCollectionView.Build(PetCatalogue.Default, collection).Single(p => p.Id == 1002).GoalComplete,
            "Phase views mark completed pets for hiding while the full catalogue preserves them");
        var rows = PetCollectionView.Build(PetCatalogue.Default, null);
        var krall = rows.Single(p => p.Id == 1002);
        check(PetCollectionView.MatchesLocation(krall, "Verteron", "Elyos") && !PetCollectionView.MatchesLocation(krall, "Verteron", "Asmodian"),
            "Bundled farming locations preserve the checked Krall region and side facts");
        var multiple = krall with { Locations = [new("Region A", "Elyos"), new("Region B", "Asmodian")] };
        check(!PetCollectionView.MatchesLocation(multiple, "Region A", "Asmodian") && PetCollectionView.MatchesLocation(multiple, null, "Asmodian"),
            "Pet region and side filters must match the same location rather than mixing separate spawn regions");
        var unknown = krall with { Locations = [] };
        check(PetCollectionView.MatchesLocation(unknown, null, null) && !PetCollectionView.MatchesLocation(unknown, null, "Elyos") && unknown.Regions == "Not listed",
            "Unknown farming locations remain in the full list without invented faction or region assignment");
    }
}
