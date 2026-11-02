using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class InventoryHoldingTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var holdings = new InventoryHoldings();
        using var output = new StreamWriter(Path.Combine(temp, "reference-inventory-holdings.jsonl"));
        void Apply(bool full, params InventoryStack[] stacks) {
            var batch = new InventoryBatch(full, stacks); holdings.Apply(batch);
            output.WriteLine(JsonSerializer.Serialize(new { Batch = batch, Expected = holdings.Snapshot() }));
        }
        Apply(false, new InventoryStack(1, 503700031, 2, 1));
        check(holdings.Snapshot() is { Complete: false, Items: [{ Count: 2 }] }, "Mid-session bag holdings preserve partial-state uncertainty");
        Apply(true, new InventoryStack(1, 503700031, 2, 1), new InventoryStack(2, 503700031, 3, 1), new InventoryStack(3, 503700032, 4, 1));
        var before = holdings.Snapshot();
        check(before.Items[0].Count == 5 && TicketPresentation.Bag(60000001, before).Contains("240"), "Bag totals sum distinct stacks and apply each consumable's ticket value");
        Apply(false, new InventoryStack(1, 503700031, 999, 1, 2));
        check(holdings.Snapshot().Items[0].Count == 3 && before.Items[0].Count == 5, "Removal excludes its old stack without mutating detached holdings");
        Apply(false, new InventoryStack(2, 503700032, 9, 1));
        check(holdings.Snapshot().Items is [{ Id: 503700032, Count: 13 }], "Replaced stack identity moves counts to the new item instead of double counting");
        Apply(true, new InventoryStack(3, 503700032, 1, 1));
        check(holdings.Snapshot().Items is [{ Count: 1 }], "Full bag inventory replaces omitted stacks independently of loot baseline merging");
        var random = new Random(6013);
        for (int i = 0; i < 200; i++) Apply(i % 17 == 0, new InventoryStack(1 + i % 10, i % 2 == 0 ? 503700031 : 503700032, random.Next(100), 1, i % 11 == 0 ? (byte)2 : (byte)1));
        Apply(true);
        check(holdings.Snapshot() is { Complete: true, Items.Length: 0 }, "Empty complete inventory means zero known bag holdings");
        holdings.Apply(new(true, [new(1, 503700031, long.MaxValue, 1), new(2, 503700031, long.MaxValue, 1)]));
        check(holdings.Snapshot().Items[0].Count == (decimal)long.MaxValue * 2, "Wide stack aggregation cannot overflow a signed 64-bit total");
        var owner = new Actor(1, "Bag owner", true, false, 0, "Cleric", 2301) { PersistentId = 6000000001 };
        var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: owner));
        void Send(string kind, long at, object fields) => engine.Apply(new("network", Network: new(kind, at, "test", JsonSerializer.SerializeToElement(fields))));
        Send("inventory", 1, new InventoryBatch(true, [new(1, 503700031, 2, 1)])); engine.NewFight("Reset");
        check(engine.CurrentHoldings().Items is [{ Count: 2 }] && engine.Fight.Network.Count == 0, "Bag totals survive DPS reset without raw inventory events");
        Send("local-zone", 2, new { MapId = 1010, AwaitingIdentity = true });
        Send("inventory", 3, new InventoryBatch(true, [new(1, 503700031, 8, 1)]));
        check(engine.CurrentHoldings() is { Complete: false, Items.Length: 0 }, "Pending bag counts stay hidden from the previous character");
        engine.Apply(new("local-player", Actor: owner with { PersistentId = 10294967297 }));
        Send("inventory", 1, new InventoryBatch(true, []));
        check(engine.CurrentHoldings() is { Complete: true, Items: [{ Count: 8 }] }, "New owner binds pending holdings and rejects old inventory snapshots");
    }
}
