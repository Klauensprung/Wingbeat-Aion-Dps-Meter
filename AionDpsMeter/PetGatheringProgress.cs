namespace AionDpsMeter.Local;

internal sealed record PetGatheringRow(int Id, string Name, string Progress, long Gained);
internal sealed class PetGatheringProgress
{
    readonly List<(int Id, long Gained)> recent = new();
    internal long Revision { get; private set; }
    internal void Clear() { recent.Clear(); Revision++; }
    internal void Observe(PetCollectionSnapshot? before, PetCollectionSnapshot? after, string kind)
    {
        if (after == null || ReferenceEquals(before, after) || kind is not ("pet-progress" or "pet-added")) return;
        foreach (var pet in after.Pets) {
            var old = before?.Pets.FirstOrDefault(p => p.PetId == pet.PetId);
            long gain = pet.ObservedGains - (old?.ObservedGains ?? 0);
            bool acquired = kind == "pet-added" && pet.Level > 0 && old?.Level is null or 0;
            if (gain <= 0 && !acquired) continue;
            var previous = recent.FirstOrDefault(p => p.Id == pet.PetId);
            recent.RemoveAll(p => p.Id == pet.PetId);
            recent.Insert(0, (pet.PetId, previous.Gained + Math.Max(0, gain)));
            if (recent.Count > 5) recent.RemoveAt(5);
            Revision++;
        }
    }
    internal PetGatheringRow[] Rows(PetCollectionSnapshot? collection) => recent.Select(entry => {
        var pet = PetCatalogue.Default.Find(entry.Id);
        var state = collection?.Pets.FirstOrDefault(p => p.PetId == entry.Id);
        int goal = Math.Clamp((state?.Level ?? 0) + 1, 1, 3);
        var progress = PetFarming.For(pet, state, goal);
        string held = state?.Souls is int souls ? $"{souls} souls held" : "Soul total unknown";
        string next = state?.Level >= 3 ? "Max level" : progress.SoulsNeeded is int need
            ? need == 0 ? $"Ready for level {goal}" : $"{need} more for {(goal == 1 ? "collection" : $"level {goal}")}"
            : "Re-enter your character to capture totals";
        return new PetGatheringRow(entry.Id, pet?.Name ?? $"Pet {entry.Id}", $"{held} · {next}", entry.Gained);
    }).ToArray();
}
