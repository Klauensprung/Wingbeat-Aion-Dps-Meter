namespace AionDpsMeter.Local;

internal sealed record PetCollectionRow(int Id, string Name, string Family, string SummonSouls, string SoulSource,
    string Level, string Souls, string Gains, string Active, bool Captured)
{
    public string LevelSource { get; init; } = "Unknown";
    public string Needed { get; init; } = "";
    public string PhaseStatus { get; init; } = "";
    public bool GoalComplete { get; init; }
    public PetLocation[] Locations { get; init; } = [];
    public string Regions => Locations.Length == 0 ? "Not listed" : string.Join(", ", Locations.Select(l => l.Region).Distinct());
    public string Factions => Locations.Length == 0 ? "Unknown" : string.Join(", ", Locations.Select(l => l.Faction).Distinct());
}

internal static class PetCollectionView
{
    internal static bool MatchesLocation(PetCollectionRow row, string? region, string? faction) =>
        region == null && faction == null || row.Locations.Any(l => (region == null || l.Region == region) &&
            (faction == null || l.Faction == faction || l.Faction == "Both"));
    internal static PetCollectionRow[] Build(PetCatalogue catalog, PetCollectionSnapshot? snapshot, int goal = 0)
    {
        var captured = snapshot?.Pets.ToDictionary(p => p.PetId) ?? new();
        var definitions = catalog.Pets.ToDictionary(p => p.Id);
        return definitions.Keys.Union(captured.Keys).Union(snapshot?.ActivePetId > 0 ? new[] { snapshot.ActivePetId } : []).Select(id => {
            definitions.TryGetValue(id, out var pet);
            captured.TryGetValue(id, out var state);
            state = PetCollection.KnownState(state, snapshot?.HasRoster ?? false, id);
            var progress = goal == 0 ? null : PetFarming.For(pet, state, goal);
            return new PetCollectionRow(id, pet?.Name ?? $"Pet {id}", pet?.Family ?? "Unknown",
                pet?.SummonSouls.ToString() ?? "?", pet?.SoulSource ?? "Not listed",
                state?.Level?.ToString() ?? "?", state?.Souls?.ToString("N0") ?? "?",
                state == null ? "?" : state.ObservedGains.ToString("N0"), snapshot?.ActivePetId == id ? "Active" : "",
                state != null || snapshot?.ActivePetId == id) {
                    LevelSource = state?.Level == null ? "Unknown" : state.LevelInferred ? "Inferred" : "Captured",
                    Needed = progress == null ? "" : progress.SoulsNeeded?.ToString("N0") ?? "?",
                    PhaseStatus = progress?.Status ?? "", GoalComplete = progress?.Complete ?? false, Locations = pet?.Locations ?? []
                };
        }).OrderBy(p => p.Name).ToArray();
    }
}
