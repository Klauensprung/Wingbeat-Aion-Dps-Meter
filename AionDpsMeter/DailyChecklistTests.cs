using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class DailyChecklistTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        long now = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        long reset = new DateTimeOffset(2026, 11, 3, 7, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        check(ChecklistTasks.DailyReset(2301, now) == reset && ChecklistTasks.DailyReset(2301, reset) == reset + 86400000 && ChecklistTasks.DailyReset(9999, now) == null,
            "Daily task reset advances strictly past the boundary and refuses unknown regions");
        var ledger = new DutyMissionLedger(); var edits = new ChecklistAdjustments();
        WeeklyPurchaseRow Row(long at) => ChecklistTasks.DailyRows(ledger.Snapshot(), [2301], at).Single();
        var unknown = Row(now);
        check(unknown.Count == null && unknown.Target == 5 && unknown.Status == "Not captured", "Known servers expose unobserved daily duty progress as unknown");
        using var output = new StreamWriter(Path.Combine(temp, "reference-daily-adjustments.jsonl"));
        object[] Expected(long at) { var row = edits.Apply(Row(at), at); return row.Count == null ? [] : [(object)new { Owner = "server:2301", TaskId = "dutyMissions", row.Count, row.ResetAt }]; }
        void Capture(int count, long at) {
            long period = ChecklistTasks.DailyReset(2301, at)!.Value; var batch = new DutyMissionReading(count, period); ledger.Apply(2301, batch, at);
            output.WriteLine(JsonSerializer.Serialize(new { Kind = "duty-missions", Server = 2301, Character = "First", Timestamp = at, Batch = batch, Expected = Expected(at) }));
        }
        void Edit(int? count, long at) {
            edits.Set(Row(at), count, at);
            output.WriteLine(JsonSerializer.Serialize(new { Kind = "manual", Server = 2301, Character = "Second", Timestamp = at, TaskId = "dutyMissions", Period = ChecklistTasks.DailyReset(2301, at), Value = count, Expected = Expected(at) }));
        }
        Capture(1, now); Edit(5, now + 1);
        check(edits.Apply(Row(now + 1), now + 1) is { Count: 5, Status: "Entered locally" } manual && manual.ResetAt == reset,
            "Daily adjustment uses the daily period and server-wide scope");
        Capture(2, now + 1); Edit(null, now + 2); Capture(0, now + 2);
        check(edits.Apply(Row(now + 2), now + 2) is { Count: 0, Status: "In progress" }, "Same-timestamp captured daily zero replaces a local clear");
        Edit(4, now + 3);
        check(edits.Apply(Row(reset), reset).Count == null, "Daily manual progress expires without inventing a fresh zero");
        Capture(1, reset); Edit(3, reset + 1); Capture(2, reset + 2);
        var fixtures = from server in new[] { 1101, 1201, 1301, 1401, 1501, 2101, 2301, 2501, 9999 }
                       from at in new[] { now, reset - 1, reset, reset + 1, now + 7 * 86400000L }
                       select new { Server = server, Timestamp = at, Cadence = "daily", ResetAt = ChecklistTasks.DailyReset(server, at) };
        File.WriteAllText(Path.Combine(temp, "reference-daily-calendar.json"), JsonSerializer.Serialize(fixtures));
    }
}
