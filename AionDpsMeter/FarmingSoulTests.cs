using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class FarmingSoulTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var ledger = new FarmingCounterLedger(); var tracker = new FarmingSoulTracker(ledger); long at = 0;
        var fixtures = new List<object>();
        void Send(string kind, object fields) {
            var message = new NetworkEvent(kind, ++at, "synthetic", JsonSerializer.SerializeToElement(fields)); tracker.Apply(message);
            fixtures.Add(new { Reset = false, Event = message, Expected = ledger.Snapshot().Counts });
        }
        void Gains(int amount, bool decrease = false) => Send(decrease ? "pet-progress-decrease" : "pet-progress", new { Gains = new[] { new CapturedPetProgress(1002, amount) } });
        Send("pet-roster", new CapturedPetRoster(0, [new(1002, 1)], [new(1002, 80)], 0));
        check(ledger.Snapshot().Counts.Count == 0, "Farming soul tracking does not count login balances as new gains");
        Gains(3); Gains(25, true); Send("pet-added", new CapturedPetLevel(1002, 2));
        Send("pet-roster", new CapturedPetRoster(0, [new(1002, 2)], [new(1002, 58)], 0));
        check(ledger.Snapshot().Counts.GetValueOrDefault(1002) == 3, "Soul spending, pet acquisition and refreshed roster do not add or subtract farming gains");
        Gains(0); Send("pet-progress", new { Gains = new[] { new CapturedPetProgress(1002, 2), new CapturedPetProgress(1005, 4) } });
        check(ledger.Snapshot().Counts[1002] == 5 && ledger.Snapshot().Counts[1005] == 4, "Farming souls count acquired quantities per pet and ignore zero increments");
        ledger.Reset(++at); fixtures.Add(new { Reset = true, Event = (NetworkEvent?)null, Expected = ledger.Snapshot().Counts }); Gains(7);
        check(ledger.Snapshot().Counts.Count == 1 && ledger.Snapshot().Counts[1002] == 7, "Independent soul reset starts gains again without requiring a new pet roster");
        for (int n = 0; n < 64; n++) { Gains(n % 9); Gains(n % 5, true); }
        File.WriteAllText(Path.Combine(temp, "reference-farming-souls.json"), JsonSerializer.Serialize(fixtures));
        var before = ledger.Snapshot();
        tracker.Apply(new("pet-progress", at - 1, "synthetic", JsonSerializer.SerializeToElement(new { Gains = new[] { new CapturedPetProgress(1002, 999) } })));
        tracker.Apply(new("pet-progress", at + 1, "synthetic", JsonSerializer.SerializeToElement(new { Gains = new[] { new CapturedPetProgress(1002, 1), new CapturedPetProgress(1002, 3) } })));
        check(ledger.Snapshot().Counts[1002] == before.Counts[1002], "Stale and duplicate-ID soul batches cannot inflate persistent farming gains");
        string path = Path.Combine(temp, "farming-pet-souls.json"); var persisted = new FarmingCounterLedger(path); var engine = new CombatEngine(soulTracking: persisted);
        void EngineSend(string kind, long time, object fields) => engine.Apply(new("network", Network: new(kind, time, "synthetic", JsonSerializer.SerializeToElement(fields))));
        EngineSend("pet-progress", 10, new { Gains = new[] { new CapturedPetProgress(1002, 3) } }); engine.NewFight("Reset DPS");
        EngineSend("account-login", 20, new LoginReading(0, 2301, []));
        EngineSend("pet-progress", 19, new { Gains = new[] { new CapturedPetProgress(1002, 900) } });
        EngineSend("pet-progress", 21, new { Gains = new[] { new CapturedPetProgress(1002, 2) } }); persisted.Save();
        check(new FarmingCounterLedger(path).Snapshot().Counts[1002] == 5,
            "Soul gains persist across DPS reset and login while old-session progress is excluded");
    }
}
