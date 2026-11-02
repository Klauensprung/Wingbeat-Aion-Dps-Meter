namespace AionDpsMeter.Local;

// UI-thread owned. Keep short-lived notices bounded even while a long notice is visible.
internal sealed class ReminderQueue
{
    readonly Queue<(string Text, long Added)> pending = new();
    internal int Count => pending.Count;
    internal void Add(string text, long now)
    {
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            var value = line.Length <= 240 ? line : line[..(char.IsHighSurrogate(line[238]) ? 238 : 239)] + "…";
            if (pending.Any(p => p.Text == value && p.Added == now)) continue;
            if (pending.Count == 64) pending.Dequeue();
            pending.Enqueue((value, now));
        }
    }
    internal string? Take(long now, bool busy)
    {
        // Future timestamps also expire if the monotonic clock is reset in a test or session.
        while (pending.TryPeek(out var item) && (item.Added > now || now - item.Added >= 30000)) pending.Dequeue();
        if (busy || pending.Count == 0) return null;
        var lines = new List<string>();
        int length = 0;
        // Native notification bodies hold 255 UTF-16 characters including the terminator.
        while (lines.Count < 5 && pending.TryPeek(out var item) && length + item.Text.Length + (lines.Count > 0 ? 1 : 0) <= 240) {
            pending.Dequeue();
            length += item.Text.Length + (lines.Count > 0 ? 1 : 0);
            lines.Add(item.Text);
        }
        return string.Join("\n", lines);
    }
}
