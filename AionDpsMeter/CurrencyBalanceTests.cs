using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class CurrencyBalanceTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var owner = new Actor(1, "Currency owner", true, false, 0, "Cleric", 2301) { PersistentId = 6000000001 };
        var tracker = new InventoryLootTracker(); long at = 0;
        using var output = new StreamWriter(Path.Combine(temp, "reference-currency-balances.jsonl"));
        void Apply(bool full, params InventoryStack[] rows) {
            var batch = new InventoryBatch(full, rows);
            tracker.Apply(new(full ? "inventory" : "inventory-update", ++at, "test", JsonSerializer.SerializeToElement(batch)), owner);
            output.WriteLine(JsonSerializer.Serialize(new { Batch = batch, Expected = tracker.Currencies() }));
        }
        Apply(false, new InventoryStack(1, 930100031, 5000000000, 1));
        check(tracker.Currencies() is [{ Count: 5000000000 }], "Mid-session currency update records an absolute wide balance without inventing loot");
        Apply(true, new InventoryStack(1, 930100031, 20, 1), new InventoryStack(2, 930100001, 30, 1), new InventoryStack(3, 110120003, 99, 1));
        var detached = tracker.Currencies();
        Apply(false, new InventoryStack(1, 930100031, 15, 1));
        check(tracker.Currencies().Single(r => r.ItemId == 930100031).Count == 15 && detached.Single(r => r.ItemId == 930100031).Count == 20,
            "Currency spending replaces the balance and detached snapshots remain unchanged");
        Apply(false, new InventoryStack(2, 930100001, 999, 1, 2));
        check(tracker.Currencies().Single(r => r.ItemId == 930100001).Count == 0, "Removed currency stack explicitly reports zero");
        Apply(true, new InventoryStack(1, 930100031, 12, 1));
        check(tracker.Currencies() is [{ ItemId: 930100031, Count: 12 }], "Full currency snapshot clears omitted balances independently of loot baseline merging");
        Apply(false, new InventoryStack(4, 930099999, 1, 1), new InventoryStack(5, 930101000, 1, 1));
        Apply(false, new InventoryStack(6, 930100031, 11, 1));
        check(tracker.Currencies() is [{ Count: 11 }], "Currency family boundaries and latest-item balance match reference semantics rather than summing instances");
        var random = new Random(2991);
        for (int i = 0; i < 200; i++) Apply(i % 19 == 0, new InventoryStack(100 + i % 12, 930100000 + i % 12, random.Next(100000), 1, i % 13 == 0 ? (byte)2 : (byte)1));
        void Ticket(bool full, long? count) {
            var ticket = new CurrencyTicketReading(full, count);
            tracker.Apply(new("currency-ticket", ++at, "test", JsonSerializer.SerializeToElement(ticket)), owner);
            output.WriteLine(JsonSerializer.Serialize(new { Ticket = ticket, Expected = tracker.Currencies() }));
        }
        Ticket(true, 960); Apply(true, new InventoryStack(1, 930100031, 100, 1));
        check(tracker.Currencies().Single(c => c.ItemId == 512000014).Count == 960, "Inventory refresh preserves independently captured ticket currency");
        Ticket(false, 0);
        check(tracker.Currencies().Single(c => c.ItemId == 512000014).Count == 0, "Ticket currency zero is explicit and distinct from an absent full-list entry");
        Ticket(true, null);
        check(tracker.Currencies().All(c => c.ItemId != 512000014), "Full ticket list without energy removes its old currency balance");
        tracker.Bind(owner with { PersistentId = 10294967297 });
        check(tracker.Currencies().Length == 0, "Full character identity change clears captured currency balances");
        var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: owner));
        void Send(string kind, long timestamp, object fields) => engine.Apply(new("network", Network: new(kind, timestamp, "test", JsonSerializer.SerializeToElement(fields))));
        Send("inventory", 1, new InventoryBatch(true, [new(1, 930100031, 123, 1)])); engine.NewFight("DPS reset");
        check(engine.CurrentCurrencies() is [{ Count: 123 }] && engine.Fight.Network.Count == 0,
            "Current currency balances survive DPS resets without raw inventory history");
        Send("local-zone", 2, new { MapId = 1010, AwaitingIdentity = true });
        Send("inventory", 3, new InventoryBatch(true, [new(1, 930100031, 456, 1)]));
        check(engine.CurrentCurrencies().Length == 0, "Pending login currency balances cannot be displayed under the old owner");
        engine.Apply(new("local-player", Actor: owner with { PersistentId = 10294967297 }));
        check(engine.CurrentCurrencies() is [{ Count: 456 }], "New owner binds the pending currency snapshot");
        Send("currency-ticket", 4, new CurrencyTicketReading(false, 910));
        Send("currency-ticket", 3, new CurrencyTicketReading(false, 999));
        check(engine.CurrentCurrencies().Single(c => c.ItemId == 512000014).Count == 910 && engine.Fight.Network.All(e => e.Kind != "currency-ticket"),
            "Ticket currency ignores stale updates and retains no raw packet history");
    }
}
