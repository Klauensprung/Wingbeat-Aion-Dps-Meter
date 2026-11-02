namespace AionDpsMeter.Local;

internal sealed record PetFarmingProgress(bool Complete, int? SoulsNeeded, string Status);
internal static class PetFarming
{
    internal static PetFarmingProgress For(PetDefinition? pet, PetProgressState? state, int goal)
    {
        if (goal is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(goal));
        if (state?.Level is int level && level >= goal)
            return new(true, 0, state.LevelInferred ? "Complete (inferred)" : "Complete");
        if (state?.Level == null) return new(false, null, "Level unknown");
        if (pet == null || state.Souls == null) return new(false, null, "Souls / cost unknown");
        int total = 0;
        for (int next = state.Level.Value + 1; next <= goal; next++) {
            int? cost = next switch { 1 => pet.SummonSouls, 2 => pet.Level2Souls, _ => pet.Level3Souls };
            if (cost == null) return new(false, null, "Cost unknown");
            total = checked(total + cost.Value);
        }
        int needed = Math.Max(0, total - state.Souls.Value);
        return new(false, needed, needed == 0 ? "Ready to level" : "Farm souls");
    }
}
