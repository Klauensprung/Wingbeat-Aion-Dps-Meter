using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class GroundLootTrackerTests
{
    internal static void Run(Action<bool, string> check)
    {
        var local = new Actor(1, "Player 1", true, false, 0, "Cleric", 1) { CharacterId = 30 };
        NetworkEvent Event(string kind, long time, object fields) => new(kind, time, "synthetic", JsonSerializer.SerializeToElement(fields));
        GroundDrop Drop(uint id = 100) => new(10, 20, id, 500000001, 3, 30, 1, 120000);
        NetworkEvent Spawn(long time = 1000, GroundDrop? drop = null) => Event("ground-drops", time, new GroundDropBatch([drop ?? Drop()], 1));
        NetworkEvent Gone(long time = 2000, bool picked = true, uint owner = 30, ushort server = 1, uint id = 100, uint source = 10) =>
            Event("ground-removals", time, new GroundRemovalBatch([new(source, 20, id, owner, server)], picked));
        var tracker = new GroundLootTracker();
        tracker.Apply(Spawn(), local);
        check(tracker.Apply(Gone(), local) is [{ ItemId: 500000001, Quantity: 3, CharacterId: 30, Server: 1 }],
            "Matched ground pickup attributes the drop's quantity to the known local character and server");
        tracker.Apply(Spawn(3000), local);
        check(tracker.Apply(Gone(4000), local).Length == 0 && tracker.Apply(Gone(), local).Length == 0,
            "Repeated spawn/removal messages cannot recount a consumed drop even with later capture timestamps");
        tracker.Clear(); tracker.Apply(Spawn(), local);
        check(tracker.Apply(Gone(owner: 99), local).Length == 0 && tracker.Apply(Gone(owner: 0), local).Length == 0 &&
            tracker.Apply(Gone(server: 2), local).Length == 0 && tracker.Apply(Gone(source: 11), local).Length == 0,
            "Other owners, unknown ownership, different servers and mismatched source entities never count as local loot");
        tracker.Apply(Gone(picked: false), local);
        check(tracker.Apply(Gone(3000), local).Length == 0, "A disappeared drop cannot later be counted by a duplicate pickup message");
        tracker.Clear(); tracker.Apply(Spawn(), local);
        check(tracker.Apply(Gone(120000), local).Length == 0, "Pickup messages at or after drop expiry are excluded");
        tracker.Clear(); tracker.Apply(Spawn(), local with { CharacterId = 0 });
        check(tracker.Apply(Gone(), local).Length == 0, "Unknown local identity cannot retroactively attribute uncaptured drops");
        tracker.Apply(Spawn(3000), local);
        check(tracker.Apply(Gone(4000), local with { CharacterId = 31 }).Length == 0 && tracker.Count == 0,
            "A character switch clears pending loot before the next observation");
        tracker.Clear(); tracker.Apply(Spawn(), local);
        check(tracker.Apply(Gone(500), local).Length == 0 && tracker.Apply(Gone(), local).Length == 1,
            "An out-of-order removal cannot consume a newer pending drop");
        tracker.Clear();
        for (int i = 0; i < 4200; i++) tracker.Apply(Spawn(1000 + i, Drop((uint)i + 1)), local);
        tracker.Apply(Spawn(), local);
        check(tracker.Count == 4096 && tracker.Apply(Gone(), local).Length == 0,
            "Loot correlation stays bounded and evicted old messages cannot re-enter through replay");
        tracker.Apply(Gone(200000), local);
        check(tracker.Count == 0, "Expired pending drops and tombstones are released on later loot traffic");

        var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: local));
        engine.Apply(new("network", Network: Spawn())); engine.Apply(new("network", Network: Gone()));
        check(engine.Snapshot().Network.All(n => n.Kind != "loot-pickup") && engine.Loot.Rows().Length == 0,
            "Ground disappearance alone no longer creates loot in the live engine");
    }
}
