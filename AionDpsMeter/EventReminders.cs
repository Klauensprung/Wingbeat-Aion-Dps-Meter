namespace AionDpsMeter.Local;

internal sealed record EventSchedule(string Id, string Name, int[] UtcMinutes, DayOfWeek? Day = null)
{
    internal DayOfWeek[]? Days { get; init; }
    internal string Basis { get; init; } = "Global reported schedule · UTC";
    internal DateTimeOffset Next(DateTimeOffset after)
    {
        var utc = after.ToUniversalTime();
        for (int day = 0; day <= 7; day++) {
            var date = new DateTimeOffset(utc.Date.AddDays(day), TimeSpan.Zero);
            if (Day != null && date.DayOfWeek != Day) continue;
            if (Days != null && !Days.Contains(date.DayOfWeek)) continue;
            foreach (int minute in UtcMinutes.Order()) {
                var candidate = date.AddMinutes(minute);
                if (candidate >= utc) return candidate;
            }
        }
        throw new InvalidOperationException("Schedule has no occurrence within a week.");
    }
}
internal sealed record EventReminder(string Id, string Name, DateTimeOffset StartsAt);
internal sealed class EventReminders
{
    // Reported Global timings, reviewed 2026-10-31. These are local schedule
    // reminders, not proof that an event started or a server reset completed.
    internal static readonly EventSchedule[] Schedules = [
        new("shugo", "Shugo Festival", Enumerable.Range(0, 24).Select(h => h * 60).ToArray()),
        new("rift", "Spacetime Rift", Enumerable.Range(0, 8).Select(h => h * 180).ToArray()) { Basis = "Global reports: every 3 h from 00:00 UTC; differs from the client table" },
        new("invasion", "Beritra Air Raid / Dimensional Invasion", Enumerable.Range(0, 24).Select(h => h * 60 + 30).ToArray()) { Basis = "Client schedule: hourly at :30" },
        new("artifact-siege", "Artifact Siege", [720]) { Days = [DayOfWeek.Monday, DayOfWeek.Thursday, DayOfWeek.Saturday], Basis = "Client: Mon/Thu/Sat 21:00 on UTC+9 clock = 12:00 UTC" },
        new("nahma", "Guardian Lord Nahma (Lower / Middle Reshanta)", [720]) { Days = [DayOfWeek.Friday, DayOfWeek.Sunday], Basis = "Client: Fri/Sun 21:00 on UTC+9 clock = 12:00 UTC" },
        new("abyss-bosses", "Reshanta executioner bosses", [750]) { Days = [DayOfWeek.Monday, DayOfWeek.Thursday, DayOfWeek.Saturday], Basis = "Argo/Kaira/Tamasa and Dramos/Marakha/Ducal; client Mon/Thu/Sat 21:30 UTC+9 = 12:30 UTC" },
        new("watcher-kaira", "Watcher Kaira", Enumerable.Range(0, 8).Select(h => h * 180 + 60).ToArray()) { Basis = "Client: every 3 h from 01:00 on UTC+9 clock; 01:00/04:00/07:00… UTC" },
        new("arena", "Arena of Tactics opens", [120, 600]) { Basis = "Client: daily 11:00 and 19:00 on UTC+9 clock = 02:00 and 10:00 UTC" },
        new("daily-reset", "Daily reset", [420]),
        new("weekly-reset", "Weekly reset", [420], DayOfWeek.Wednesday)
    ];
    readonly Dictionary<string, DateTimeOffset> announced = new();
    internal EventReminder[] Poll(DateTimeOffset now, IEnumerable<string> enabled, int leadSeconds)
    {
        var selected = enabled.ToHashSet(StringComparer.Ordinal);
        var reminders = new List<EventReminder>();
        int lead = Math.Clamp(leadSeconds, 0, 1800);
        foreach (var schedule in Schedules) {
            if (!selected.Contains(schedule.Id)) continue;
            // A short tick grace supports a zero-second reminder without replaying
            // events missed during suspend or a long period with the app closed.
            var next = schedule.Next(now.AddSeconds(-10));
            if (next > now.AddSeconds(lead) || announced.TryGetValue(schedule.Id, out var previous) && next <= previous) continue;
            announced[schedule.Id] = next; reminders.Add(new(schedule.Id, schedule.Name, next));
        }
        return reminders.ToArray();
    }
}
