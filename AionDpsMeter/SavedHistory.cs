namespace AionDpsMeter.Local;

internal static class SavedHistory
{
    internal static int LootPickups(Fight fight) => fight.ArchiveSummary?.LootPickups ?? (fight.SavedLoot != null ?
        (int)Math.Min(int.MaxValue, fight.SavedLoot.Rows.Sum(r => (long)r.Pickups)) : fight.LiveLoot?.PickupCount ?? fight.Network.Count(n => n.Kind == "loot-pickup"));
    internal static bool HasLocalHealing(Fight fight) => fight.ArchiveSummary?.HasLocalHealing ??
        (fight.LocalPlayer > 0 && fight.Healing.Any(h => h.ActorId == fight.LocalPlayer && h.Damage > 0));
    internal static bool Includes(Fight fight) => fight.OmittedGathers > 0 || fight.GatheredObjects.Count > 0 || fight.DungeonRun?.HasProgress == true || fight.Training != null || EncounterOverview.HasLocalDamage(fight) || HasLocalHealing(fight) || LootPickups(fight) > 0;
    internal static string Activity(Fight fight) => fight.Training != null ? "Training" :
        !fight.HasEvents && fight.DungeonRun?.HasProgress == true ? fight.DungeonRun.ClearedAt != null ? "Dungeon completion" : "Dungeon progress" :
        (fight.GatheredObjects.Count > 0 || fight.OmittedGathers > 0) ? fight.HasEvents ? "Combat + gathering" : LootPickups(fight) > 0 ? "Loot + gathering" : "Gathering" :
        LootPickups(fight) > 0 ? (fight.HasEvents ? "Combat + loot" : "Loot") : HasLocalHealing(fight) && !EncounterOverview.HasLocalDamage(fight) ? "Healing" : "Combat";
}
