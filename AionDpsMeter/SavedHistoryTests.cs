using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class SavedHistoryTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var loot = new Fight { LocalPlayer = 1, Label = "Synthetic loot outing" };
        loot.Actors[1] = new(1, "Player 1", true, false, 0, "Cleric", 1) { CharacterId = 30 };
        loot.Network.Add(new("loot-pickup", 1000, "synthetic", JsonSerializer.SerializeToElement(new LootPickup(1000, 1, 500000001, 3, 30, 1, 1010))));
        var compact = FightArchiveSummary.Create(loot);
        check(SavedHistory.Includes(loot) && SavedHistory.Includes(compact) && SavedHistory.Activity(compact) == "Loot" &&
            SavedHistory.LootPickups(compact) == 1 && compact.Network.Count == 0,
            "Loot-only history remains discoverable through compact metadata without retaining raw network events");
        var store = new FightStore(Path.Combine(temp, "saved-loot-history")); store.Save(loot);
        var loaded = store.Load(Path.Combine(store.Root, loot.Id + ".json"));
        check(SavedHistory.Includes(FightArchiveSummary.Create(loaded)) && LootLedger.FromFight(loaded).Rows().Single() is { Quantity: 3, MapId: 1010 },
            "Loot-only saved files reopen with quantities and maps intact after archive rebuilding");
        var healing = new Fight { LocalPlayer = 1 };
        healing.Actors[1] = loot.Actors[1];
        healing.Healing.Add(new() { ActorId = 1, TargetId = 2, Timestamp = 1000, Damage = 500 });
        check(SavedHistory.Includes(FightArchiveSummary.Create(healing)) && SavedHistory.Activity(healing) == "Healing",
            "Local healing-only encounters are visible in saved history without requiring outgoing damage");
        var training = new Fight { LocalPlayer = 1, Training = new(1000, 31000, 30, "", "No hits") };
        training.Actors[1] = loot.Actors[1];
        check(training.HasRecordedData && SavedHistory.Includes(FightArchiveSummary.Create(training)) && SavedHistory.Activity(training) == "Training",
            "Zero-damage training attempts remain recorded and discoverable for comparison");
        var idle = new Fight { LocalPlayer = 1 }; idle.Actors[1] = loot.Actors[1];
        idle.Network.Add(new("ground-drops", 1000, "synthetic", JsonSerializer.SerializeToElement(new { Drops = Array.Empty<object>() })));
        check(!SavedHistory.Includes(idle) && !SavedHistory.Includes(FightArchiveSummary.Create(idle)),
            "Unattributed drop observations and idle identity records do not flood the saved activity list");
    }
}
