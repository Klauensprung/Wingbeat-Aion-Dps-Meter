namespace AionDpsMeter.Local;

internal sealed record ChecklistTask(string Id, string Name, int Target, bool PerCharacter, int[] Goods);
internal sealed record WeeklyPurchaseRow(int Server, string Owner, string Task, decimal? Count, int Target, long? ResetAt, string Status, long CapturedAt = 0, long CaptureVersion = 0)
{
    public string Progress => Count is decimal count ? $"{count:N0} / {Target:N0}" : $"Not captured / {Target:N0}";
    public string Reset => ResetAt is long at ? DateTimeOffset.FromUnixTimeMilliseconds(at).ToLocalTime().ToString("dd MMM HH:mm") : "Unknown";
}
internal static class ChecklistTasks
{
    // Factual task IDs, goods mappings and limits from the pinned reference Ao catalogue.
    internal static readonly ChecklistTask[] Purchases = [
        new("commands", "Commands", 12, false, [133019901, 233019901]),
        new("abyssCommands", "Abyss commands", 5, false, [134019901, 140019901, 234019901, 240019901]),
        new("abyssCommandsVeteran", "Veteran Abyss commands", 5, false, [134019902, 140019902, 234019902, 240019902]),
        new("abyssCommandsElite", "Elite Abyss commands", 5, false, [134019903, 140019903, 234019903, 240019903]),
        new("abyssCommandsSpecial", "Special Abyss commands", 5, false, [134019904, 140019904, 234019904, 240019904]),
        new("buyOdyle", "Odyle purchase (character)", 4, true, [1209]),
        new("buyOdyleServer", "Odyle purchase (server)", 16, false, [1210])
    ];
    internal static readonly ChecklistTask[] Crafting = [
        new("morphOdyle", "Odyle crafting (character)", 4, true, []),
        new("morphOdyleServer", "Odyle crafting (server)", 16, false, [])
    ];
    internal static WeeklyPurchaseRow[] Complete(IEnumerable<WeeklyPurchaseRow> observed, IEnumerable<(int Server, string Name)> knownCharacters, long now)
    {
        var rows = observed.ToDictionary(r => (r.Server, r.Owner, r.Task));
        var characters = knownCharacters.Concat(rows.Values.Where(r => r.Owner != "Server-wide").Select(r => (r.Server, r.Owner)))
            .Where(c => c.Server > 0 && !string.IsNullOrWhiteSpace(c.Item2)).Distinct().ToArray();
        var definitions = Purchases.Concat(Crafting).ToArray();
        foreach (int server in characters.Select(c => c.Server).Concat(rows.Values.Select(r => r.Server)).Distinct()) {
            var reset = WeeklyReset(server, now);
            foreach (var task in definitions) {
                var owners = task.PerCharacter ? characters.Where(c => c.Server == server).Select(c => c.Item2) : ["Server-wide"];
                foreach (string owner in owners) rows.TryAdd((server, owner, task.Name), new(server, owner, task.Name, null, task.Target, reset, "Not captured"));
            }
        }
        return rows.Values.OrderBy(r => r.Server).ThenBy(r => r.Owner).ThenBy(r => r.Task).ToArray();
    }
    internal static readonly ChecklistTask Duty = new("dutyMissions", "Duty missions", 5, false, []);
    internal static long? ResetForTask(string task, int server, long timestamp) => task == Duty.Name ? DailyReset(server, timestamp) : WeeklyReset(server, timestamp);
    internal static long? DailyReset(int server, long timestamp)
    {
        if (WeeklyReset(server, timestamp) == null) return null;
        var now = DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
        var reset = new DateTimeOffset(now.Year, now.Month, now.Day, 7, 0, 0, TimeSpan.Zero);
        if (reset <= now) reset = reset.AddDays(1);
        return reset.ToUnixTimeMilliseconds();
    }
    internal static WeeklyPurchaseRow[] DailyRows(IEnumerable<DutyMissionProgress> readings, IEnumerable<int> knownServers, long now)
    {
        var rows = readings.ToDictionary(r => r.Server, r => new WeeklyPurchaseRow(r.Server, "Server-wide", Duty.Name, r.ResetAt > now ? r.Rewarded : null, 5, r.ResetAt,
            r.ResetAt <= now ? "Previous period" : r.Rewarded >= 5 ? "Target reached" : "In progress", r.Timestamp, r.Sequence));
        foreach (int server in knownServers.Where(s => s > 0).Distinct()) rows.TryAdd(server, new(server, "Server-wide", Duty.Name, null, 5, DailyReset(server, now), "Not captured"));
        return rows.Values.OrderBy(r => r.Server).ToArray();
    }
    internal static ChecklistTask? ForGoods(int goods) => Purchases.FirstOrDefault(t => t.Goods.Contains(goods));
    internal static WeeklyPurchaseRow[] Summarize(IEnumerable<ShopPurchaseProgress> source, long now)
    {
        return source.Select(r => (Reading: r, Task: ForGoods(r.Goods))).Where(r => r.Task != null)
            .GroupBy(r => (r.Reading.Server, r.Reading.Character, r.Task!.Id)).Select(group => {
                var task = group.First().Task!;
                var latest = group.MaxBy(r => (r.Reading.Timestamp, r.Reading.Sequence)).Reading;
                var period = group.Where(r => r.Reading.ResetAt == latest.ResetAt).Select(r => r.Reading).ToArray();
                bool current = latest.ResetAt > now;
                decimal? count = current ? period.Sum(r => (decimal)r.Count) : null;
                string status = latest.ResetAt == null ? "Reset unknown" : !current ? "Previous period" :
                    period.Any(r => !r.BaselineKnown) ? "Observed additions only" : count >= task.Target ? "Target reached" : "In progress";
                return new WeeklyPurchaseRow(group.Key.Server, group.Key.Character.Length == 0 ? "Server-wide" : group.Key.Character, task.Name, count, task.Target, latest.ResetAt, status, latest.Timestamp, latest.Sequence);
            }).OrderBy(r => r.Server).ThenBy(r => r.Owner).ThenBy(r => r.Task).ToArray();
    }
    internal static long? WeeklyReset(int server, long timestamp)
    {
        // Reference global regions (NAE/NAW/EU/LA/AS) all reset Wednesday
        // 16:00 Asia/Seoul, i.e. 07:00 UTC, independently of local DST.
        if (server / 1000 is not (1 or 2) || server % 1000 / 100 is < 1 or > 5 || timestamp is < 0 or > 253401436799999) return null;
        var now = DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
        var day = new DateTimeOffset(now.Year, now.Month, now.Day, 7, 0, 0, TimeSpan.Zero);
        int days = ((int)DayOfWeek.Wednesday - (int)day.DayOfWeek + 7) % 7;
        var reset = day.AddDays(days); if (reset <= now) reset = reset.AddDays(7);
        return reset.ToUnixTimeMilliseconds();
    }
}
