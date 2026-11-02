namespace AionDpsMeter.Local;

internal static class EventReminderTests
{
    internal static void Run(Action<bool, string> check)
    {
        var now = new DateTimeOffset(2026, 10, 31, 6, 58, 0, TimeSpan.Zero);
        var reminders = new EventReminders();
        check(reminders.Poll(now, [], 120).Length == 0, "Event reminders are silent until selected");
        var first = reminders.Poll(now, ["shugo", "daily-reset", "unknown"], 120);
        check(first.Length == 2 && first.All(r => r.StartsAt == now.AddMinutes(2)),
            "Simultaneous selected events share the correct UTC occurrence and unknown IDs are ignored");
        check(reminders.Poll(now.AddSeconds(1), ["shugo", "daily-reset"], 300).Length == 0 &&
            reminders.Poll(now.AddMinutes(-1), ["shugo", "daily-reset"], 300).Length == 0,
            "Repeated ticks, lead-time edits and clock rollback do not repeat an announced occurrence");
        check(reminders.Poll(now.AddHours(1), ["shugo", "daily-reset"], 120).Single().Id == "shugo",
            "The next hourly occurrence remains eligible without repeating the daily reset");
        check(new EventReminders().Poll(now.AddMinutes(2).AddSeconds(2), ["shugo"], 0).Length == 1 &&
            new EventReminders().Poll(now.AddMinutes(2).AddSeconds(11), ["shugo"], 0).Length == 0,
            "Zero-lead reminders tolerate a late timer tick but do not replay missed events after a long pause");
        var weekly = EventReminders.Schedules.Single(s => s.Id == "weekly-reset");
        var next = weekly.Next(now);
        check(next == new DateTimeOffset(2026, 11, 4, 7, 0, 0, TimeSpan.Zero) && weekly.Next(next.AddSeconds(1)) == next.AddDays(7),
            "Weekly reminders advance across month boundaries and roll to the following week after the scheduled instant");
        var daily = EventReminders.Schedules.Single(s => s.Id == "daily-reset");
        check(daily.Next(now.ToOffset(TimeSpan.FromHours(-5))) == daily.Next(now) &&
            daily.Next(now.AddDays(1)).Hour == 7,
            "UTC event schedules do not shift when display time-zone offsets change");
        var siege = EventReminders.Schedules.Single(s => s.Id == "artifact-siege");
        var saturday = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
        check(siege.Next(saturday) == saturday && siege.Next(saturday.AddSeconds(1)) == saturday.AddDays(2),
            "Multi-day siege schedules keep an exact start and skip Sunday to the next Monday");
        var rift = EventReminders.Schedules.Single(s => s.Id == "rift");
        check(rift.Next(now) == new DateTimeOffset(2026, 10, 31, 9, 0, 0, TimeSpan.Zero) &&
            rift.Next(new DateTimeOffset(2026, 10, 31, 23, 59, 59, TimeSpan.Zero)) == new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            "Reported Global rift times use the explicit UTC phase and cross midnight correctly");
        var arena = EventReminders.Schedules.Single(s => s.Id == "arena");
        check(arena.Next(now).Hour == 10 && arena.Next(now.AddHours(5)).Hour == 2 &&
            EventReminders.Schedules.Select(s => s.Id).Distinct().Count() == EventReminders.Schedules.Length,
            "Multiple daily windows roll to the following day and event identities remain unique");
    }
}
