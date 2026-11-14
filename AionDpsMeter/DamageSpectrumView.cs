using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AionDpsMeter.Local;

internal sealed class DamageSpectrumView : Grid
{
    sealed record Target(int? Id, string Name) { public override string ToString() => Name; }
    sealed record PhaseChoice(SpectrumPhase? Phase, string Name) { public override string ToString() => Name; }
    readonly ComboBox targets = new() { MinWidth = 150, MaxWidth = 240, Margin = new(0, 0, 12, 0) };
    readonly ComboBox intervals = new() { Width = 85, Margin = new(0, 0, 12, 0), ItemsSource = new[] { "1 s", "3 s", "5 s", "10 s" }, SelectedIndex = 1 };
    readonly ComboBox phases = new() { MinWidth = 160, MaxWidth = 270, Margin = new(0, 0, 12, 0) };
    readonly ComboBox compare = new() { MinWidth = 160, MaxWidth = 270, Margin = new(0, 0, 12, 0) };
    SpectrumData? reference;
    readonly SpectrumPlot plot = new();
    Dictionary<int, SpectrumPoint[]> trends = new();
    double windowSeconds = 3;
    readonly StackPanel players = new();
    readonly TextBlock summary = Label("", 12), readoutTitle = Label("", 12), phaseHint = Label("", 11);
    readonly Dictionary<int, TextBlock> values = new();
    readonly HashSet<int> hidden = new();
    readonly Dictionary<int, Brush> colors = new();
    Fight? fight;
    EncounterParticipant[] people = [];
    SpectrumData data = new(0, 1, [], []);
    bool updating;
    double from;
    double? to;
    int? hover;
    string peopleKey = "";
    static readonly string[] Palette = ["#61DCC0", "#B99AFF", "#F2B96A", "#68B9F4", "#F17FA5", "#D7DE75", "#85D5EC", "#E5A2DC"];
    Window? expanded;
    internal DamageSpectrumView(bool allowExpand = true)
    {
        Margin = new(0, 12, 0, 0);
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var top = new StackPanel(); Children.Add(top);

        var hint = Label("Compare players at the same moment. Drag to zoom; click to pin a moment; double-click to reset.", 12);
        hint.Margin = new(0, 6, 0, 16); hint.TextWrapping = TextWrapping.Wrap; top.Children.Add(hint);
        var controls = new WrapPanel { Margin = new(0, 0, 0, 10) }; top.Children.Add(controls);
        void Field(string text, Control control) { var panel = new StackPanel(); var label = Label(text, 10); label.Margin = new(0, 0, 0, 5); panel.Children.Add(label); panel.Children.Add(control); controls.Children.Add(panel); }
        Field("Damage target", targets); Field("DPS window", intervals); Field("Recorded phase", phases); Field("Compare with", compare);
        var reset = new Button { Content = Localization.T("Full fight"), Padding = new(14, 7, 14, 7), VerticalAlignment = VerticalAlignment.Bottom };
        reset.Click += (_, _) => Reset(); controls.Children.Add(reset);
        if (allowExpand) {
            var expand = new Button { Content = Localization.T("Expand graph"), Padding = new(14, 7, 14, 7), Margin = new(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom };
            expand.Click += (_, _) => OpenExpanded(); controls.Children.Add(expand);
        }
        summary.Margin = new(0, 0, 0, 5); top.Children.Add(summary);
        phaseHint.Margin = new(0, 0, 0, 12); top.Children.Add(phaseHint);
        var layout = new Grid(); layout.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new() { Width = new GridLength(250) }); SetRow(layout, 1); Children.Add(layout);
        var chartCard = UiTheme.Card(plot, new Thickness(14)); chartCard.Margin = new(0, 0, 12, 0); layout.Children.Add(chartCard);
        var side = new DockPanel();
        var sideHead = new StackPanel(); DockPanel.SetDock(sideHead, Dock.Top); side.Children.Add(sideHead);
        readoutTitle.FontWeight = FontWeights.SemiBold; readoutTitle.TextWrapping = TextWrapping.Wrap; sideHead.Children.Add(readoutTitle);
        var buttons = new WrapPanel { Margin = new(0, 10, 0, 12) }; sideHead.Children.Add(buttons);
        foreach (string action in new[] { "All players", "Only me" }) {
            var button = new Button { Content = Localization.T(action), Padding = new(8, 4, 8, 4), Margin = new(0, 0, 6, 0) };
            button.Click += (_, _) => { hidden.Clear(); if (action == "Only me") hidden.UnionWith(people.Where(p => p.Id != fight?.LocalPlayer).Select(p => p.Id)); BuildPlayers(); Draw(); };
            buttons.Children.Add(button);
        }
        side.Children.Add(new ScrollViewer { Content = players, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var sideCard = UiTheme.Card(side, new Thickness(14)); SetColumn(sideCard, 1); layout.Children.Add(sideCard);
        targets.SelectionChanged += (_, _) => { if (!updating) { from = 0; to = null; updating = true; phases.SelectedIndex = 0; compare.SelectedIndex = 0; updating = false; UpdatePhases(); Rebuild(); } };
        intervals.SelectionChanged += (_, _) => { if (!updating) Rebuild(); };
        phases.SelectionChanged += (_, _) => { if (updating) return; var phase = (phases.SelectedItem as PhaseChoice)?.Phase; from = phase?.From ?? 0; to = phase?.To; Rebuild(); };
        compare.SelectionChanged += (_, _) => { if (!updating) Rebuild(); };
        plot.Hover += index => { hover = index; UpdateReadout(); };
        plot.Range += (a, b) => { from = a; to = b; updating = true; phases.SelectedIndex = 0; updating = false; Rebuild(); };
        plot.Reset += Reset;
        IsVisibleChanged += (_, _) => { if (IsVisible) Rebuild(); };
    }
    internal Window? OpenExpanded(bool offscreen = false)
    {
        if (fight == null) return null;
        if (expanded != null) { expanded.Activate(); return expanded; }
        var owner = Window.GetWindow(this);
        var view = new DamageSpectrumView(false) { Margin = new(24) };
        var window = new Window { Title = Localization.T("Damage spectrum") + " / " + EncounterOverview.Title(fight), Content = view,
            Owner = owner, Width = 1280, Height = 820, MinWidth = 900, MinHeight = 600,
            Background = owner?.Background ?? UiTheme.Brush("#0C1017"), Foreground = UiTheme.Brush("#E7EDF7"),
            WindowStartupLocation = offscreen ? WindowStartupLocation.Manual : WindowStartupLocation.CenterOwner };
        if (owner != null) window.Resources = owner.Resources;
        if (offscreen) { window.Left = -32000; window.Top = -32000; window.ShowActivated = false; }
        expanded = window; window.Closed += (_, _) => expanded = null;
        view.Update(fight, people);
        view.updating = true;
        view.targets.SelectedItem = view.targets.Items.Cast<Target>().FirstOrDefault(t => t.Id == (targets.SelectedItem as Target)?.Id);
        view.intervals.SelectedIndex = intervals.SelectedIndex;
        view.updating = false; view.UpdatePhases();
        view.hidden.UnionWith(hidden); view.BuildPlayers(); view.from = from; view.to = to;
        view.updating = true; view.phases.SelectedIndex = phases.SelectedIndex; view.compare.SelectedIndex = compare.SelectedIndex; view.updating = false;
        window.Show(); view.Rebuild(); return window;
    }
    internal void CheckPresentation()
    {
        if (fight == null || data.Series.Length < 2 || plot.ActualWidth < 400 || plot.ActualHeight < 240)
            throw new InvalidOperationException("Spectrum did not render a full-size multi-player graph.");
        var original = fight;
        int id = people[0].Id;
        hidden.Add(id); BuildPlayers(); Draw();
        Update(original.Copy(), people);
        if (!hidden.Contains(id)) throw new InvalidOperationException("Spectrum lost player selection on refresh.");
        if (phases.Items.Count > 2) {
            phases.SelectedIndex = 1; compare.SelectedIndex = 2;
            if (reference == null || data.From != ((PhaseChoice)phases.SelectedItem).Phase!.From)
                throw new InvalidOperationException("Spectrum phase comparison did not select the requested ranges.");
            Update(original.Copy(), people);
            if (reference == null || phases.SelectedIndex != 1 || compare.SelectedIndex != 2)
                throw new InvalidOperationException("Spectrum phase selections were lost on live refresh.");
        }
        hidden.Clear(); BuildPlayers(); compare.SelectedIndex = 0; Reset();
    }
    static TextBlock Label(string text, double size) => new() { Text = Localization.T(text), FontSize = size, Foreground = UiTheme.Brush("#B8C6D8") };
    internal void Update(Fight value, EncounterParticipant[] participants)
    {
        bool changed = fight?.Id != value.Id;
        bool same = ReferenceEquals(fight, value);
        fight = value; people = participants;
        if (changed) { hidden.Clear(); from = 0; to = null; peopleKey = ""; updating = true; phases.SelectedIndex = 0; compare.SelectedIndex = 0; updating = false; }
        var choices = new[] { new Target(null, Localization.T("All targets")) }.Concat(value.Actors.Values.Where(CapturedBoss.IsBoss).OrderBy(a => a.Id).Select(a => new Target(a.Id, a.Name))).ToArray();
        if (!targets.IsDropDownOpen && !targets.Items.Cast<Target>().SequenceEqual(choices)) {
            int? prior = (targets.SelectedItem as Target)?.Id;
            updating = true; targets.ItemsSource = choices;
            targets.SelectedItem = changed ? choices.Skip(1).FirstOrDefault() ?? choices[0] : choices.FirstOrDefault(c => c.Id == prior) ?? choices[0];
            updating = false;
        }
        string key = string.Join("|", people.OrderBy(p => p.Id).Select(p => $"{p.Id}:{p.Participant}:{p.Class}"));
        if (peopleKey != key) { peopleKey = key; BuildPlayers(); }
        if (!same || changed) { UpdatePhases(); if (IsVisible) Rebuild(); }
        if (expanded?.Content is DamageSpectrumView expandedView) {
            expanded.Title = Localization.T("Damage spectrum") + " / " + EncounterOverview.Title(value);
            expandedView.Update(value, participants);
        }
    }
    void UpdatePhases()
    {
        if (fight == null || phases.IsDropDownOpen || compare.IsDropDownOpen) return;
        var captured = DamageSpectrum.Phases(fight, (targets.SelectedItem as Target)?.Id);
        var choices = new[] { new PhaseChoice(null, Localization.T("Full fight")) }.Concat(captured.Select(p => new PhaseChoice(p, $"{Localization.T("Phase")} {p.Phase}  /  {MeterPresentation.Duration(p.From)} - {MeterPresentation.Duration(p.To)}"))).ToArray();
        phaseHint.Text = Localization.T(captured.Length == 0 ? "No recorded phase boundaries for this target. Drag across the graph to compare a time range." : "Phase bands show observed server phase changes, not inferred HP thresholds.");
        if (phases.Items.Cast<PhaseChoice>().SequenceEqual(choices)) return;
        var prior = (phases.SelectedItem as PhaseChoice)?.Phase;
        updating = true; phases.ItemsSource = choices; phases.SelectedItem = choices.FirstOrDefault(p => p.Phase?.From == prior?.From && p.Phase?.Phase == prior?.Phase) ?? choices[0]; var priorComparison = (compare.SelectedItem as PhaseChoice)?.Phase;
        var comparisons = new[] { new PhaseChoice(null, Localization.T("No comparison")) }.Concat(choices.Skip(1)).ToArray();
        compare.ItemsSource = comparisons; compare.SelectedItem = comparisons.FirstOrDefault(p => p.Phase?.From == priorComparison?.From && p.Phase?.Phase == priorComparison?.Phase) ?? comparisons[0];
        updating = false;
    }
    void BuildPlayers()
    {
        players.Children.Clear(); values.Clear(); colors.Clear(); int index = 0;
        foreach (var person in people.OrderBy(p => p.Id)) {
            var color = UiTheme.Brush(Palette[index++ % Palette.Length]); colors[person.Id] = color;
            var check = new CheckBox { Content = person.Participant, IsChecked = !hidden.Contains(person.Id), Foreground = color, FontSize = 13, FontWeight = FontWeights.SemiBold, ToolTip = person.Class };
            var value = Label("", 11); value.Margin = new(23, 7, 0, 0); value.TextWrapping = TextWrapping.Wrap; values[person.Id] = value;
            var stack = new StackPanel { Margin = new(0, 0, 0, 12) }; stack.Children.Add(check); stack.Children.Add(value); players.Children.Add(stack);
            stack.MouseEnter += (_, _) => plot.Highlight(person.Id); stack.MouseLeave += (_, _) => plot.Highlight(null);
            check.Click += (_, _) => { if (check.IsChecked == true) hidden.Remove(person.Id); else hidden.Add(person.Id); Draw(); };
        }
    }
    void Reset() { from = 0; to = null; updating = true; phases.SelectedIndex = 0; updating = false; Rebuild(); }
    void Rebuild()
    {
        if (fight == null) return;
        windowSeconds = new[] { 1d, 3d, 5d, 10d }[Math.Max(0, intervals.SelectedIndex)];
        const double interval = 1;
        data = DamageSpectrum.Build(fight, people, (targets.SelectedItem as Target)?.Id, interval, from, to);
        reference = (compare.SelectedItem as PhaseChoice)?.Phase is { } baseline ? DamageSpectrum.Build(fight, people, (targets.SelectedItem as Target)?.Id, interval, baseline.From, baseline.To) : null;
        trends = data.Series.ToDictionary(s => s.Id, s => DamageSpectrum.Trend(s.Buckets, windowSeconds));
        hover = null; Draw();
    }
    void Draw()
    {
        plot.Update(data, hidden, colors, trends);
        var visible = data.Series.Where(s => !hidden.Contains(s.Id)).ToArray();
        long damage = visible.Sum(s => s.Buckets.Sum(b => b.Damage));
        double seconds = data.To - data.From;
        double bin = data.Series.FirstOrDefault()?.Buckets.FirstOrDefault() is { } b ? b.To - b.From : 0;
        summary.Text = $"{MeterPresentation.Duration(data.From)} - {MeterPresentation.Duration(data.To)}   /   {MeterPresentation.Number(damage)} {Localization.T("damage")}   /   {MeterPresentation.Number(damage / seconds)} {Localization.T("average DPS")}   /   {Math.Max(windowSeconds, bin):0.##} s {Localization.T("rolling DPS")}";
        UpdateReadout();
    }
    void UpdateReadout()
    {
        var sample = trends.Values.FirstOrDefault();
        int? at = hover is int h && sample != null && h >= 0 && h < sample.Length ? h : null;
        readoutTitle.Text = at is int i ? $"{MeterPresentation.Duration(sample![i].From)} - {MeterPresentation.Duration(sample[i].To)}\n{Localization.T("DPS at cursor")}" : Localization.T("Selected range / average DPS");
        foreach (var series in data.Series) {
            long total = series.Buckets.Sum(b => b.Damage);
            double dps = at is int n ? trends[series.Id][n].Dps : total / (data.To - data.From);
            if (values.TryGetValue(series.Id, out var label)) {
                label.Opacity = hidden.Contains(series.Id) ? .35 : 1;
                label.Text = $"{dps:N0} DPS / {MeterPresentation.Number(total)}";
                label.ToolTip = Localization.T("Selected range / average DPS") + " / " + Localization.T("range damage");
                if (reference?.Series.FirstOrDefault(s => s.Id == series.Id) is { } other) {
                    double average = total / (data.To - data.From), against = other.Buckets.Sum(b => b.Damage) / (reference.To - reference.From);
                    string delta = against > 0 ? $"{(average / against - 1) * 100:+0.0;-0.0;0}%" : "--";
                    label.Text += $"\n{Localization.T("Reference")}: {against:N0} DPS\n{Localization.T("Range difference")}: {delta}";
                }
            }
        }
    }
}

internal sealed class SpectrumPlot : FrameworkElement
{
    SpectrumData data = new(0, 1, [], []);
    SpectrumSeries[] visible = [];
    Dictionary<int, Brush> colors = new();
    Dictionary<int, SpectrumPoint[]> trends = new();
    int? highlighted;
    internal void Highlight(int? id) { highlighted = id; InvalidateVisual(); }
    int? hover;
    double? drag;
    double pointer;
    bool pinned;
    public event Action<int?>? Hover;
    public event Action<double, double>? Range;
    public event Action? Reset;
    Rect Area => new(58, 34, Math.Max(1, ActualWidth - 76), Math.Max(1, ActualHeight - 76));
    double Time(double x) => data.From + Math.Clamp((x - Area.Left) / Area.Width, 0, 1) * (data.To - data.From);
    internal SpectrumPlot()
    {
        MinHeight = 240; ClipToBounds = true; Cursor = Cursors.Cross; Focusable = true;
        MouseMove += (_, e) => { pointer = e.GetPosition(this).X; if (!pinned || drag != null) SetHover(pointer); InvalidateVisual(); };
        MouseLeave += (_, _) => { if (!pinned && drag == null) { hover = null; Hover?.Invoke(null); InvalidateVisual(); } };
        MouseLeftButtonDown += (_, e) => { Focus(); if (e.ClickCount == 2) { Reset?.Invoke(); return; } drag = e.GetPosition(this).X; pointer = drag.Value; CaptureMouse(); e.Handled = true; };
        MouseLeftButtonUp += (_, e) => {
            if (drag is not double start) return;
            double end = e.GetPosition(this).X; drag = null; ReleaseMouseCapture();
            double a = Time(Math.Min(start, end)), b = Time(Math.Max(start, end));
            if (Math.Abs(start - end) > 8 && b - a >= .25) { pinned = false; Range?.Invoke(a, b); }
            else { pinned = !pinned; SetHover(end); }
            InvalidateVisual();
        };
        LostMouseCapture += (_, _) => { drag = null; InvalidateVisual(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { pinned = false; hover = null; Hover?.Invoke(null); InvalidateVisual(); } };
    }
    void SetHover(double x)
    {
        var bins = data.Series.FirstOrDefault()?.Buckets;
        int? next = bins == null ? null : Math.Clamp((int)Math.Round((Time(x) - data.From) / Math.Max(.001, bins[0].To - bins[0].From)) - 1, 0, bins.Length - 1);
        if (hover != next) { hover = next; Hover?.Invoke(hover); }
    }
    internal void Update(SpectrumData value, HashSet<int> hidden, Dictionary<int, Brush> palette, Dictionary<int, SpectrumPoint[]> samples)
    {
        bool newData = !ReferenceEquals(data, value);
        double? pinnedTime = pinned && hover is int old && data.Series.FirstOrDefault()?.Buckets is { } oldBins && old < oldBins.Length ? oldBins[old].To : null;
        data = value; trends = samples;
        visible = value.Series.Where(s => !hidden.Contains(s.Id)).ToArray(); colors = new(palette);
        if (newData) { hover = null; pinned = false;
            if (pinnedTime is double time && time >= data.From && time <= data.To) { pinned = true; SetHover(Area.Left + (time - data.From) / (data.To - data.From) * Area.Width); }
        } InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize)); var a = Area;
        double maximum = Math.Max(1, visible.SelectMany(s => trends[s.Id]).Select(b => b.Dps).DefaultIfEmpty().Max() * 1.12);
        double X(double seconds) => a.Left + (seconds - data.From) / (data.To - data.From) * a.Width;
        double Y(double dps) => a.Bottom - dps / maximum * a.Height;
        void Text(string text, double x, double y, Brush? brush = null, double size = 11) => dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush ?? UiTheme.Brush("#93A7BF"), VisualTreeHelper.GetDpi(this).PixelsPerDip), new(x, y));
        foreach (var phase in data.Phases.Where(p => p.To > data.From && p.From < data.To)) {
            double left = X(Math.Max(data.From, phase.From)), right = X(Math.Min(data.To, phase.To));
            dc.DrawRectangle(UiTheme.Brush(phase.Phase % 2 == 0 ? "#105BA9DD" : "#1063D9BF"), null, new Rect(left, a.Top, right - left, a.Height));
            dc.DrawLine(new Pen(UiTheme.Brush("#5063D9BF"), 1) { DashStyle = DashStyles.Dash }, new(left, a.Top), new(left, a.Bottom));
            if (right - left > 75) Text($"{Localization.T("Phase")} {phase.Phase}", left + 6, 9, UiTheme.Brush("#78D9C3"));
        }
        var grid = new Pen(UiTheme.Brush("#243347"), 1);
        for (int i = 0; i <= 4; i++) { double y = a.Bottom - a.Height * i / 4; dc.DrawLine(grid, new(a.Left, y), new(a.Right, y)); Text(MeterPresentation.Number(maximum * i / 4), 2, y - 7); }
        int ticks = Math.Max(2, (int)(a.Width / 110));
        for (int i = 0; i <= ticks; i++) { double x = a.Left + a.Width * i / ticks; Text(MeterPresentation.Duration(data.From + (data.To - data.From) * i / ticks), x - 15, a.Bottom + 12); }
        Text("DPS", 2, 9);
        dc.PushClip(new RectangleGeometry(a));
        foreach (var series in visible) {
            var line = new StreamGeometry(); var points = trends[series.Id];
            using (var path = line.Open()) {
                // Straight segments join measured window samples; no spline can
                // overshoot a peak or invent negative damage between samples.
                path.BeginFigure(new(X(points[0].To), Y(points[0].Dps)), false, false);
                for (int i = 1; i < points.Length; i++) path.LineTo(new(X(points[i].To), Y(points[i].Dps)), true, false);
            }
            var brush = colors[series.Id];
            dc.PushOpacity(highlighted == null || highlighted == series.Id ? 1 : .18);
            dc.DrawGeometry(null, new Pen(brush, highlighted == series.Id ? 2.8 : 1.8) { LineJoin = PenLineJoin.Round }, line);
            if (points.Length == 1) dc.DrawEllipse(brush, null, new(X(points[0].To), Y(points[0].Dps)), 3, 3);
            dc.Pop();
        }
        if (hover is int h && data.Series.FirstOrDefault()?.Buckets is { } buckets && h < buckets.Length) {
            double x = X(buckets[h].To);
            dc.DrawLine(new Pen(UiTheme.Brush("#D8E6F4"), 1) { DashStyle = DashStyles.Dash }, new(x, a.Top), new(x, a.Bottom));
            foreach (var s in visible) dc.DrawEllipse(colors[s.Id], new Pen(UiTheme.Brush("#102030"), 2), new(x, Y(trends[s.Id][h].Dps)), 4, 4);
        }
        if (drag is double start) { double left = Math.Clamp(Math.Min(start, pointer), a.Left, a.Right), right = Math.Clamp(Math.Max(start, pointer), a.Left, a.Right); dc.DrawRectangle(UiTheme.Brush("#3061DCC0"), new Pen(UiTheme.Brush("#61DCC0"), 1), new Rect(left, a.Top, right - left, a.Height)); }
        dc.Pop();
        if (visible.Length == 0) Text(Localization.T("Select players to compare their damage."), a.Left + 20, a.Top + 35);
    }
}
