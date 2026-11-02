namespace AionDpsMeter.Local;

internal sealed record NearbyPet(int Id, string Name, string Family, int? SoulsNeeded, string Status,
    string Map, double Distance, PetSpawnPoint Point)
{
    public string Needed => SoulsNeeded?.ToString("N0") ?? "?";
    public string DistanceText => Distance.ToString("N0");
    public string Coordinates => $"{Point.X:0}, {Point.Y:0}, {Point.Z:0}";
}

internal static class PetNearby
{
    // Coordinates are world units. Do not label them metres or imply a traversable route.
    internal static NearbyPet[] Build(IEnumerable<PetDefinition> pets, PetCollectionSnapshot? collection,
        CapturedPosition? position, int goal, bool includeUnknown,
        Func<int, IReadOnlyList<PetSpawnSite>> sites)
    {
        if (goal is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(goal));
        if (position == null || LocalPositionTracker.MapKey(position.MapId) is not { } mapKey) return [];
        LocalPositionTracker.Validate(position);
        var states = collection?.Pets.ToDictionary(p => p.PetId) ?? new();
        var rows = new List<NearbyPet>();
        foreach (var pet in pets) {
            states.TryGetValue(pet.Id, out var state);
            state = PetCollection.KnownState(state, collection?.HasRoster ?? false, pet.Id);
            var progress = PetFarming.For(pet, state, goal);
            if (progress.Complete || progress.SoulsNeeded == 0 || (!includeUnknown && progress.SoulsNeeded == null)) continue;
            PetSpawnPoint? nearest = null; double distance = double.PositiveInfinity; string map = "";
            foreach (var site in sites(pet.Id).Where(s => s.MapKey == mapKey))
                foreach (var point in site.Spawns) {
                    double dx = point.X - position.X, dy = point.Y - position.Y;
                    double squared = dx * dx + dy * dy;
                    if (squared < distance) { nearest = point; distance = squared; map = site.Map; }
                }
            if (nearest != null) rows.Add(new(pet.Id, pet.Name, pet.Family, progress.SoulsNeeded,
                progress.Status, map, Math.Sqrt(distance), nearest));
        }
        return rows.OrderBy(r => r.Distance).ThenBy(r => r.Id).Take(30).ToArray();
    }
}
