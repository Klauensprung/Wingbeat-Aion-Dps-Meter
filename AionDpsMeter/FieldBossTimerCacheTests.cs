using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class FieldBossTimerCacheTests
{
    internal static void Run(Action<bool, string> check)
    {
        const long now = 1700000000000;
        FieldBossObservation Observation(long seen, long due, bool alive = false) => new(seen,
            new(1010, [new(1, alive, null, null, null, due)]));
        var directory = Path.Combine(Path.GetTempPath(), "Wingbeat-boss-cache-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "timers.json");
        try {
            var cache = new FieldBossTimerCache(path);
            cache.Capture(2301, [Observation(now, now + 60000)]);
            cache.Capture(2302, [Observation(now, now + 3600000)]);
            check(cache.Rows(2301, now).Single().Remaining == "0.00:01:00" && cache.Rows(2302, now).Single().Remaining == "0.01:00:00",
                "Boss cache separates identical map slots by server");
            check(cache.Poll(2301, now, 120, false).Length == 0 && cache.Poll(0, now, 120, true).Length == 0,
                "Boss notifications require opt-in and a known server");
            cache.Save();
            cache = new(path);
            var disabled = new HashSet<string> { FieldBossTimerCache.SelectionKey(2301, 1010, 1) };
            check(cache.Poll(2301, now, 120, true, disabled).Length == 0 &&
                !disabled.Contains(FieldBossTimerCache.SelectionKey(2302, 1010, 1)),
                "Individual boss exclusions suppress reminders without affecting another server");
            check(cache.LastServer == 2302 && cache.Rows(2301, now).Length == 1,
                "Boss observations survive restart without opening the timer window");
            check(cache.Poll(2301, now, 120, true).Length == 1 && cache.Poll(2301, now + 1, 120, true).Length == 0,
                "Boss reminders fire once inside the chosen lead window");
            cache.Save(); cache = new(path);
            check(cache.Poll(2301, now + 2, 120, true).Length == 0,
                "Restart does not repeat a delivered boss deadline");
            cache.Capture(2301, [Observation(now - 1, now + 90000)]);
            check(cache.Rows(2301, now).Single().Remaining == "0.00:01:00",
                "Older boss observations cannot replace a newer cached deadline");
            cache.Capture(2301, [Observation(now + 3, now + 90000)]);
            check(cache.Poll(2301, now + 4, 120, true).Length == 1,
                "A corrected boss deadline can generate its own reminder");
            cache.Capture(2301, [Observation(now + 5, now + 100000, true)]);
            check(cache.Poll(2301, now + 6, 120, true).Length == 0,
                "Reported alive bosses never trigger respawn countdown reminders");
            check(cache.Poll(2302, now + 86400000, 120, true).Length == 0 &&
                cache.Rows(2302, now + 86400000).Single().Status == "Timer elapsed; refresh in game",
                "Expired boss times do not become invented daily schedules");
            File.WriteAllText(path, "{broken");
            check(new FieldBossTimerCache(path).Rows(2301, now).Length == 0,
                "Corrupt boss cache safely starts empty");

            var engine = new CombatEngine();
            var observation = Observation(now, now + 60000);
            var ev = new NetworkEvent("field-boss-list", now, "synthetic", JsonSerializer.SerializeToElement(observation.List));
            engine.Apply(new("network", Network: ev));
            check(engine.FieldBossCache.LastServer == 0, "Boss observations without live identity are not assigned to a cached server");
            engine.Apply(new("local-player", Actor: new(1, "Player 1", true, false, 0, "Cleric", 2301)));
            engine.Apply(new("network", Network: ev));
            engine.NewFight("Next fight");
            check(engine.FieldBossCache.Rows(2301, now).Length == 1,
                "Live boss lists populate the cache and survive fight resets");
            engine.Apply(new("network", Network: new NetworkEvent("account-login", now + 10, "synthetic", JsonSerializer.SerializeToElement(new LoginReading(0, 2302, [])))));
            check(engine.FieldBosses.MapCount == 0 && engine.FieldBossCache.Rows(2301, now).Length == 1,
                "Login clears live boss state but retains server-scoped cached observations");
        } finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
