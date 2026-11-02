using System.Collections.Frozen;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class CombatEffectTypes
{
    sealed record Data(int[] Healing, int[] ResourceOnly, int[] PeriodicHealing, int[] PeriodicExcluded, int[] Barrier);
    static readonly Data data = Read();
    static readonly FrozenSet<int> healing = data.Healing.ToFrozenSet();
    static readonly FrozenSet<int> resourceOnly = data.ResourceOnly.ToFrozenSet();
    static readonly FrozenSet<int> periodicHealing = data.PeriodicHealing.ToFrozenSet();
    static readonly FrozenSet<int> periodicExcluded = data.PeriodicExcluded.ToFrozenSet();
    static readonly FrozenSet<int> barrier = data.Barrier.ToFrozenSet();
    static Data Read() {
        using var stream = typeof(CombatEffectTypes).Assembly.GetManifestResourceStream("Wingbeat.CombatEffects.json")
            ?? throw new InvalidOperationException("Combat effect metadata is missing.");
        return JsonSerializer.Deserialize<Data>(stream) ?? throw new InvalidOperationException("Combat effect metadata is empty.");
    }
    internal static bool Healing(int id) => healing.Contains(id);
    internal static bool ResourceOnly(int id) => resourceOnly.Contains(id);
    internal static bool PeriodicHealing(int id) => periodicHealing.Contains(id);
    internal static bool PeriodicExcluded(int id) => periodicExcluded.Contains(id);
    internal static bool Barrier(int id) => barrier.Contains(id);
}
