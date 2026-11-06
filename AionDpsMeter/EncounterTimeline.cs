using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace AionDpsMeter.Local;

internal sealed record DamageBucket(double From, double To, long Damage)
{
    internal double Dps => Damage / Math.Max(.001, To - From);
}

internal static class EncounterTimeline
{
    internal static DamageBucket[] Build(Fight fight, IEnumerable<int> actors)
    {
        var ids = actors.ToHashSet();
        double seconds = fight.Seconds;
        int count = (int)Math.Clamp(Math.Ceiling(seconds / 2), 1, 120);
        var amounts = new long[count]; long start = fight.Start;
        foreach (var hit in fight.Damage) {
            if (!ids.Contains(hit.ActorId)) continue;
            int index = Math.Clamp((int)((hit.Timestamp - start) / 1000d / seconds * count), 0, count - 1);
            amounts[index] += hit.Damage;
        }
        return Enumerable.Range(0, count).Select(i => new DamageBucket(i * seconds / count, (i + 1) * seconds / count, amounts[i])).ToArray();
    }
}

// Fixed-size drawing, not one visual per hit. A saved or live fight uses the
// same bounded plot; hover displays the real aggregated interval.
internal sealed class EncounterTimelinePlot : FrameworkElement
{
    DamageBucket[] buckets = [];
    Fight? previous;
    int[] previousActors = [];
    internal int Builds { get; private set; }
    internal int BucketCount => buckets.Length;
    internal EncounterTimelinePlot()
    {
        Height = 64; MinWidth = 100; ClipToBounds = true;
        MouseMove += (_, e) => {
            if (buckets.Length == 0) return;
            int index = Math.Clamp((int)(e.GetPosition(this).X / Math.Max(1, ActualWidth) * buckets.Length), 0, buckets.Length - 1);
            var bucket = buckets[index];
            ToolTip = global::AionDpsMeter.Local.Localization.F($"{MeterPresentation.Duration(bucket.From)}–{MeterPresentation.Duration(bucket.To)}\n{bucket.Damage:N0} damage · {bucket.Dps:N1} DPS");
        };
    }
    internal void Update(Fight fight, IEnumerable<int> actorIds)
    {
        var actors = actorIds.Distinct().Order().ToArray();
        if (ReferenceEquals(fight, previous) && actors.SequenceEqual(previousActors)) return;
        previous = fight; previousActors = actors;
        buckets = EncounterTimeline.Build(fight, actors); Builds++; InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double width = ActualWidth, height = Math.Max(1, ActualHeight - 20);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var grid = new Pen(UiTheme.Brush("#27303C"), 1);
        for (int i = 0; i < 3; i++) dc.DrawLine(grid, new(0, i * height / 2), new(width, i * height / 2));
        if (buckets.Length == 0) return;
        double peak = buckets.Max(b => b.Dps), max = Math.Max(1, peak);
        var line = new StreamGeometry();
        using (var path = line.Open()) {
            for (int i = 0; i < buckets.Length; i++) {
                var point = new Point((i + .5) * width / buckets.Length, height - buckets[i].Dps / max * (height - 8));
                if (i == 0) path.BeginFigure(point, false, false); else path.LineTo(point, true, false);
            }
        }
        line.Freeze();
        var accent = TryFindResource("Accent") as Brush ?? UiTheme.Brush("#55D6BE");
        dc.DrawGeometry(null, new Pen(accent, 2) { LineJoin = PenLineJoin.Round }, line);
        if (buckets.Length == 1) dc.DrawEllipse(accent, null, new(width / 2, height - buckets[0].Dps / max * (height - 8)), 3, 3);
        void Label(string value, double x, double y) => dc.DrawText(new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 10, UiTheme.Brush("#94A3B8"), VisualTreeHelper.GetDpi(this).PixelsPerDip), new(x, y));
        Label("0:00", 0, height + 5); Label(MeterPresentation.Duration(buckets[^1].To), Math.Max(0, width - 40), height + 5);
        Label(MeterPresentation.Number(peak) + " peak DPS", Math.Max(0, width - 105), 3);
    }
}
