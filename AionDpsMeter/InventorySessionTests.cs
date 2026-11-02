using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class InventorySessionTests
{
    internal static void Run(Action<bool, string> check)
    {
        var transfer = new InventoryLootSession();
        var owner = new Actor(1, "Transfer owner", true, false, 0, "Cleric", 2301) { PersistentId = 6000000000 };
        NetworkEvent Stack(long time, long count, bool snapshot) => new(snapshot ? "inventory" : "inventory-update", time, "test",
            JsonSerializer.SerializeToElement(new InventoryBatch(snapshot, [new(100, 110120003, count, 1, 1, 5)])));
        transfer.Bind(owner); transfer.Apply(Stack(1, 50, true), owner);
        transfer.Prepare(2); transfer.Bind(owner with { Id = 2 });
        check(transfer.Apply(Stack(3, 52, false), owner with { Id = 2 }) is [{ Quantity: 2 }],
            "Confirmed same-character map transfer retains stack counts when no new depot snapshot arrives");
        transfer.Login(4); transfer.Prepare(5); transfer.Bind(owner);
        check(transfer.Apply(Stack(6, 100, false), owner).Length == 0,
            "Explicit login never borrows a pre-login inventory baseline even for the same persistent character");
        transfer.Prepare(7); transfer.Apply(Stack(8, 200, true), null); transfer.Bind(owner);
        check(transfer.Apply(Stack(9, 202, false), owner) is [{ Quantity: 2 }],
            "Fresh pending depot counts take precedence over a confirmed transfer's old baseline");
        transfer.Prepare(10); transfer.Bind(owner with { PersistentId = 6000000001 });
        check(transfer.Apply(Stack(11, 300, false), owner with { PersistentId = 6000000001 }).Length == 0,
            "A different persistent character cannot inherit counts even with the same display name and entity ID");
        transfer.Prepare(12); transfer.Prepare(13); transfer.Bind(owner with { PersistentId = 6000000001 });
        check(transfer.Apply(Stack(14, 400, false), owner with { PersistentId = 6000000001 }).Length == 0,
            "Superseded preparation cannot revive a baseline from an uncertain transfer");
        var displays = new InventoryLootSession(); displays.Bind(owner);
        NetworkEvent Message(string kind, long at, object fields) => new(kind, at, "test", JsonSerializer.SerializeToElement(fields));
        displays.Apply(Message("inventory", 1, new InventoryBatch(true,
            [new(100, 110120003, 50, 1), new(200, 930100000, 1000, 1)])), owner);
        displays.Apply(Message("content-tickets", 1, new ContentTicketReading(true,
            [new(1, 2, 0, 0, 0), new(2, 3, 0, 0, 0)])), owner);
        displays.ApplyEnergy(new(800, 100, true, 1), owner);
        displays.Prepare(2);
        check(displays.Holdings().Items.Length == 0 && displays.Currencies().Length == 0 && displays.Tickets().Length == 0,
            "Transfer snapshots stay hidden while ownership is unconfirmed");
        displays.Apply(Stack(3, 52, false), null);
        displays.Apply(Message("content-tickets", 3, new ContentTicketReading(false, [new(1, 9, 0, 0, 0)])), null);
        displays.Bind(owner with { Id = 2 });
        check(displays.Holdings() is { Complete: true } holdings && holdings.Items.Single(i => i.Id == 110120003).Count == 52 &&
            displays.Currencies().Single(c => c.ItemId == 930100000).Count == 1000 && displays.TicketsComplete &&
            displays.Tickets().Single(t => t.Id == 1).Count == 9 && displays.Tickets().Single(t => t.Id == 2).Count == 3 &&
            displays.Energy is { Daily: 800, Bonus: 100 },
            "Confirmed transfer merges missing holdings, currencies and tickets while newer partial readings win");
        displays.Prepare(4);
        displays.Apply(Message("inventory", 5, new InventoryBatch(true, [])), null);
        displays.Apply(Message("content-tickets", 5, new ContentTicketReading(true, [])), null);
        displays.Apply(Message("currency-ticket", 5, new CurrencyTicketReading(true, null)), null);
        displays.Bind(owner);
        check(displays.Holdings() is { Complete: true, Items.Length: 0 } && displays.Currencies().Length == 0 &&
            displays.TicketsComplete && displays.Tickets().Length == 0 && displays.Energy == null,
            "Fresh empty full snapshots prevent transfer inheritance from resurrecting old balances or tickets");
        var transferEngine = new CombatEngine();
        transferEngine.Apply(new("local-player", Actor: owner));
        transferEngine.Apply(new("network", Network: Stack(1, 50, true)));
        transferEngine.Apply(new("network", Network: Message("local-zone", 2, new { MapId = 600002, AwaitingIdentity = true })));
        transferEngine.Apply(new("local-player", Actor: owner with { Id = 2 }));
        transferEngine.Apply(new("network", Network: Message("local-zone", 3, new { MapId = 600002 })));
        transferEngine.Apply(new("network", Network: Stack(4, 52, false)));
        check(transferEngine.Loot.Rows() is [{ MapId: 600002, Quantity: 2 }] &&
            transferEngine.Snapshot().DungeonRun?.ReceivedItems is [{ Count: 2 }] &&
            transferEngine.CurrentHoldings().Items.Single().Count == 52,
            "Engine map preparation and identity confirmation preserve exact loot, dungeon rewards and holdings across transfer");
        var engine = new CombatEngine();
        var first = new Actor(1, "First", true, true, 0, "Cleric", 1);
        var second = first with { Id = 2, Name = "Second" };
        void Identity(Actor actor) => engine.Apply(new("local-player", Actor: actor));
        void Network(string kind, long time, object value) => engine.Apply(new("network", Network: new(kind, time, "test", JsonSerializer.SerializeToElement(value))));
        void Inventory(long time, long count, bool full = false) => Network(full ? "inventory" : "inventory-update", time,
            new InventoryBatch(full, [new(100, 110120003, count, 1, 1, 5)], full ? (ushort)528 : (ushort)0));
        void Prepare(long time) => Network("local-zone", time, new { MapId = 1010, AwaitingIdentity = true });
        Inventory(1, 50, true); Prepare(2); Identity(first); Inventory(3, 52);
        check(engine.Loot.Rows().Single().Quantity == 2, "Initial inventory preceding both map preparation and identity remains the first baseline");
        Prepare(10); Inventory(11, 100, true); Inventory(12, 105);
        check(engine.Loot.Rows().Single().Quantity == 2, "Pending character inventory cannot credit the previous character's loot");
        Prepare(10); // Repeated boundary must not throw away the new baseline.
        Identity(second); Inventory(13, 108);
        check(engine.Loot.Rows().Single(r => r.Character == "Second").MapId == 1010,
            "Character confirmation preserves the new map already supplied during preparation for loot attribution");
        check(engine.Loot.Rows().Single(r => r.Character == "Second").Quantity == 8 && engine.Loot.Rows().Single(r => r.Character == "First").Quantity == 2,
            "Inventory received during map preparation binds to the new identity and duplicate boundaries preserve it");
        Inventory(9, 999, true); Inventory(14, 109);
        check(engine.Loot.Rows().Single(r => r.Character == "Second").Quantity == 9, "Pre-boundary delayed inventory cannot replace the current character's baseline");
        Prepare(20); Inventory(21, 200, true); Prepare(22); Identity(first); Inventory(23, 500); Inventory(24, 501);
        check(engine.Loot.Rows().Where(r => r.Character == "First").Sum(r => r.Quantity) == 3,
            "Superseded map preparation discards unbound inventory instead of attaching it to a later login");
        Prepare(30); Inventory(31, 700, true); Identity(first); Inventory(32, 702);
        check(engine.Loot.Rows().Where(r => r.Character == "First").Sum(r => r.Quantity) == 5 &&
            engine.Fight.Network.All(e => e.Kind is not ("inventory" or "inventory-update")),
            "Same-character reentry uses a newly supplied baseline while retaining no raw inventory history");
        long oldMapLoot = engine.Loot.Rows().Where(r => r.MapId == 1010).Sum(r => r.Quantity);
        Network("account-login", 40, new LoginReading(0, 2301, [])); Inventory(41, 800, true); Identity(first);
        Network("local-zone", 39, new { MapId = 1110 }); Inventory(42, 801);
        check(engine.Loot.Rows().Any(r => r.Character == "First" && r.MapId == 0 && r.Quantity == 1 && r.First == 42),
            "Loot after reconnect without a new map stays unattributed rather than reusing a previous or delayed zone");
        Network("local-zone", 45, new { MapId = 1110 }); Inventory(46, 803);
        Network("account-login", 40, new LoginReading(0, 2301, [])); Inventory(47, 804);
        check(engine.Loot.Rows().Where(r => r.Character == "First" && r.MapId == 1110).Sum(r => r.Quantity) == 3 &&
            engine.Loot.Rows().Where(r => r.MapId == 1010).Sum(r => r.Quantity) == oldMapLoot,
            "Fresh map attribution resumes after reconnect, duplicate login preserves it, and earlier loot keeps its original map");
    }
}
