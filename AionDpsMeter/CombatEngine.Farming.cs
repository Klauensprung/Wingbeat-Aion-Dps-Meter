namespace AionDpsMeter.Local;

public sealed partial class CombatEngine
{
    internal void ResetFarmingTracking(long at)
    {
        // Serialize with ingestion so every category starts at the same boundary.
        // Current inventory, engagement and pet state remain valid after reset.
        lock (Gate) {
            KillTracking.Reset(at); GatheringTracking.Reset(at); LootTracking.Reset(at);
            SoulTracking.Reset(at); PetRegistrationTracking.Reset(at); PetLevelTracking.Reset(at);
        }
    }
}
