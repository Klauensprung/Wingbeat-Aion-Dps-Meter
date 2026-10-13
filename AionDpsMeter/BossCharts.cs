using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AionDpsMeter.Local;

internal static class BossDrawing
{
    internal static readonly Brush Text = UiTheme.Brush("#E7EDF7"), Muted = UiTheme.Brush("#93A2B8"),
        Cast = UiTheme.Brush("#55D6BE"), Hit = UiTheme.Brush("#F18B91"), Gold = UiTheme.Brush("#E7CF7D"), Blue = UiTheme.Brush("#7C9EF7"),
        Surface = UiTheme.Brush("#121B28"), Alternate = UiTheme.Brush("#182231");
    internal static void Label(DrawingContext dc, string text, double x, double y, Brush? brush = null, double width = 180, double size = 11)
    {
        var label = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush ?? Text, 1) {
            MaxTextWidth = Math.Max(1, width), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        dc.DrawText(label, new(x, y));
    }
    internal static Brush Colour(string kind) => kind switch { "Cast signal" => Cast, "Hit" => Hit, "Enrage" => Gold, "Defense" => Blue, _ => Text };
    internal static string Time(double seconds) => $"{(int)seconds / 60:00}:{seconds % 60:00.0}";
}

internal sealed class BossTrendPlot : FrameworkElement
{
    BossEncounterData? data;
    const double Left = 145;
    internal BossTrendPlot() { MinHeight = 165; MinWidth = 340; ClipToBounds = true; SizeChanged += (_, _) => InvalidateVisual(); }
    internal void Update(BossEncounterData? value) { data = value; InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(BossDrawing.Surface, null, new(0, 0, ActualWidth, ActualHeight));
        if (data == null) return;
        double width = Math.Max(1, ActualWidth - Left - 12), seconds = data.Seconds, rowHeight = Math.Max(45, (ActualHeight - 30) / 3), barHeight = rowHeight - 22;
        void Bars(string title, double y, Func<BossDamageBin, long> amount, Brush colour) {
            double maximum = Math.Max(1, data.Bins.Max(b => amount(b) / b.Width));
            BossDrawing.Label(dc, title, 10, y, colour, Left - 20);
            BossDrawing.Label(dc, "Peak " + MeterPresentation.Number(maximum) + "/s", 10, y + 20, BossDrawing.Muted, Left - 20);
            foreach (var bin in data.Bins) {
                double height = amount(bin) / bin.Width / maximum * barHeight;
                if (height > 0) dc.DrawRectangle(colour, null, new(Left + bin.Seconds / seconds * width, y + rowHeight - 12 - height, Math.Max(1, bin.Width / seconds * width - 1), height));
            }
            dc.DrawLine(new Pen(BossDrawing.Muted, .3), new(Left, y + rowHeight - 11), new(Left + width, y + rowHeight - 11));
        }
        Bars("Damage to boss", 10, b => b.ToBoss, BossDrawing.Cast);
        Bars("Damage from boss", 10 + rowHeight, b => b.FromBoss, BossDrawing.Hit);
        double hpTop = 10 + 2 * rowHeight;
        BossDrawing.Label(dc, "Recorded HP", 10, hpTop, BossDrawing.Gold, Left - 20);
        if (data.Hp.Length == 0) BossDrawing.Label(dc, "No HP packets captured", Left, hpTop + 22, BossDrawing.Muted, width);
        else {
            double max = Math.Max(1, data.Hp.Max(p => p.Value));
            BossDrawing.Label(dc, "Max seen " + MeterPresentation.Number(max), 10, hpTop + 20, BossDrawing.Muted, Left - 20);
            Point? previous = null;
            foreach (var hp in data.Hp) {
                var point = new Point(Left + (hp.Timestamp - data.Start) / 1000d / seconds * width, hpTop + rowHeight - 12 - hp.Value / max * barHeight);
                if (previous is Point last) {
                    dc.DrawLine(new Pen(BossDrawing.Gold, 1.5), last, new(point.X, last.Y));
                    dc.DrawLine(new Pen(BossDrawing.Gold, 1.5), new(point.X, last.Y), point);
                }
                dc.DrawEllipse(BossDrawing.Gold, null, point, 2, 2); previous = point;
            }
        }
        BossDrawing.Label(dc, "00:00", Left, ActualHeight - 18, BossDrawing.Muted, 70);
        BossDrawing.Label(dc, BossDrawing.Time(seconds), ActualWidth - 65, ActualHeight - 18, BossDrawing.Muted, 65);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); if (data == null) return;
        var p = e.GetPosition(this); double t = Math.Clamp((p.X - Left) / Math.Max(1, ActualWidth - Left - 12), 0, 1) * data.Seconds;
        if (p.X < Left) { ToolTip = "Damage charts use separate vertical scales. HP is an absolute reading, not a percentage."; return; }
        var bin = data.Bins.LastOrDefault(b => b.Seconds <= t) ?? data.Bins[0];
        var hp = data.Hp.LastOrDefault(h => (h.Timestamp - data.Start) / 1000d <= t);
        ToolTip = $"{BossDrawing.Time(bin.Seconds)} - {BossDrawing.Time(bin.Seconds + bin.Width)}\nTo boss: {bin.ToBoss:N0} ({bin.ToBoss / bin.Width:N1}/s)\nFrom boss: {bin.FromBoss:N0} ({bin.FromBoss / bin.Width:N1}/s)\n" +
            (hp == null ? "No earlier HP reading" : $"Last HP: {hp.Value:N0} at {BossDrawing.Time((hp.Timestamp - data.Start) / 1000d)}");
    }
}

internal sealed class BossTimelinePlot : FrameworkElement
{
    internal const double LabelWidth = 190, RowHeight = 34, Axis = 26;
    BossEncounterData? data;
    (int Skill, string Name, BossMoment[] Events)[] lanes = [];
    double start, span = 1;
    BossMoment? selected;
    DrawingGroup? scene;
    internal int SceneBuilds { get; private set; }
    internal event Action<BossMoment, bool>? Inspect;
    internal event Action<bool>? Hover;
    internal event Action<double, double>? Zoom;
    internal BossTimelinePlot() {
        ClipToBounds = true; MinWidth = 340; Height = 80; Cursor = Cursors.Cross;
        SizeChanged += (_, _) => ClearScene();
        MouseEnter += (_, _) => Hover?.Invoke(true); MouseLeave += (_, _) => Hover?.Invoke(false);
    }
    internal void Update(BossEncounterData value, int? skill, double offset, double seconds)
    {
        if (ReferenceEquals(value, data) && skill == filter && offset == start && Math.Max(1, seconds) == span) return;
        if (!ReferenceEquals(value, data) || skill != filter) {
            data = value; filter = skill;
            lanes = value.Abilities.Where(a => skill == null || a.SkillCode == skill).OrderBy(a => a.SkillCode)
                .Select(a => (a.SkillCode, a.Skill, value.Moments.Where(m => m.SkillCode == a.SkillCode && m.Kind is not ("Enrage" or "Boss death")).ToArray())).ToArray();
            var markers = value.Moments.Where(m => m.Kind is "Enrage" or "Boss death").ToArray();
            if (markers.Length > 0) lanes = [..lanes, (-1, "Encounter markers", markers)];
        }
        start = offset; span = Math.Max(1, seconds); Height = Math.Max(78, Axis + lanes.Length * RowHeight + 8); ClearScene();
    }
    int? filter;
    void ClearScene() { scene = null; InvalidateVisual(); }
    internal void Select(BossMoment? value) { if (selected == value) return; selected = value; InvalidateVisual(); }
    internal BossMoment? At(Point point)
    {
        double width = Math.Max(1, ActualWidth - LabelWidth - 12);
        if (point.X < LabelWidth || point.X > LabelWidth + width || point.Y < Axis) return null;
        int row = (int)((point.Y - Axis) / RowHeight); if (row >= lanes.Length || data == null) return null;
        var events = lanes[row].Events; long min = data.Start + (long)(start * 1000), max = min + (long)(span * 1000);
        int first = Lower(events, min), after = Lower(events, max + 1); if (first >= after) return null;
        long timestamp = min + (long)((point.X - LabelWidth) / width * span * 1000);
        int at = Math.Clamp(Lower(events, timestamp), first, after - 1);
        if (at > first && timestamp - events[at - 1].Timestamp <= events[at].Timestamp - timestamp) at--;
        return events[at];
    }
    static int Lower(BossMoment[] events, long time) {
        int low = 0, high = events.Length; while (low < high) { int mid = low + (high - low) / 2; if (events[mid].Timestamp < time) low = mid + 1; else high = mid; } return low;
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (At(e.GetPosition(this)) is { } hit) Inspect?.Invoke(hit, false); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { base.OnMouseLeftButtonDown(e); if (At(e.GetPosition(this)) is { } hit) { Inspect?.Invoke(hit, true); e.Handled = true; } }
    protected override void OnMouseWheel(MouseWheelEventArgs e) {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) { Zoom?.Invoke(e.Delta > 0 ? .8 : 1.25, Math.Clamp((e.GetPosition(this).X - LabelWidth) / Math.Max(1, ActualWidth - LabelWidth - 12), 0, 1)); e.Handled = true; }
        base.OnMouseWheel(e);
    }
    protected override void OnRender(DrawingContext dc)
    {
        if (scene == null) { scene = new(); using (var drawing = scene.Open()) DrawScene(drawing); scene.Freeze(); }
        dc.DrawDrawing(scene);
        if (selected != null && data != null) {
            double t = (selected.Timestamp - data.Start) / 1000d, width = Math.Max(1, ActualWidth - LabelWidth - 12);
            if (t >= start && t <= start + span) dc.DrawLine(new Pen(BossDrawing.Text, 1), new(LabelWidth + (t - start) / span * width, Axis), new(LabelWidth + (t - start) / span * width, ActualHeight));
        }
    }
    void DrawScene(DrawingContext dc)
    {
        SceneBuilds++;
        dc.DrawRectangle(BossDrawing.Surface, null, new(0, 0, ActualWidth, ActualHeight)); if (data == null) return;
        double width = Math.Max(1, ActualWidth - LabelWidth - 12);
        for (int i = 0; i < lanes.Length; i++) {
            double y = Axis + i * RowHeight;
            if (i % 2 == 1) dc.DrawRectangle(BossDrawing.Alternate, null, new(0, y, ActualWidth, RowHeight));
            BossDrawing.Label(dc, lanes[i].Name, 9, y + 9, width: LabelWidth - 18);
            int first = Lower(lanes[i].Events, data.Start + (long)(start * 1000));
            var pixels = new HashSet<(int, string)>();
            foreach (var m in lanes[i].Events.Skip(first)) {
                double seconds = (m.Timestamp - data.Start) / 1000d; if (seconds > start + span) break;
                int pixel = (int)((seconds - start) / span * width); if (!pixels.Add((pixel, m.Kind))) continue;
                double x = LabelWidth + pixel;
                if (m.Kind == "Cast signal") dc.DrawEllipse(null, new Pen(BossDrawing.Cast, 2), new(x, y + 10), 4, 4);
                else dc.DrawRectangle(BossDrawing.Colour(m.Kind), null, new(x - 1, y + 20, 3, 9));
            }
        }
        for (int i = 0; i <= 4; i++) {
            double x = LabelWidth + i * width / 4;
            dc.DrawLine(new Pen(BossDrawing.Muted, .25), new(x, Axis), new(x, ActualHeight));
            BossDrawing.Label(dc, BossDrawing.Time(start + i * span / 4), Math.Min(x, ActualWidth - 62), 5, BossDrawing.Muted, 62);
        }
        if (lanes.Length == 0) BossDrawing.Label(dc, "No boss attacks recorded yet", 12, 43, BossDrawing.Muted, 400);
    }
}

internal sealed class BossTimelineView : Grid
{
    readonly BossTimelinePlot plot = new();
    readonly TextBlock detail = new() { TextWrapping = TextWrapping.Wrap, MinHeight = 32, Margin = new(0, 4, 0, 4), FontSize = 11 };
    readonly TextBlock state = new() { Margin = new(0, 5, 0, 5), Foreground = BossDrawing.Muted, TextWrapping = TextWrapping.Wrap };
    readonly ScrollBar scrub = new() { Orientation = Orientation.Horizontal, Height = 16, Margin = new(190, 4, 12, 4) };
    readonly ComboBox zoom = new() { ItemsSource = new[] { "Whole boss", "10 seconds", "30 seconds", "60 seconds", "Custom zoom" }, SelectedIndex = 0, Width = 130, Margin = new(0, 0, 6, 0) };
    readonly Button previous, next;
    readonly Func<int, string> names;
    BossEncounterData? source, pending;
    Func<int, string> targetName = id => $"Entity {id}";
    Func<int, string>? pendingNames;
    BossMoment? selected;
    bool hover, pinned, adjusting;
    double offset, span = 1;
    bool fit = true;
    int? skill;
    BossMoment[] events = [];
    Dictionary<BossMoment, int> indices = new();
    internal BossTimelinePlot Plot => plot;
    internal BossMoment? Selected => selected;
    internal double Span => span;
    internal BossTimelineView(Func<int, string> names)
    {
        this.names = names;
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star), MinHeight = 72 });
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = GridLength.Auto });
        Button Action(string text, Action action) { var b = new Button { Content = text, Padding = new(10, 6, 10, 6), Margin = new(0, 0, 6, 0) }; b.Click += (_, _) => action(); return b; }
        var controls = new WrapPanel(); controls.Children.Add(zoom);
        controls.Children.Add(Action("Zoom in", () => Zoom(.5, .5))); controls.Children.Add(Action("Zoom out", () => Zoom(2, .5)));
        previous = Action("Previous", () => Step(-1)); next = Action("Next", () => Step(1)); controls.Children.Add(previous); controls.Children.Add(next);
        controls.Children.Add(Action("Resume", Resume)); Children.Add(controls); SetRow(state, 1); Children.Add(state);
        var viewport = SplitView.Scroll(plot); SetRow(viewport, 2); Children.Add(viewport);
        SetRow(scrub, 3); Children.Add(scrub);
        var inspection = new Border { Child = detail, Background = BossDrawing.Alternate, Padding = new(12, 4, 12, 4), CornerRadius = new(6) }; SetRow(inspection, 4); Children.Add(inspection);
        plot.Inspect += Inspect; plot.Hover += value => { hover = value; if (!hover && !pinned) ApplyPending(); State(); }; plot.Zoom += Zoom;
        zoom.SelectionChanged += (_, _) => { if (adjusting || zoom.SelectedIndex == 4) return; fit = zoom.SelectedIndex == 0; span = zoom.SelectedIndex switch { 1 => 10, 2 => 30, 3 => 60, _ => source?.Seconds ?? 1 }; offset = 0; Draw(); };
        scrub.ValueChanged += (_, _) => { if (!adjusting) { offset = scrub.Value; Draw(); } };
        State();
    }
    internal void Update(BossEncounterData data, Func<int, string> targets)
    {
        bool changed = source?.EntityId != data.EntityId || source.FightId != data.FightId;
        pending = data; pendingNames = targets;
        if (changed) { hover = pinned = false; selected = null; skill = null; offset = 0; fit = true; adjusting = true; zoom.SelectedIndex = 0; adjusting = false; }
        if (!hover && !pinned) ApplyPending();
    }
    void ApplyPending() { if (pending == null) return; source = pending; pending = null; targetName = pendingNames ?? targetName; selected = null; IndexEvents(); Draw(); }
    void IndexEvents() { events = source?.Moments.Where(m => skill == null || m.SkillCode == skill || m.Kind is "Enrage" or "Boss death").ToArray() ?? []; indices = events.Select((m, i) => (m, i)).ToDictionary(p => p.m, p => p.i); }
    internal void Filter(int? code) { skill = code; selected = null; pinned = false; ApplyPending(); IndexEvents(); Draw(); }
    internal void Resume() { hover = pinned = false; selected = null; ApplyPending(); Draw(); }
    internal void Zoom(double factor, double fraction)
    {
        if (source == null) return;
        double anchor = offset + span * fraction; fit = false; span = Math.Clamp(span * factor, 1, Math.Max(1, source.Seconds)); offset = anchor - span * fraction;
        adjusting = true; zoom.SelectedIndex = 4; adjusting = false; Draw();
    }
    internal void Inspect(BossMoment moment, bool pin) { if (pinned && !pin || selected == moment && (!pin || pinned)) return; selected = moment; pinned |= pin; Draw(); }
    internal void Step(int direction)
    {
        if (source == null) return;
        if (events.Length == 0) return;
        int index = selected == null ? -1 : indices.GetValueOrDefault(selected, -1);
        selected = events[Math.Clamp(index + direction, 0, events.Length - 1)]; pinned = true;
        double time = (selected.Timestamp - source.Start) / 1000d;
        if (time < offset || time > offset + span) offset = time - span / 2;
        Draw();
    }
    void Draw()
    {
        if (source == null) return;
        if (fit) span = source.Seconds;
        offset = Math.Clamp(offset, 0, Math.Max(0, source.Seconds - span));
        adjusting = true; scrub.Maximum = Math.Max(0, source.Seconds - span); scrub.ViewportSize = span; scrub.Value = offset; scrub.IsEnabled = scrub.Maximum > 0; adjusting = false;
        plot.Update(source, skill, offset, span); plot.Select(selected); State();
    }
    void State()
    {
        state.Text = (pinned ? "Pinned / Resume for live updates" : hover ? "Held while inspecting" : "Observed sequence") + "  |  Circle: cast / Red: hit / Blue: defense / Gold: enrage";
        int at = selected == null ? -1 : indices.GetValueOrDefault(selected, -1);
        previous.IsEnabled = at > 0; next.IsEnabled = events.Length > 0 && at < events.Length - 1;
        detail.Text = selected == null || source == null ? "Point at a skill lane to inspect. Click to pin; Previous / Next steps through simultaneous hits. Ctrl + wheel zooms. Cast signals do not confirm completed casts." :
            $"{BossDrawing.Time((selected.Timestamp - source.Start) / 1000d)}  /  {selected.Kind}  /  {(selected.SkillCode > 0 ? names(selected.SkillCode) + " [" + selected.SkillCode + "]" : selected.Detail)}\n" +
            (selected.TargetId > 0 ? $"Target: {targetName(selected.TargetId)} [Entity {selected.TargetId}]  /  " : "") +
            (selected.Kind == "Hit" ? $"{selected.Amount:N0} damage / " : "") + selected.Detail;
    }
}
