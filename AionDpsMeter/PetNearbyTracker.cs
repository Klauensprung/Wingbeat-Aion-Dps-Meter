namespace AionDpsMeter.Local;

internal sealed class PetNearbyTracker
{
    CapturedPosition? previousPosition;
    PetCollectionSnapshot? previousCollection;
    int previousGoal = -1;
    NearbyPet[] rows = [];
    internal IReadOnlyList<NearbyPet> Update(CapturedPosition? position, PetCollectionSnapshot? collection, int goal)
    {
        goal = Math.Clamp(goal, 1, 3);
        if (position == previousPosition && ReferenceEquals(collection, previousCollection) && goal == previousGoal) return rows;
        previousPosition = position; previousCollection = collection; previousGoal = goal;
        rows = PetNearby.Build(PetCatalogue.Default.Pets, collection, position, goal, false, PetSpawns.Default.ForPet).Take(5).ToArray();
        return rows;
    }
}
