using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class ChecklistAdjustmentTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        long now = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        long period = ChecklistTasks.WeeklyReset(2301, now)!.Value;
        var row = new WeeklyPurchaseRow(2301, "First", "Odyle purchase (character)", 1, 4, period, "In progress", now, 1);
        string path = Path.Combine(temp, "checklist-adjustments.json"); var edits = new ChecklistAdjustments(path);
        edits.Set(row, 3, now);
        check(edits.Apply(row, now) is { Count: 3, Status: "Entered locally" } && row.Count == 1,
            "Local checklist entry replaces displayed progress without changing captured counters");
        check(edits.Apply(row with { Owner = "Second" }, now).Count == 1 && edits.Apply(row with { Server = 1301 }, now).Count == 1,
            "Checklist adjustments stay isolated by character and server");
        check(edits.Apply(row with { CaptureVersion = 2, Count = 2 }, now) is { Count: 2, Status: "In progress" },
            "A later capture with the same timestamp supersedes manual progress using its sequence");
        edits.Save(); var restored = new ChecklistAdjustments(path);
        check(restored.Apply(row, now).Count == 3, "Checklist adjustments survive app restart with their captured baseline anchor");
        restored.Set(row, null, now + 1); restored.Save();
        check(new ChecklistAdjustments(path).Apply(row, now + 1) is { Count: null, Status: "Cleared locally" },
            "Cleared local progress persists as unknown until another captured update");
        check(edits.Apply(row, period).Count == 1, "Manual progress expires at its weekly boundary");
        var expired = row with { Count = null, ResetAt = now - 1, Status = "Previous period" };
        edits.Set(expired, 2, now + 2);
        check(edits.Apply(expired, now + 2) is { Count: 2 } current && current.ResetAt == period,
            "Manual entry can start the current week when only an old captured period exists");
        bool invalid = false; try { edits.Set(row, 5, now); } catch (InvalidDataException) { invalid = true; }
        bool unknown = false; try { edits.Set(row with { Server = 9999 }, 2, now); } catch (InvalidDataException) { unknown = true; }
        check(invalid && unknown, "Manual progress rejects values above target and unknown reset calendars");
        string bad = Path.Combine(temp, "invalid-adjustments.json"); File.WriteAllText(bad, "[{\"Server\":-1}]");
        check(new ChecklistAdjustments(bad).Apply(row, now) == row && File.ReadAllText(bad) == "[{\"Server\":-1}]",
            "Invalid manual checklist data cannot crash startup or delete the source file");
        string blockedPath = Path.Combine(temp, "checklist-parent-file"); File.WriteAllText(blockedPath, "keep");
        var failedSave = new ChecklistAdjustments(Path.Combine(blockedPath, "progress.json")); bool failed = false;
        try { failedSave.SetAndSave(row, 2, now); } catch (IOException) { failed = true; }
        check(failed && failedSave.Apply(row, now) == row && File.ReadAllText(blockedPath) == "keep", "Failed checklist save restores prior displayed state and preserves existing files");
        using var output = new StreamWriter(Path.Combine(temp, "reference-manual-checklist.jsonl"));
        var captured = new ShopPurchaseTracker(); var manual = new ChecklistAdjustments();
        WeeklyPurchaseRow[] Rows(long time) => ChecklistTasks.Complete(ChecklistTasks.Summarize(captured.Snapshot(), time), [(2301, "First"), (2301, "Second")], time);
        object[] Expected(long time) => Rows(time).Select(r => manual.Apply(r, time)).Where(r => r.Count != null).Select(r => (object)new {
            Owner = r.Owner == "Server-wide" ? $"server:{r.Server}" : $"{r.Server}:{r.Owner}",
            TaskId = ChecklistTasks.Purchases.Concat(ChecklistTasks.Crafting).Single(t => t.Name == r.Task).Id, r.Count, r.ResetAt
        }).ToArray();
        void Capture(string name, int goods, int count, long time, bool added = false) {
            var batch = new ShopPurchaseBatch(added, [new(goods, count, null)]);
            captured.Apply(new("shop-purchases", time, "test", JsonSerializer.SerializeToElement(batch)), new(1, name, true, true, 0, "Cleric", 2301));
            output.WriteLine(JsonSerializer.Serialize(new { Kind = "shop-purchases", Server = 2301, Character = name, Timestamp = time, Batch = batch, Expected = Expected(time) }));
        }
        void Edit(string name, string taskId, int? value, long time) {
            var definition = ChecklistTasks.Purchases.Single(t => t.Id == taskId);
            var selected = Rows(time).Single(r => r.Task == definition.Name && r.Owner == (definition.PerCharacter ? name : "Server-wide"));
            manual.Set(selected, value, time);
            output.WriteLine(JsonSerializer.Serialize(new { Kind = "manual", Server = 2301, Character = name, Timestamp = time, TaskId = taskId, Period = ChecklistTasks.WeeklyReset(2301, time), Value = value, Expected = Expected(time) }));
        }
        Capture("First", 133019901, 5, now);
        Edit("First", "commands", 12, now + 1);
        Capture("Second", 233019901, 2, now + 1);
        Edit("Second", "commands", null, now + 2);
        Capture("First", 133019901, 1, now + 3, true);
        Edit("First", "buyOdyle", 3, now + 4);
        Capture("Second", 1209, 1, now + 5);
        Edit("First", "buyOdyle", null, now + 6);
        Capture("First", 1209, 0, now + 7);
        Edit("First", "commands", 2, now + 8);
        Capture("First", 999999, 1, period);
        Edit("First", "commands", 3, period + 1);
        Capture("First", 133019901, 1, period + 2);
    }
}
