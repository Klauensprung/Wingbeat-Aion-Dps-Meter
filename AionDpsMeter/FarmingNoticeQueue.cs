namespace AionDpsMeter.Local;

// Overlay-only transient notices; the Targets window keeps its separate history.
internal sealed class FarmingNoticeQueue
{
    readonly List<(FarmingNotice Notice, long At)> entries = new(3);
    internal FarmingNotice[] Update(IEnumerable<FarmingNotice> incoming, long now, bool clear = false)
    {
        if (clear) entries.Clear();
        entries.RemoveAll(e => now - e.At >= 8000);
        foreach (var notice in incoming) {
            entries.Insert(0, (notice, now));
            if (entries.Count > 3) entries.RemoveAt(3);
        }
        return entries.Select(e => e.Notice).ToArray();
    }
}
