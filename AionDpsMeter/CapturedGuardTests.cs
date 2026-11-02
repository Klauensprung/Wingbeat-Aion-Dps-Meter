using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class CapturedGuardTests
{
    internal static void Run(Action<bool, string> check)
    {
        var fight = new Fight();
        void Guard(long at, byte state, int current, int max) => fight.Network.Add(new("guard", at, "test",
            JsonSerializer.SerializeToElement(new GuardReading(2, state, max, current, 0, 0, 0, false, 0, true))));
        Guard(100, 1, 25, 100);
        check(CapturedGuard.Build(fight)[2] is { Ratio: .25, Broken: false }, "Captured stagger ratio uses the transmitted current and maximum values");
        Guard(200, 3, 0, 100); Guard(150, 1, 50, 100);
        check(CapturedGuard.Build(fight)[2] is { Ratio: 0, Broken: true, Timestamp: 200 }, "Reference guard state 3 means broken; delayed earlier readings cannot replace it");
        Guard(300, 0, 0, 0);
        check(CapturedGuard.Build(fight).Count == 0, "A zero-maximum guard update clears an earlier stagger bar");
        Guard(400, 1, 150, 100);
        check(CapturedGuard.Build(fight)[2].Ratio == 1, "Stagger display bounds its bar without rewriting the captured count");
        fight.Network.Add(new("entity-death", 400, "test", JsonSerializer.SerializeToElement(new { EntityId = 2 })));
        check(CapturedGuard.Build(fight).Count == 0, "Observed death hides stale target guard including tied timestamps");
        Guard(500, 1, 50, 100);
        fight.Actors[3] = new(3, "Archived", false, false, 0, "Unknown", 0) { OriginalEntityId = 2 };
        check(CapturedGuard.Build(fight).Count == 0, "Reused entity IDs do not inherit ambiguous stagger readings");
    }
}
