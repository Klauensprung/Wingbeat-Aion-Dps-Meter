using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal sealed record ChecklistAdjustment(int Server, string Owner, string TaskId, int? Value, long Period, long EnteredAt, long CapturedAt, long CaptureVersion);
internal sealed class ChecklistAdjustments
{
    readonly Dictionary<(int Server, string Owner, string Task), ChecklistAdjustment> entries = new();
    readonly string? path;
    internal ChecklistAdjustments(string? path = null)
    {
        this.path = path;
        if (path == null || !File.Exists(path)) return;
        try {
            if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException();
            var rows = JsonSerializer.Deserialize<ChecklistAdjustment[]>(File.ReadAllText(path)) ?? throw new InvalidDataException();
            if (rows.Length > 1024 || rows.Any(r => r == null || !Valid(r)) || rows.Select(r => (r.Server, r.Owner, r.TaskId)).Distinct().Count() != rows.Length) throw new InvalidDataException();
            foreach (var r in rows) entries.Add((r.Server, r.Owner, r.TaskId), r);
        } catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) { LocalLog.Write("Checklist adjustments could not be loaded; original file retained."); }
    }
    static IEnumerable<ChecklistTask> Definitions => ChecklistTasks.Purchases.Concat(ChecklistTasks.Crafting).Append(ChecklistTasks.Duty);
    static bool Valid(ChecklistAdjustment r) {
        var task = Definitions.FirstOrDefault(t => t.Id == r.TaskId);
        return task != null && r.Server > 0 && r.Owner != null && r.Owner.Length <= 128 && !string.IsNullOrWhiteSpace(r.Owner) &&
            (task.PerCharacter ? r.Owner != "Server-wide" : r.Owner == "Server-wide") && r.Value is not < 0 && (r.Value == null || r.Value <= task.Target) &&
            r.EnteredAt >= 0 && r.Period > r.EnteredAt && r.Period <= 253402300799999 && r.CapturedAt >= 0 && r.CaptureVersion >= 0;
    }
    internal void Set(WeeklyPurchaseRow row, int? value, long now)
    {
        var task = Definitions.Single(t => t.Name == row.Task);
        var period = ChecklistTasks.ResetForTask(row.Task, row.Server, now) ?? throw new InvalidDataException("This server's reset calendar is not known.");
        var entry = new ChecklistAdjustment(row.Server, row.Owner, task.Id, value, period, now, row.CapturedAt, row.CaptureVersion);
        if (!Valid(entry)) throw new InvalidDataException($"Enter a value from 0 to {task.Target}.");
        var key = (row.Server, row.Owner, task.Id);
        if (!entries.ContainsKey(key) && entries.Count >= 1024) entries.Remove(entries.MinBy(e => e.Value.EnteredAt).Key);
        entries[key] = entry;
    }
    internal WeeklyPurchaseRow Apply(WeeklyPurchaseRow row, long now)
    {
        var task = Definitions.FirstOrDefault(t => t.Name == row.Task);
        if (task == null || !entries.TryGetValue((row.Server, row.Owner, task.Id), out var entry) || entry.Period != ChecklistTasks.ResetForTask(row.Task, row.Server, now) ||
            row.CapturedAt != entry.CapturedAt || row.CaptureVersion != entry.CaptureVersion) return row;
        return row with { Count = entry.Value, ResetAt = entry.Period, Status = entry.Value == null ? "Cleared locally" : "Entered locally" };
    }
    internal void SetAndSave(WeeklyPurchaseRow row, int? value, long now)
    {
        var previous = entries.ToArray();
        try { Set(row, value, now); Save(); }
        catch { entries.Clear(); foreach (var entry in previous) entries.Add(entry.Key, entry.Value); throw; }
    }
    internal void Save()
    {
        if (path == null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp"; File.WriteAllText(temporary, JsonSerializer.Serialize(entries.Values)); File.Move(temporary, path, true);
    }
}
