using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class EquippedGearTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var detail = new GearDetails(1, 2, 3, 100, [], [new(19, -5)], [], [new(17010000, 2)], [], []);
        InventoryStack Item(long instance, byte container = 11, byte modification = 1) => new(instance, 110120003, 1, container, modification) { Equipment = container == 11 ? detail : null };
        NetworkEvent Batch(long at, bool full, params InventoryStack[] items) => new(full ? "inventory" : "inventory-update", at, "test", JsonSerializer.SerializeToElement(new InventoryBatch(full, items, 528)));
        var first = EquippedGear.Apply(null, Batch(1, true, Item(100), Item(200)))!;
        var moved = EquippedGear.Apply(first, Batch(2, false, Item(100, 1)))!;
        var removed = EquippedGear.Apply(moved, Batch(3, false, Item(200, 11, 2)))!;
        check(first is { Complete: true, Items.Length: 2 } && moved.Items.Single().InstanceId == 200 && removed is { Complete: true, Items.Length: 0 },
            "Gear tracks instance IDs and removes moved/unequipped and deleted items without mutating earlier snapshots");
        check(ReferenceEquals(first, EquippedGear.Apply(first, Batch(0, true))) && EquippedGear.Apply(null, Batch(1, false, Item(100))) is { Complete: false },
            "Stale gear snapshots cannot roll back and delta-only capture remains partial");
        check(EquippedGear.Apply(first, Batch(2, false, Item(100) with { Equipment = null })) is { Complete: false, Items.Length: 1 } &&
            EquippedGear.Apply(first, Batch(2, true)) is { Complete: true, Items.Length: 0 },
            "Missing equipped details invalidate completeness; an authoritative empty inventory clears gear");
        var engine = new CombatEngine();
        void Prepare(long time) => engine.Apply(new("network", Network: new("local-zone", time, "test", JsonSerializer.SerializeToElement(new { MapId = 1010, AwaitingIdentity = true }))));
        engine.Apply(new("network", Network: Batch(1, true, Item(100)))); Prepare(2);
        engine.Apply(new("local-player", Actor: new(1, "First", true, false, 0, "Cleric", 1)));
        var saved = engine.Snapshot(); Prepare(3); engine.Apply(new("network", Network: Batch(4, true, Item(200)))); Prepare(3);
        check(engine.Fight.Actors[1].Gear!.Items.Single().InstanceId == 100, "Pending gear inventory cannot overwrite the previous character");
        engine.Apply(new("local-player", Actor: new(2, "Second", true, false, 0, "Assassin", 1)));
        check(engine.Fight.Actors[2].Gear!.Items.Single().InstanceId == 200 && saved.Actors[1].Gear!.Items.Single().InstanceId == 100 && engine.Fight.Network.Count == 0,
            "Initial and pending gear baselines bind to confirmed identity with duplicate preparation ignored and raw inventory excluded");
        var store = new FightStore(Path.Combine(temp, "equipped-gear")); store.Save(saved);
        check(store.Load(Path.Combine(store.Root, saved.Id + ".json")).Actors[1].Gear!.Items.Single().Details.Stats.Single().Value == -5 &&
            FightArchiveSummary.Create(saved).Actors[1].Gear == null,
            "Full fight files preserve gear details while compact archive summaries release them");
        bool invalid = false;
        try { EquippedGear.Apply(first, Batch(2, false, Item(300) with { Equipment = detail with { ItemLevel = -1 } })); } catch (InvalidDataException) { invalid = true; }
        check(invalid && first.Items.Length == 2, "Malformed gear details cannot mutate a valid prior snapshot");
    }
}
