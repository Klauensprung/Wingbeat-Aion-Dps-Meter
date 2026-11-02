using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

public sealed record PetProgressState(int PetId, int? Level, int? Souls, long ObservedGains)
{
    public bool LevelInferred { get; init; }
    public int PendingSoulSpend { get; init; }
    public int AcquisitionSoulCredit { get; init; }
}
public sealed record PetCollectionSnapshot(long Timestamp, int ActivePetId, bool HasRoster, PetProgressState[] Pets)
{
    public PetGenus[]? Genera { get; init; }
}
internal static class PetCollection
{
    internal static PetProgressState? KnownState(PetProgressState? state, bool hasRoster, int id) =>
        state ?? (hasRoster ? new(id, 0, 0, 0) : null);
    internal static void Validate(PetCollectionSnapshot snapshot)
    {
        if (snapshot.Genera is { } genera && (genera.Length > 256 || genera.Any(g => g == null || g.Level < 0 || g.Effects == null || g.Effects.Length > 256 || g.Effects.Any(e => e == null) || g.Effects.Select(e => e.Slot).Distinct().Count() != g.Effects.Length) || genera.Select(g => g.Type).Distinct().Count() != genera.Length))
            throw new InvalidDataException("Invalid pet genus snapshot.");
        if (snapshot.Timestamp < 0 || (snapshot.ActivePetId != 0 && snapshot.ActivePetId is < 1000 or > 99999) || snapshot.Pets == null ||
            snapshot.Pets.Length > 2000 || snapshot.Pets.Any(p => p == null || p.PetId is < 1000 or > 99999 || p.Level is < 0 or > 3 ||
                p.PendingSoulSpend is < 0 or > 106 || p.AcquisitionSoulCredit is < 0 or > 5 || p.LevelInferred && p.Level is not (>= 1 and <= 3) ||
                p.Souls is < 0 or > 1000000 || p.ObservedGains is < 0 or > 1000000000) || snapshot.Pets.Select(p => p.PetId).Distinct().Count() != snapshot.Pets.Length)
            throw new InvalidDataException("Invalid captured pet collection.");
    }
    internal static PetCollectionSnapshot? Apply(PetCollectionSnapshot? previous, NetworkEvent e, int localPlayer = 0)
    {
        if (e.Kind is not ("pet-roster" or "pet-progress" or "pet-progress-decrease" or "pet-level" or "pet-added")) return previous;
        if (e.Timestamp < 0 || previous != null && e.Timestamp < previous.Timestamp) return previous;
        PetCollectionSnapshot next;
        if (e.Kind == "pet-roster") {
            var roster = e.Fields.Deserialize<CapturedPetRoster>() ?? throw new InvalidDataException("Missing pet roster.");
            if (roster.Pets == null || roster.Progress == null || roster.Pets.Length > 2000 || roster.Progress.Length > 2000 ||
                roster.Pets.Any(p => p == null || p.Level is < 1 or > 3) || roster.Progress.Any(p => p == null || p.Souls is < 0 or > 1000000) ||
                roster.Pets.Select(p => p.PetId).Distinct().Count() != roster.Pets.Length || roster.Progress.Select(p => p.PetId).Distinct().Count() != roster.Progress.Length)
                throw new InvalidDataException("Invalid pet roster entries.");
            var levels = roster.Pets.ToDictionary(p => p.PetId, p => p.Level);
            var souls = roster.Progress.ToDictionary(p => p.PetId, p => p.Souls);
            next = new(e.Timestamp, roster.ActivePetId, true, levels.Keys.Union(souls.Keys).Order().Select(id =>
                new PetProgressState(id, levels.TryGetValue(id, out int level) ? level : 0, souls.TryGetValue(id, out int amount) ? amount : 0, 0)).ToArray()) { Genera = roster.Genera };
        } else {
            var pets = previous?.Pets.ToDictionary(p => p.PetId) ?? new();
            var genera = previous?.Genera;
            if (e.Kind is "pet-level" or "pet-added") {
                var update = e.Fields.Deserialize<CapturedPetLevel>() ?? throw new InvalidDataException("Missing pet level.");
                if (update.EntityId > 0 && update.EntityId != localPlayer) return previous;
                if (update.Level is < 1 or > 3) throw new InvalidDataException("Invalid pet level.");
                if (update.Genus is { Level: < 0 }) throw new InvalidDataException("Invalid pet genus level.");
                if (e.Kind == "pet-added" && update.Genus is { } genus && genera?.Any(g => g.Type == genus.Type && g.Level != genus.Level) == true)
                    genera = genera.Select(g => g.Type == genus.Type ? g with { Level = genus.Level } : g).ToArray();
                pets.TryGetValue(update.PetId, out var old);
                old = KnownState(old, previous?.HasRoster ?? false, update.PetId);
                PetProgressState updated;
                if (e.Kind == "pet-added") {
                    updated = old ?? new(update.PetId, null, null, 0);
                    if (updated.Level is null or 0) updated = updated with { AcquisitionSoulCredit = 5 };
                    if (update.Level >= (updated.Level ?? 0)) updated = updated with { Level = update.Level, LevelInferred = false };
                    updated = ApplyPendingSpend(updated);
                    if (updated == old && ReferenceEquals(genera, previous?.Genera)) return previous;
                } else {
                    // The legacy level-only layout does not establish the balance
                    // or acquisition/spending ordering; discard transient inference.
                    updated = new(update.PetId, update.Level, old?.Level == update.Level ? old?.Souls : null, old?.ObservedGains ?? 0);
                }
                pets[update.PetId] = updated;
            } else {
                var gains = e.Fields.GetProperty("Gains").Deserialize<CapturedPetProgress[]>() ?? throw new InvalidDataException("Missing pet gains.");
                if (gains.Length > 2000 || gains.Any(p => p == null || p.Souls is < 0 or > 1000000) || gains.Select(p => p.PetId).Distinct().Count() != gains.Length)
                    throw new InvalidDataException("Invalid pet gain entries.");
                foreach (var gain in gains) {
                    pets.TryGetValue(gain.PetId, out var old);
                    old = KnownState(old, previous?.HasRoster ?? false, gain.PetId);
                    bool decrease = e.Kind == "pet-progress-decrease";
                    int? remaining = old?.Souls is int souls ? decrease ? souls >= gain.Souls ? souls - gain.Souls : null : checked(souls + gain.Souls) : null;
                    var updated = (old ?? new(gain.PetId, null, null, 0)) with {
                        Souls = remaining, ObservedGains = checked((old?.ObservedGains ?? 0) + (decrease ? 0 : gain.Souls)) };
                    pets[gain.PetId] = decrease ? Spend(updated, gain.Souls) : updated;
                }
            }
            next = new(e.Timestamp, previous?.ActivePetId ?? 0, previous?.HasRoster ?? false, pets.Values.OrderBy(p => p.PetId).ToArray()) { Genera = genera };
        }
        Validate(next); return next;
    }
    // Verified installed reference costs. Inference requires an exact sum, never
    // merely enough souls. Pending spending above every possible sum (105) can
    // never match, so 106 represents any larger value without growing state.
    static int LevelsFor(int level, int spent)
    {
        ReadOnlySpan<int> costs = [5, 25, 75]; int sum = 0;
        for (int i = level; i < costs.Length; i++) {
            sum += costs[i];
            if (sum == spent) return i + 1 - level;
            if (sum > spent) break;
        }
        return 0;
    }
    static PetProgressState ApplyPendingSpend(PetProgressState state)
    {
        int levels = state.PendingSoulSpend > 0 ? LevelsFor(state.Level ?? 0, state.PendingSoulSpend) : 0;
        return levels == 0 ? state : state with { Level = (state.Level ?? 0) + levels, LevelInferred = true, PendingSoulSpend = 0 };
    }
    static PetProgressState Spend(PetProgressState state, int amount)
    {
        int credited = Math.Min(amount, state.AcquisitionSoulCredit);
        state = state with { AcquisitionSoulCredit = state.AcquisitionSoulCredit - credited };
        int remainder = amount - credited;
        if (remainder > 0) {
            int levels = LevelsFor(state.Level ?? 0, remainder);
            state = levels > 0 ? state with { Level = (state.Level ?? 0) + levels, LevelInferred = true } :
                state with { PendingSoulSpend = (int)Math.Min(106L, (long)state.PendingSoulSpend + remainder) };
        }
        return ApplyPendingSpend(state);
    }

}
