namespace AionDpsMeter.Local;

internal static class ChecklistViewTests
{
    internal static void Run(Action<bool, string> check)
    {
        long now = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var empty = ChecklistTasks.Complete([], [(2301, "First"), (2301, "Second"), (2301, "First")], now);
        check(empty.Length == 11 && empty.Count(r => r.Owner == "Server-wide") == 7 && empty.All(r => r.Count == null && r.Status == "Not captured"),
            "Full weekly checklist shares server tasks and shows two per-character tasks without duplicate or invented zero progress");
        WeeklyPurchaseRow[] observed = [new(2301, "Server-wide", "Commands", 0, 12, now + 1000, "In progress"),
            new(2301, "First", "Odyle crafting (character)", null, 4, now - 1000, "Previous period")];
        var filled = ChecklistTasks.Complete(observed, [(2301, "First"), (2301, "Second")], now);
        check(filled.Single(r => r.Task == "Commands").Count == 0 && filled.Single(r => r.Task == "Odyle crafting (character)" && r.Owner == "First").Status == "Previous period" &&
            filled.Single(r => r.Task == "Odyle crafting (character)" && r.Owner == "Second").Status == "Not captured",
            "Checklist distinguishes captured zero, expired data and unobserved tasks");
        var servers = ChecklistTasks.Complete([], [(2301, "Same"), (1301, "Same")], now);
        check(servers.Length == 18 && servers.Select(r => (r.Server, r.Owner, r.Task)).Distinct().Count() == 18,
            "Same-named characters on different servers get separate complete task lists");
        check(ChecklistTasks.Complete([], [(9999, "Unknown region")], now).All(r => r.ResetAt == null),
            "Unobserved tasks on unknown regions do not invent reset times");
        check(ChecklistTasks.Complete([], [], now).Length == 0 && ChecklistTasks.Complete(observed, [], now).Length == 9,
            "No known identity creates no fictional roster, while captured task owners can populate their complete checklist");
    }
}
