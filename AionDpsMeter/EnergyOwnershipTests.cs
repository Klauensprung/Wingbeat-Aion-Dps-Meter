using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class EnergyOwnershipTests
{
    internal static void Run(Action<bool, string> check)
    {
        var owner = new Actor(1, "First", true, false, 0, "Cleric", 2301) { PersistentId = 6000000001 };
        var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: owner));
        void Energy(long at, int daily, int bonus = 0) => engine.Apply(new("energy", Energy: new(daily, bonus, false, at)));
        void Network(string kind, long at, object value) => engine.Apply(new("network", Network: new(kind, at, "test", JsonSerializer.SerializeToElement(value))));
        Energy(10, 800, 100); Energy(9, 999);
        check(engine.Fight.Energy is { Daily: 800, Bonus: 100 }, "Older energy packet cannot regress the current energy display");
        engine.NewFight("Reset");
        check(engine.Fight.Energy?.Daily == 800, "DPS reset preserves current owner's energy");
        Network("local-zone", 20, new { MapId = 1010, AwaitingIdentity = true }); Energy(21, 500, 50);
        check(engine.Fight.Energy == null && engine.CurrentCurrencies().Length == 0, "Pending energy cannot be displayed under the previous character");
        engine.Apply(new("local-player", Actor: owner with { Name = "Second", PersistentId = 7000000001 }));
        check(engine.Fight.Energy is { Daily: 500, Bonus: 50 } && engine.CurrentCurrencies().Single(c => c.ItemId == 512000014).Count == 550,
            "Energy display and currency balance bind the same pending ticket to the new owner");
        Network("currency-ticket", 30, new CurrencyTicketReading(true, null)); Energy(29, 999);
        check(engine.Fight.Energy == null && engine.CurrentCurrencies().All(c => c.ItemId != 512000014), "Empty full ticket list clears energy and prevents stale resurrection");
        Energy(31, 0);
        check(engine.Fight.Energy?.Daily == 0 && engine.CurrentCurrencies().Single(c => c.ItemId == 512000014).Count == 0, "Explicit zero remains visible in both energy presentations");
        engine.Apply(new("local-player", Actor: owner));
        check(engine.Fight.Energy == null, "Reconnect without a new ticket baseline cannot inherit previous energy");
    }
}
