using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;
internal static class FarmingPetTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var registrations = new FarmingCounterLedger(); var levels = new FarmingCounterLedger();
        var tracker = new FarmingPetTracker(registrations, levels); long at = 0;
        var fixtures = new List<object>();
        object Counts() => new { Registered = registrations.Snapshot().Counts, LevelUps = levels.Snapshot().Counts };
        void Send(string kind, object fields) {
            var e = new NetworkEvent(kind, ++at, "synthetic", JsonSerializer.SerializeToElement(fields)); tracker.Apply(e);
            fixtures.Add(new { Reset = false, Event = e, Expected = Counts() });
        }
        void Roster(int level) => Send("pet-roster", new CapturedPetRoster(0, level == 0 ? [] : [new(1002, level)], [], 0));
        void Spend(int amount) => Send("pet-progress-decrease", new { Gains = new[] { new CapturedPetProgress(1002, amount) } });
        void Added(int level) => Send("pet-added", new CapturedPetLevel(1002, level));
        void Reset() { registrations.Reset(++at); levels.Reset(at); fixtures.Add(new { Reset = true, Event = (NetworkEvent?)null, Expected = Counts() }); }
        Roster(3);
        check(registrations.Snapshot().Counts.Count == 0 && levels.Snapshot().Counts.Count == 0, "Existing roster pets do not count as farming registrations or levels");
        for (int order = 0; order < 2; order++) {
            Reset(); Roster(0);
            if (order == 0) { Added(1); Spend(5); } else { Spend(5); Added(1); }
            Added(1);
            check(registrations.Snapshot().Counts.GetValueOrDefault(1002) == 1 && levels.Snapshot().Counts.Count == 0,
                "Registration/spend ordering and repeated acquisition do not double count registration");
            Spend(25); Added(2); Spend(75); Added(3);
            check(levels.Snapshot().Counts.GetValueOrDefault(1002) == 2 && registrations.Snapshot().Counts[1002] == 1,
                "Inferred upgrades followed by explicit level confirmation count once");
        }
        foreach (int spend in new[] { 0, 5, 25, 30, 75, 100, 105, 106 }) {
            foreach (int initial in new[] { 0, 1, 2, 3 }) {
                Reset(); Roster(initial); Spend(spend); Added(1); Added(2); Added(3);
            }
        }
        Reset(); Roster(1); Spend(25); Reset(); Added(2); Spend(75);
        check(registrations.Snapshot().Counts.Count == 0 && levels.Snapshot().Counts[1002] == 1,
            "Counter reset preserves current level and pending spending evidence");
        File.WriteAllText(Path.Combine(temp, "reference-farming-pets.json"), JsonSerializer.Serialize(fixtures));
        tracker.Apply(new("pet-added", at - 1, "synthetic", JsonSerializer.SerializeToElement(new CapturedPetLevel(1005, 3))));
        check(!registrations.Snapshot().Counts.ContainsKey(1005), "Stale pet acquisition cannot inflate farming counters");
        var engine = new CombatEngine(petRegistrationTracking: registrations, petLevelTracking: levels);
        void EngineSend(string kind, long time, object fields) => engine.Apply(new("network", Network: new(kind, time, "synthetic", JsonSerializer.SerializeToElement(fields))));
        EngineSend("pet-roster", 1000, new CapturedPetRoster(0, [new(1005, 1)], [], 0)); engine.NewFight("Reset DPS");
        EngineSend("pet-added", 1001, new CapturedPetLevel(1005, 2));
        check(levels.Snapshot().Counts[1005] == 1 && !registrations.Snapshot().Counts.ContainsKey(1005), "DPS reset preserves pet farming baseline");
        EngineSend("account-login", 1010, new LoginReading(0, 2301, []));
        EngineSend("pet-added", 1009, new CapturedPetLevel(1006, 3));
        EngineSend("pet-roster", 1011, new CapturedPetRoster(0, [new(1005, 1)], [], 0));
        EngineSend("pet-added", 1012, new CapturedPetLevel(1005, 2));
        check(levels.Snapshot().Counts[1005] == 2 && !registrations.Snapshot().Counts.ContainsKey(1006), "Login replaces pet baseline and rejects old-session activity");
    }
}
