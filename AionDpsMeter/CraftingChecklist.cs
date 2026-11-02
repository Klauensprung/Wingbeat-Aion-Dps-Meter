using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal sealed record CraftingProgress(int Server, string Character, string Task, int Count, long ResetAt, long Timestamp, long Sequence = 0);
public sealed class CraftingChecklist
{
    readonly Dictionary<(int Server, string Character, string Task), CraftingProgress> entries = new();
    readonly object gate = new(), saveGate = new();
    readonly string? path;
    long revision, savedRevision, sequence;
    public CraftingChecklist(string? path = null)
    {
        this.path = path;
        if (path == null || !File.Exists(path)) return;
        try {
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException();
            var rows = JsonSerializer.Deserialize<CraftingProgress[]>(File.ReadAllText(path)) ?? throw new InvalidDataException();
            if (rows.Length > 1024 || rows.Any(r => r == null || !Valid(r)) || rows.Select(r => (r.Server, r.Character, r.Task)).Distinct().Count() != rows.Length) throw new InvalidDataException();
            foreach (var row in rows) entries.Add((row.Server, row.Character, row.Task), row);
            sequence = rows.Select(r => r.Sequence).DefaultIfEmpty().Max();
        } catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) { LocalLog.Write("Saved crafting counters could not be loaded; original file retained."); }
    }
    static bool Valid(CraftingProgress r) => r.Server > 0 && r.Count >= 0 && r.Sequence >= 0 && r.Timestamp >= 0 && r.ResetAt > r.Timestamp && r.ResetAt <= 253402300799999 && r.Character != null && r.Character.Length <= 128 &&
        (r.Task == "morphOdyleServer" ? r.Character == "" : r.Task == "morphOdyle" && !string.IsNullOrWhiteSpace(r.Character));
    internal void Apply(NetworkEvent message, Actor? owner)
    {
        if (message.Kind != "morph-counts" || owner is not { Player: true, Server: > 0 } || ChecklistTasks.WeeklyReset(owner.Server, message.Timestamp) is not long reset) return;
        var batch = message.Fields.Deserialize<ConversionBatch>(); if (batch == null) return;
        lock (gate) foreach (var item in batch.Counts) {
            bool shared = item.Recipe is 31070001 or 32070001;
            if (!shared && item.Recipe is not (31070002 or 32070002)) continue;
            int? count = shared ? item.Account : item.Character; if (count == null || count < 0) continue;
            string character = shared ? "" : owner.Name, task = shared ? "morphOdyleServer" : "morphOdyle";
            var key = (owner.Server, character, task); entries.TryGetValue(key, out var previous);
            if (previous != null && previous.Timestamp > message.Timestamp) continue;
            // Recipe aliases replace the task total; unlike purchases, they do not sum.
            if (sequence == long.MaxValue) { sequence = 0; foreach (var entry in entries.OrderBy(e => e.Value.Sequence).ToArray()) entries[entry.Key] = entry.Value with { Sequence = ++sequence }; }
            var next = new CraftingProgress(owner.Server, character, task, count.Value, reset, message.Timestamp, ++sequence);
            if (!Valid(next) || next == previous) continue;
            if (previous == null && entries.Count >= 1024) entries.Remove(entries.MinBy(e => e.Value.Timestamp).Key);
            entries[key] = next; revision++;
        }
    }
    internal CraftingProgress[] Snapshot() { lock (gate) return entries.Values.OrderBy(r => r.Server).ThenBy(r => r.Character).ThenBy(r => r.Task).ToArray(); }
    internal WeeklyPurchaseRow[] Rows(long now) => Snapshot().Select(r => new WeeklyPurchaseRow(r.Server, r.Character == "" ? "Server-wide" : r.Character,
        r.Task == "morphOdyle" ? "Odyle crafting (character)" : "Odyle crafting (server)", r.ResetAt > now ? r.Count : null,
        r.Task == "morphOdyle" ? 4 : 16, r.ResetAt, r.ResetAt <= now ? "Previous period" : r.Count >= (r.Task == "morphOdyle" ? 4 : 16) ? "Target reached" : "In progress", r.Timestamp, r.Sequence)).ToArray();
    internal void Save()
    {
        if (path == null) return;
        lock (saveGate) {
            CraftingProgress[] rows; long version;
            lock (gate) { if (revision == savedRevision) return; rows = entries.Values.ToArray(); version = revision; }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temporary = path + ".tmp"; File.WriteAllText(temporary, JsonSerializer.Serialize(rows)); File.Move(temporary, path, true);
            lock (gate) savedRevision = version;
        }
    }
}
