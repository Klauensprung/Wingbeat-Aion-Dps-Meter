namespace AionDpsMeter.Local;

internal static class ReminderQueueTests
{
    internal static void Run(Action<bool, string> check)
    {
        var queue = new ReminderQueue();
        queue.Add("Event starts\nPet reached level 2", 100);
        queue.Add("Debuff on party", 100);
        check(queue.Take(100, false) == "Event starts\nPet reached level 2\nDebuff on party" && queue.Count == 0,
            "Same-frame event, pet and party notices share one delivery without replacing each other");
        queue.Add("Second notice", 200);
        check(queue.Take(200, true) == null && queue.Count == 1 && queue.Take(300, false) == "Second notice",
            "A visible notification holds the next notice until delivery is available");
        for (int i = 0; i < 7; i++) queue.Add($"Notice {i}", 400);
        check(queue.Take(400, false)!.Split('\n').Length == 5 && queue.Take(500, false) == "Notice 5\nNotice 6",
            "Notice batches preserve overflow instead of hiding it behind a count");
        queue.Add("Old combat notice", 600);
        queue.Add("Recent notice", 30500);
        check(queue.Take(30600, true) == null && queue.Count == 1 && queue.Take(30600, false) == "Recent notice",
            "Queued notices expire while busy so old combat alerts do not replay later");
        for (int i = 0; i < 1000; i++) queue.Add($"Burst {i}", 31000);
        check(queue.Count == 64 && queue.Take(31000, false)!.StartsWith("Burst 936\n"),
            "Notification bursts retain at most 64 of the newest messages");
        queue = new();
        queue.Add("Same\nSame\n\n", 10);
        check(queue.Take(10, false) == "Same" && queue.Take(10, false) == null,
            "Identical same-tick notices and blank lines do not produce duplicate deliveries");
        queue.Add(new string('x', 238) + "😀tail", 20);
        check(queue.Take(20, false) == new string('x', 238) + "…", "Long notices are bounded without splitting a surrogate pair");
        queue.Add(new string('a', 140), 30);
        queue.Add(new string('b', 140), 30);
        check(queue.Take(30, false) == new string('a', 140) && queue.Take(30, false) == new string('b', 140),
            "Native notification text limits split batches without silently losing subsequent messages");
    }
}
