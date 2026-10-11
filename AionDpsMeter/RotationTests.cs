using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace AionDpsMeter.Local;

internal static class RotationTests
{
    public static void Run(Action<bool, string> check)
    {
        var fight = FeatureTests.Sample(); var data = RotationData.Build(fight, [1, 2, 10]); var lane = data.Lanes[0];
        check(data.Lanes.Length == 3 && data.Lanes[2].Hits.Length == 0 && lane.Hits.Select(h => h.Seconds).SequenceEqual(new[] { 0d, 2d, 5d, 30d }),
            "Rotation index retains idle members and orders damage/healing on the fight's shared clock");
        check(lane.Nearest(14, 0, 30, false)?.Seconds == 0 && lane.Nearest(16, 0, 30, false)?.Seconds == 30,
            "Rotation inspection snaps between events without requiring a direct hit on a narrow marker");
        check(lane.Nearest(3, 0, 30, true) is { Healing: true, Amount: 250, TargetId: 1 } && lane.Nearest(4, 0, 30, true) is { Amount: 750, TargetId: 2 },
            "Healing inspection preserves self/group targets separately from damage");
        check(lane.Nearest(29, 10, 30, true) == null && lane.Nearest(14, 10, 30, false)?.Seconds == 30 && lane.Nearest(15, 10, 20, false) == null,
            "Rotation hit testing excludes offscreen events and includes the visible right edge");
        fight.Damage.Add(fight.Damage[0] with { Damage = 2222 }); fight.Healing[0] = fight.Healing[0] with { Timestamp = 1000, IsHeal = false };
        lane = RotationData.Build(fight, [1]).Lanes[0];
        check(lane.Hits.Where(h => h.Seconds == 0).Select(h => h.Sequence).Distinct().Count() == 3 && lane.Healing.Length == 2,
            "Simultaneous rotation events retain distinct step positions and legacy healing classification");
        var large = new Fight(); for (int i = 0; i < 12000; i++) large.Damage.Add(new() { ActorId = 1, Timestamp = 1000 + i, Damage = i, SkillCode = 17010001 });
        lane = RotationData.Build(large, [1]).Lanes[0];
        check(lane.Hits.Length == 12000 && lane.Nearest(0, 0, 1, false)?.Amount == 0 && lane.Nearest(12, 11, 12, false)?.Amount == 11999,
            "Zoomable rotations retain old events beyond the previous 5,000-per-character limit");
    }
    public static void CheckView(RotationView view, Fight fight, Action<string> render)
    {
        var ids = new[] { 1, 2, 10 }; view.Update(fight, ids, "live-test"); render("Rotations-inspect-ready");
        var plot = view.Plot; int builds = view.ModelBuilds;
        if (VisualTreeHelper.GetChildrenCount(plot) != 0) throw new Exception("Rotation plot created per-event visual children.");
        view.Update(fight.Copy(), ids, "live-test");
        if (view.ModelBuilds != builds) throw new Exception("Unchanged rotation events rebuilt the index.");
        var point = new Point(RotationPlot.LabelWidth + plot.PlotWidth * .3, RotationPlot.AxisHeight + 20);
        view.SetHover(true); view.InspectAt(point, false);
        if (view.Selected is not { Amount: 1000, ActorId: 1, Healing: false } || !view.DetailText.Contains("Test target")) throw new Exception("Wide rotation hit testing did not show immediate details.");
        render("Rotations-hover"); int scenes = plot.SceneBuilds;
        var appended = fight.Copy(); appended.Damage.Add(fight.Damage[1] with { Timestamp = 45000, Damage = 9999 });
        view.Update(appended, ids, "live-test");
        if (view.ModelBuilds != builds || view.Selected.Amount != 1000) throw new Exception("Live data moved a timeline being inspected.");
        view.InspectAt(point, true); view.SetHover(false);
        if (!view.Held) throw new Exception("A pinned event changed after leaving the plot.");
        view.Step(1);
        if (view.Selected is not { Healing: true, Amount: 250 }) throw new Exception("Rotation stepping skipped a self-heal.");
        view.Step(1);
        if (view.Selected is not { Healing: true, Amount: 750 } || !view.DetailText.Contains("Member 2")) throw new Exception("Rotation stepping lost a group-heal recipient.");
        render("Rotations-pinned-heal");
        if (plot.SceneBuilds != scenes) throw new Exception("Moving the inspector rebuilt the full drawing.");
        view.SetZoom(3); render("Rotations-zoomed");
        if (view.Selected?.Amount != 750 || plot.At(new(RotationPlot.LabelWidth + plot.PlotWidth / 2, RotationPlot.AxisHeight + 40))?.Amount != 750)
            throw new Exception("Zoom did not keep a pinned healing event in view.");
        view.SetZoom(4);
        view.Resume();
        if (view.Held || view.ModelBuilds != builds + 1 || view.Selected != null) throw new Exception("Resume did not apply pending events.");
        view.Seek(0); view.InspectAt(new(RotationPlot.LabelWidth + 1, RotationPlot.AxisHeight + 20), true);
        if (view.Selected?.Seconds != 0) throw new Exception("Scrubbing did not expose earlier events.");
        var nextFight = FeatureTests.Sample(1); view.Update(nextFight, ids, "live-test");
        if (view.DisplayedFight != fight.Id) throw new Exception("A new fight replaced pinned details.");
        view.Resume();
        if (view.DisplayedFight != nextFight.Id) throw new Exception("Resume did not switch to the latest fight.");
        view.SetHover(true); view.Update(fight, ids, "saved-different");
        if (view.Held || view.DisplayedFight != fight.Id) throw new Exception("Opening a saved fight retained stale inspection.");
        view.Resume();
        var corrected = fight.Copy(); corrected.Damage[0] = corrected.Damage[0] with { Damage = 2000 };
        view.Update(corrected, ids, "saved-different"); view.InspectAt(point, true);
        if (view.Selected?.Amount != 2000) throw new Exception("Replacing a saved event with the same count retained stale amounts.");
        view.Resume();
        view.SetZoom(3); view.Seek(8);
        double anchor = view.WindowStart + view.WindowSeconds * .3;
        int zoomBuilds = view.ModelBuilds;
        view.ZoomAt(.5, .3); render("Rotations-pointer-zoom");
        if (Math.Abs(view.WindowSeconds - 5) > .0001 || Math.Abs(view.WindowStart + view.WindowSeconds * .3 - anchor) > .0001 || !view.Held || view.ModelBuilds != zoomBuilds)
            throw new Exception("Pointer zoom moved its anchor, resumed capture scrolling or rebuilt the event index.");
        view.ZoomAt(.001, .5);
        if (view.WindowSeconds != 1) throw new Exception("Rotation zoom did not clamp to a one-second window.");
        view.SetZoom(6); render("Rotations-fit-fight");
        if (view.WindowStart != 0 || view.WindowSeconds != Math.Max(1, corrected.Seconds)) throw new Exception("Fit fight clipped the rotation.");
        view.SetZoom(4); view.Resume();
    }
    public static object[] Benchmark()
    {
        var result = new List<object>();
        foreach (int count in new[] { 5000, 50000 })
        {
            var f = new Fight();
            for (int i = 1; i <= 10; i++) f.Actors[i] = new(i, "Player " + i, true, true, 0, "Cleric", 1);
            for (int i = 0; i < count; i++) f.Damage.Add(new() { ActorId = i % 10 + 1, Timestamp = 1000 + i * 10, Damage = 100, SkillCode = 17010001 });
            var data = RotationData.Build(f, Enumerable.Range(1, 10).ToArray()); double width = 1000, height = RotationPlot.AxisHeight + 10 * RotationPlot.RowHeight + 10;
            void Render(FrameworkElement element)
            {
                element.Measure(new(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
                new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32).Render(element);
            }
            void Old()
            {
                var canvas = new Canvas { Width = width, Height = height };
                for (int row = 0; row < data.Lanes.Length; row++)
                {
                    var label = new TextBlock { Text = data.Lanes[row].Actor.Name }; Canvas.SetTop(label, RotationPlot.AxisHeight + row * RotationPlot.RowHeight); canvas.Children.Add(label);
                    foreach (var hit in data.Lanes[row].Hits)
                    {
                        var marker = new Rectangle { Width = 3, Height = 18, Fill = Brushes.LightBlue, ToolTip = $"{hit.Seconds:N2}s · Skill {hit.SkillCode} · {hit.Amount}" };
                        Canvas.SetLeft(marker, RotationPlot.LabelWidth + hit.Seconds / data.Seconds * (width - RotationPlot.LabelWidth - 12)); Canvas.SetTop(marker, RotationPlot.AxisHeight + row * RotationPlot.RowHeight + 12); canvas.Children.Add(marker);
                    }
                }
                Render(canvas);
            }
            RotationPlot New() { var plot = new RotationPlot(); plot.Update(data, 0, data.Seconds); Render(plot); return plot; }
            double Time(Action action) { var sw = Stopwatch.StartNew(); action(); sw.Stop(); return sw.Elapsed.TotalMilliseconds; }
            Old(); _ = New();
            double oldMs = Time(Old); RotationPlot? current = null; double newMs = Time(() => current = New());
            long found = 0; double lookupMs = Time(() => {
                for (int i = 0; i < 10000; i++) if (current!.At(new(RotationPlot.LabelWidth + i % 800, RotationPlot.AxisHeight + i % 10 * RotationPlot.RowHeight + 20)) != null) found++;
            });
            if (found != 10000) throw new Exception("Rotation benchmark lost visible events.");
            result.Add(new { Events = count, OldWpfBuildAndRenderMs = oldMs, NewWpfBuildAndRenderMs = newMs, HitTests = 10000, HitTestMs = lookupMs,
                NewVisualChildren = VisualTreeHelper.GetChildrenCount(current!), current!.DrawnMarks });
        }
        return result.ToArray();
    }
}
