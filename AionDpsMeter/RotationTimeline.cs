using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal sealed record RotationHit(int ActorId, int TargetId, int SkillCode, int Amount, bool Healing, bool Critical, double Seconds, int Sequence);
internal sealed class RotationLane(Actor actor, RotationHit[] hits)
{
    public Actor Actor { get; } = actor;
    public RotationHit[] Hits { get; } = hits;
    public RotationHit[] Damage { get; } = hits.Where(h => !h.Healing).ToArray();
    public RotationHit[] Healing { get; } = hits.Where(h => h.Healing).ToArray();
    public static int LowerBound(RotationHit[] hits, double seconds)
    {
        int low = 0, high = hits.Length;
        while (low < high) { int mid = low + (high - low) / 2; if (hits[mid].Seconds < seconds) low = mid + 1; else high = mid; }
        return low;
    }
    public RotationHit? Nearest(double seconds, double start, double end, bool healing)
    {
        var events = healing ? Healing : Damage;
        int first = LowerBound(events, start), after = LowerBound(events, Math.BitIncrement(end));
        if (first >= after) return null;
        int at = Math.Clamp(LowerBound(events, seconds), first, after - 1);
        if (at > first && Math.Abs(events[at - 1].Seconds - seconds) <= Math.Abs(events[at].Seconds - seconds)) at--;
        return events[at];
    }
}
internal sealed record RotationData(string FightId, long Start, double Seconds, RotationLane[] Lanes)
{
    public static RotationData Build(Fight fight, int[] actorIds)
    {
        long start = fight.Start; var wanted = actorIds.ToHashSet();
        var hits = new Dictionary<int, List<RotationHit>>();
        void Add(List<DamageEvent> events, bool healing)
        {
            for (int i = 0; i < events.Count; i++)
            {
                var hit = events[i]; if (!wanted.Contains(hit.ActorId)) continue;
                if (!hits.TryGetValue(hit.ActorId, out var actorHits)) hits[hit.ActorId] = actorHits = new();
                actorHits.Add(new(hit.ActorId, hit.TargetId, hit.SkillCode, hit.Damage, healing, hit.IsCritical, (hit.Timestamp - start) / 1000d, i * 2 + (healing ? 1 : 0)));
            }
        }
        Add(fight.Damage, false); Add(fight.Healing, true);
        return new(fight.Id, start, fight.Seconds, actorIds.Distinct().Select(id => new RotationLane(
            fight.Actors.GetValueOrDefault(id) ?? new(id, fight.Name(id), false, false, 0, "Unknown", 0),
            hits.GetValueOrDefault(id)?.OrderBy(h => h.Seconds).ThenBy(h => h.Sequence).ToArray() ?? [])).ToArray());
    }
}

// One retained drawing replaces thousands of WPF elements. Mouse hit testing
// uses sorted event arrays, independently of the width/density of drawn marks.
internal sealed class RotationPlot : FrameworkElement
{
    public const double LabelWidth = 155, AxisHeight = 28, RowHeight = 54;
    static readonly Brush Muted = UiTheme.Brush("#93A2B8"), Text = UiTheme.Brush("#E7EDF7"), Heal = UiTheme.Brush("#65D6AF");
    static readonly Brush Surface = UiTheme.Brush("#121B28"), Alternate = UiTheme.Brush("#182231");
    static readonly Pen GridLine = FrozenPen("#283448", 1), CursorLine = FrozenPen("#DAE5F4", 1);
    DrawingGroup? scene;
    RotationData data = new("", 0, 1, []);
    double start, span = 30;
    RotationHit? selection;
    public int SceneBuilds { get; private set; }
    public int DrawnMarks { get; private set; }
    public double PlotWidth => Math.Max(1, ActualWidth - LabelWidth - 12);
    public event Action<Point, bool>? Inspect;
    public event Action<bool>? Hover;
    public event Action<int>? Step;
    public event Action? Resume;
    public event Action<double, double>? Zoom;
    public RotationPlot()
    {
        Height = 100; MinWidth = 360; Focusable = true; Cursor = Cursors.Cross; ClipToBounds = true;
        SizeChanged += (_, _) => ClearScene();
        MouseEnter += (_, _) => Hover?.Invoke(true);
        MouseLeave += (_, _) => Hover?.Invoke(false);
    }
    static Pen FrozenPen(string color, double width) { var pen = new Pen(UiTheme.Brush(color), width); pen.Freeze(); return pen; }
    void ClearScene() { scene = null; InvalidateVisual(); }
    public void Update(RotationData value, double windowStart, double windowSeconds)
    {
        if (ReferenceEquals(data, value) && start == windowStart && span == windowSeconds) return;
        data = value; start = windowStart; span = Math.Max(.1, windowSeconds);
        Height = Math.Max(100, AxisHeight + data.Lanes.Length * RowHeight + 10); ClearScene();
    }
    public void Select(RotationHit? value) { if (selection == value) return; selection = value; InvalidateVisual(); }
    public RotationHit? At(Point point)
    {
        if (point.X < LabelWidth || point.X > LabelWidth + PlotWidth || point.Y < AxisHeight) return null;
        int row = (int)((point.Y - AxisHeight) / RowHeight);
        if (row >= data.Lanes.Length) return null;
        double seconds = start + Math.Clamp((point.X - LabelWidth) / PlotWidth, 0, 1) * span;
        bool healing = (point.Y - AxisHeight) % RowHeight >= 33;
        return data.Lanes[row].Nearest(seconds, start, start + span, healing);
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); Inspect?.Invoke(e.GetPosition(this), false); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { base.OnMouseLeftButtonDown(e); Focus(); Inspect?.Invoke(e.GetPosition(this), true); e.Handled = true; }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var point = e.GetPosition(this);
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && point.X >= LabelWidth && point.X <= LabelWidth + PlotWidth)
        {
            Zoom?.Invoke(Math.Pow(.8, e.Delta / 120d), (point.X - LabelWidth) / PlotWidth);
            e.Handled = true;
        }
        base.OnMouseWheel(e);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right) { Step?.Invoke(e.Key == Key.Right ? 1 : -1); e.Handled = true; }
        else if (e.Key == Key.Escape) { Resume?.Invoke(); e.Handled = true; }
        else if (e.Key is Key.Add or Key.OemPlus or Key.Subtract or Key.OemMinus)
        {
            Zoom?.Invoke(e.Key is Key.Add or Key.OemPlus ? .8 : 1.25, .5); e.Handled = true;
        }
        base.OnKeyDown(e);
    }
    public static string Time(double seconds) => seconds >= 60 ? $"{(int)seconds / 60}:{seconds % 60:00.0}" : $"{seconds:0.0}s";
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (scene == null) BuildScene();
        dc.DrawDrawing(scene);
        if (selection == null || selection.Seconds < start || selection.Seconds > start + span) return;
        int row = Array.FindIndex(data.Lanes, l => l.Actor.Id == selection.ActorId); if (row < 0) return;
        double x = LabelWidth + (selection.Seconds - start) / span * PlotWidth, y = AxisHeight + row * RowHeight;
        dc.DrawLine(CursorLine, new(x, AxisHeight), new(x, ActualHeight));
        dc.DrawRoundedRectangle(null, CursorLine, new(x - 5, y + (selection.Healing ? 34 : 8), 10, selection.Healing ? 14 : 25), 2, 2);
    }
    void BuildScene()
    {
        scene = new(); SceneBuilds++; DrawnMarks = 0;
        using (var dc = scene.Open())
        {
            dc.DrawRectangle(Surface, null, new(0, 0, Math.Max(1, ActualWidth), Math.Max(1, ActualHeight)));
            void Label(string text, double x, double y, Brush brush, double size = 11, double maxWidth = 145)
            {
                var label = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
                    { MaxTextWidth = maxWidth, MaxTextHeight = 19, Trimming = TextTrimming.CharacterEllipsis };
                dc.DrawText(label, new(x, y));
            }
            for (int i = 0; i < data.Lanes.Length; i++)
            {
                double y = AxisHeight + i * RowHeight;
                dc.DrawRectangle(i % 2 == 0 ? Alternate : Surface, null, new(0, y, Math.Max(1, ActualWidth), RowHeight));
                var lane = data.Lanes[i]; var color = UiTheme.Brush(MeterPresentation.ColorFor(lane.Actor));
                Label(lane.Actor.Name, 9, y + 8, Text, 12);
                Label($"{lane.Damage.Length:N0} hits · {lane.Healing.Length:N0} heals", 9, y + 29, Muted, 10);
                void Marks(RotationHit[] hits, bool healing)
                {
                    int first = RotationLane.LowerBound(hits, start), after = RotationLane.LowerBound(hits, Math.BitIncrement(start + span));
                    if (first == after) return;
                    var geometry = new StreamGeometry();
                    using (var path = geometry.Open())
                    {
                        int left = -1, right = -1;
                        void Segment()
                        {
                            if (left < 0) return;
                            double top = y + (healing ? 38 : 12), bottom = top + (healing ? 7 : 18);
                            path.BeginFigure(new(LabelWidth + left, top), true, true);
                            path.LineTo(new(LabelWidth + right, top), true, false); path.LineTo(new(LabelWidth + right, bottom), true, false);
                            path.LineTo(new(LabelWidth + left, bottom), true, false); DrawnMarks++;
                        }
                        for (int j = first; j < after; j++)
                        {
                            int pixel = (int)Math.Round((hits[j].Seconds - start) / span * PlotWidth);
                            if (left >= 0 && pixel <= right) { right = Math.Max(right, pixel + 3); continue; }
                            Segment(); left = pixel; right = pixel + 3;
                        }
                        Segment(); // Merge only overlapping pixels; every raw event remains independently inspectable.
                    }
                    geometry.Freeze(); dc.DrawGeometry(healing ? Heal : color, null, geometry);
                }
                Marks(lane.Damage, false); Marks(lane.Healing, true);
                dc.DrawLine(GridLine, new(0, y + RowHeight), new(ActualWidth, y + RowHeight));
            }
            double approximate = span / Math.Max(2, PlotWidth / 100), power = Math.Pow(10, Math.Floor(Math.Log10(approximate)));
            double tick = new[] { 1d, 2, 5, 10 }.First(n => n * power >= approximate) * power;
            for (double at = Math.Ceiling(start / tick) * tick; at <= start + span; at += tick)
            {
                double x = LabelWidth + (at - start) / span * PlotWidth;
                dc.DrawLine(GridLine, new(x, AxisHeight), new(x, ActualHeight));
                Label(Time(at), Math.Min(x + 3, ActualWidth - 58), 5, Muted, maxWidth: 58);
            }
            if (data.Lanes.Length == 0) Label("No matching characters yet", 12, 48, Muted, maxWidth: 300);
        }
        scene.Freeze();
    }
}

internal sealed record RotationRow(int ActorId, string Character, double Seconds, string Event, string Skill, int SkillCode, string Target, int Amount, bool Critical)
{
    internal RotationHit Hit { get; init; } = null!;
}

internal sealed class RotationView : Grid
{
    readonly RotationPlot plot = new();
    static readonly double[] ZoomSeconds = [1, 2, 5, 10, 30, 60];
    readonly ComboBox zoom = new() { ItemsSource = new[] { "1 second", "2 seconds", "5 seconds", "10 seconds", "30 seconds", "60 seconds", "Whole fight", "Custom zoom" }, SelectedIndex = 4, Width = 130, Margin = new(0, 0, 6, 0) };
    readonly ScrollBar scrub = new() { Orientation = Orientation.Horizontal, Height = 16, Margin = new(155, 6, 12, 6), Minimum = 0, SmallChange = 1 };
    readonly TextBlock state = new() { Foreground = UiTheme.Brush("#93A2B8"), Margin = new(0, 8, 0, 8), TextWrapping = TextWrapping.Wrap };
    readonly TextBlock title = new() { Text = "Point anywhere in a player's damage or healing lane", FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock detail = new() { Text = "Details appear immediately. Click to pin, then use ◀ / ▶ or the arrow keys to step through events.", Foreground = UiTheme.Brush("#AAB6C8"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 5, 0, 0) };
    readonly TextBlock tableSummary = new() { Margin = new(0, 8, 0, 2), Foreground = UiTheme.Brush("#93A2B8"), TextWrapping = TextWrapping.Wrap };
    readonly Image icon = new() { Width = 38, Height = 38, Source = SkillIcons.Placeholder, Margin = new(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
    readonly Button hold, previous, next;
    readonly DataGrid table;
    readonly Func<int, string> skillName;
    readonly Func<int, ImageSource?> skillIcon;
    Fight? source, pending;
    int[] actors = [], pendingActors = [];
    RotationData data = new("", 0, 1, []);
    RotationHit? selected;
    bool hovering, pinned, paused, following = true, adjusting, updatingTable;
    double windowStart;
    double windowSeconds = 30;
    bool wholeFight, adjustingZoom;
    string context = "";
    public int ModelBuilds { get; private set; }
    internal RotationPlot Plot => plot;
    internal RotationHit? Selected => selected;
    internal bool Held => hovering || pinned || paused;
    internal string DisplayedFight => source?.Id ?? "";
    internal string DetailText => detail.Text;
    double Span => wholeFight ? Math.Max(1, data.Seconds) : windowSeconds;
    internal double WindowStart => windowStart;
    internal double WindowSeconds => Span;
    internal RotationView(DataGrid eventTable, Func<int, string> names, Func<int, ImageSource?> images)
    {
        table = eventTable; skillName = names; skillIcon = images; Margin = new(8, 0, 8, 8);
        Button Button(string text, Action action) { var b = new Button { Content = text, Padding = new(10, 6, 10, 6), Margin = new(0, 0, 6, 0) }; b.Click += (_, _) => action(); return b; }
        var controls = new WrapPanel();
        controls.Children.Add(new TextBlock { Text = "Zoom", VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 8, 0) });
        var zoomOut = Button("−", () => ZoomAt(1.25, ZoomAnchor)); zoomOut.ToolTip = "Zoom out (−)"; controls.Children.Add(zoomOut);
        controls.Children.Add(zoom);
        var zoomIn = Button("+", () => ZoomAt(.8, ZoomAnchor)); zoomIn.ToolTip = "Zoom in (+ or Ctrl + mouse wheel over the timeline)"; controls.Children.Add(zoomIn);
        controls.Children.Add(Button("Fit fight", () => ChangeZoom(data.Seconds, .5, true)));
        controls.Children.Add(Button("Earlier", () => Seek(windowStart - Span * .8)));
        controls.Children.Add(Button("Later", () => Seek(windowStart + Span * .8)));
        controls.Children.Add(Button("Follow latest", Resume));
        hold = Button("Hold view", () => { if (paused || pinned) Resume(); else { paused = true; UpdateState(); } }); controls.Children.Add(hold);
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        Children.Add(controls); SetRow(state, 1); Children.Add(state);
        var inspect = new DockPanel(); DockPanel.SetDock(icon, Dock.Left); inspect.Children.Add(icon);
        var steps = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        previous = Button("◀", () => Step(-1)); previous.ToolTip = "Previous event (Left arrow)";
        next = Button("▶", () => Step(1)); next.ToolTip = "Next event (Right arrow)";
        steps.Children.Add(previous); steps.Children.Add(next); DockPanel.SetDock(steps, Dock.Right); inspect.Children.Add(steps);
        var labels = new StackPanel(); labels.Children.Add(title); labels.Children.Add(detail); inspect.Children.Add(labels);
        var inspection = new Border { Child = inspect, Background = UiTheme.Brush("#1C2939"), Padding = new(12), CornerRadius = new(5), MinHeight = 78, Margin = new(0, 5, 0, 4) };
        SetRow(inspection, 2); Children.Add(inspection);
        var timelinePanel = new DockPanel(); DockPanel.SetDock(scrub, Dock.Bottom); timelinePanel.Children.Add(scrub); timelinePanel.Children.Add(SplitView.Scroll(plot));
        var eventPanel = new DockPanel(); DockPanel.SetDock(tableSummary, Dock.Top); eventPanel.Children.Add(tableSummary);
        table.MaxHeight = double.PositiveInfinity; table.MinHeight = 60; table.Margin = new(0, 6, 0, 0); eventPanel.Children.Add(table);
        var split = new SplitView(timelinePanel, eventPanel, .55, 90, 100); SetRow(split, 3); Children.Add(split);
        plot.Hover += SetHover; plot.Inspect += InspectAt; plot.Step += Step; plot.Resume += Resume;
        plot.Zoom += ZoomAt;
        scrub.ValueChanged += (_, _) => { if (!adjusting) Seek(scrub.Value); };
        zoom.SelectionChanged += (_, _) => {
            if (adjustingZoom) return;
            int index = zoom.SelectedIndex;
            if (index >= 0 && index < ZoomSeconds.Length) ChangeZoom(ZoomSeconds[index], ZoomAnchor);
            else if (index == ZoomSeconds.Length) ChangeZoom(data.Seconds, .5, true);
        };
        table.SelectionChanged += (_, _) => { if (!updatingTable && table.SelectedItem is RotationRow row) InspectHit(row.Hit, true); };
        UpdateState();
    }
    public void Update(Fight fight, int[] actorIds, string viewContext)
    {
        if (context != viewContext) { context = viewContext; pinned = paused = hovering = false; following = true; selected = null; source = null; plot.Select(null); ResetDetails(); }
        pending = fight; pendingActors = actorIds;
        if (!Held) ApplyPending();
        else UpdateState();
    }
    void ApplyPending()
    {
        if (pending == null) return;
        var fight = pending; pending = null;
        bool same = source != null && source.Id == fight.Id && source.Damage.SequenceEqual(fight.Damage) && source.Healing.SequenceEqual(fight.Healing) &&
            data.Start == fight.Start && data.Seconds == fight.Seconds && actors.SequenceEqual(pendingActors) && source.Actors.SequenceEqual(fight.Actors);
        bool newFight = source?.Id != fight.Id;
        source = fight; actors = pendingActors;
        if (same) { UpdateState(); return; }
        if (newFight) { selected = null; plot.Select(null); following = true; ResetDetails(); }
        data = RotationData.Build(fight, actors); ModelBuilds++;
        if (selected != null) selected = data.Lanes.SelectMany(l => l.Hits).FirstOrDefault(h => h.ActorId == selected.ActorId && h.Sequence == selected.Sequence);
        WindowChanged(); UpdateDetails();
    }
    void WindowChanged()
    {
        double maximum = Math.Max(0, data.Seconds - Span);
        windowStart = following ? maximum : Math.Clamp(windowStart, 0, maximum);
        adjusting = true;
        try { scrub.Maximum = maximum; scrub.ViewportSize = Span; scrub.LargeChange = Span * .8; scrub.Value = windowStart; scrub.IsEnabled = maximum > 0; }
        finally { adjusting = false; }
        plot.Update(data, windowStart, Span); plot.Select(selected); UpdateTable(); UpdateState();
    }
    internal void SetHover(bool value) { hovering = value; if (!Held) ApplyPending(); UpdateState(); }
    internal void InspectAt(Point point, bool pin)
    {
        var hit = plot.At(point);
        if (hit != null && (!pinned || pin)) { hovering = true; InspectHit(hit, pin); }
        else if (hit == null && !pinned && selected != null) { selected = null; plot.Select(null); ResetDetails(); UpdateTable(); }
    }
    void InspectHit(RotationHit hit, bool pin)
    {
        if (selected == hit && (!pin || pinned)) return;
        bool laneChanged = selected?.ActorId != hit.ActorId;
        selected = hit; pinned |= pin; if (pin) following = false; plot.Select(hit); UpdateDetails();
        if (laneChanged) UpdateTable(); UpdateState();
    }
    internal void Step(int direction)
    {
        if (selected == null) return;
        var lane = data.Lanes.First(l => l.Actor.Id == selected.ActorId);
        int index = Array.IndexOf(lane.Hits, selected), target = Math.Clamp(index + direction, 0, lane.Hits.Length - 1);
        selected = lane.Hits[target]; pinned = true; following = false;
        if (selected.Seconds < windowStart || selected.Seconds > windowStart + Span) { windowStart = selected.Seconds - Span / 2; WindowChanged(); }
        plot.Select(selected); UpdateDetails(); UpdateState();
    }
    internal void Seek(double start) { following = false; paused = true; windowStart = start; WindowChanged(); }
    double ZoomAnchor => selected != null && selected.Seconds >= windowStart && selected.Seconds <= windowStart + Span
        ? (selected.Seconds - windowStart) / Span : .5;
    internal void SetZoom(int index) => zoom.SelectedIndex = Math.Clamp(index, 0, ZoomSeconds.Length);
    internal void ZoomAt(double factor, double fraction) => ChangeZoom(Span * factor, fraction);
    void ChangeZoom(double seconds, double fraction, bool fit = false)
    {
        if (!double.IsFinite(seconds) || !double.IsFinite(fraction)) return;
        fraction = Math.Clamp(fraction, 0, 1);
        double anchor = windowStart + Span * fraction;
        wholeFight = fit; windowSeconds = Math.Clamp(seconds, 1, Math.Max(60, data.Seconds));
        windowStart = fit ? 0 : anchor - Span * fraction;
        following = false; paused = true;
        adjustingZoom = true;
        try {
            int preset = Array.FindIndex(ZoomSeconds, value => Math.Abs(value - Span) < .000001);
            zoom.SelectedIndex = fit ? ZoomSeconds.Length : preset >= 0 ? preset : ZoomSeconds.Length + 1;
        }
        finally { adjustingZoom = false; }
        WindowChanged();
    }
    internal void Resume()
    {
        hovering = pinned = paused = false; following = true; selected = null; plot.Select(null); ResetDetails();
        ApplyPending(); WindowChanged();
    }
    void ResetDetails() { title.Text = "Point anywhere in a player's damage or healing lane"; detail.Text = "Details appear immediately. Click to pin, then use ◀ / ▶ or the arrow keys to step through events."; icon.Source = SkillIcons.Placeholder; previous.IsEnabled = next.IsEnabled = false; }
    void UpdateDetails()
    {
        if (selected == null || source == null) { ResetDetails(); return; }
        var hit = selected; title.Text = $"{skillName(hit.SkillCode)}  ·  {hit.Amount:N0} {(hit.Healing ? "healing" : "damage")}";
        detail.Text = $"{source.Name(hit.ActorId)} → {source.Name(hit.TargetId)}\n{RotationPlot.Time(hit.Seconds)} into fight · {(hit.Critical ? "Critical · " : "")}skill {hit.SkillCode}";
        icon.Source = skillIcon(hit.SkillCode) ?? SkillIcons.Placeholder;
        var lane = data.Lanes.First(l => l.Actor.Id == hit.ActorId); int index = Array.IndexOf(lane.Hits, hit);
        previous.IsEnabled = index > 0; next.IsEnabled = index < lane.Hits.Length - 1;
    }
    void UpdateTable()
    {
        if (source == null) return;
        var visible = data.Lanes.Where(l => selected == null || l.Actor.Id == selected.ActorId)
            .SelectMany(l => l.Hits.Skip(RotationLane.LowerBound(l.Hits, windowStart)).TakeWhile(h => h.Seconds <= windowStart + Span))
            .OrderBy(h => h.Seconds).ThenBy(h => h.Sequence).ToArray();
        var rows = visible.TakeLast(400).Select(h => new RotationRow(h.ActorId, source.Name(h.ActorId), h.Seconds, h.Healing ? "Heal" : "Damage", skillName(h.SkillCode), h.SkillCode,
            source.Name(h.TargetId), h.Amount, h.Critical) { Hit = h }).ToArray();
        updatingTable = true;
        try {
            if (table is LiveTable liveTable) liveTable.SetRows(rows);
            else if (table.ItemsSource == null || !table.ItemsSource.Cast<RotationRow>().SequenceEqual(rows)) table.ItemsSource = rows;
        }
        finally { updatingTable = false; }
        tableSummary.Text = $"{(selected == null ? "Party" : source.Name(selected.ActorId))} · {visible.Length:N0} events in view" + (visible.Length > 400 ? " · table shows latest 400; zoom for earlier events" : " · select a row to pin its details");
    }
    void UpdateState()
    {
        state.Text = $"{RotationPlot.Time(windowStart)} – {RotationPlot.Time(windowStart + Span)} · " +
            (pinned ? "Pinned · click another event or Resume" : paused ? "View held" : hovering ? "View held under pointer" : following ? "Following latest" : "Inspecting history") +
            (Held && context.StartsWith("live", StringComparison.Ordinal) ? " · capture continues" : "") +
            (pending != null && source != null && pending.Id != source.Id ? " · new fight ready; Resume to follow" : "") +
            "\nCtrl + wheel: zoom at pointer · + / −: zoom · scroll normally for players · upper marks: damage · lower green marks: healing";
        hold.Content = pinned || paused ? "Resume" : "Hold view";
    }
}
