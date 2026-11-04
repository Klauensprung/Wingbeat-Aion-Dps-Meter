using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace AionDpsMeter.Local;

internal sealed class CooldownOverlayWindow : Window
{
    sealed record Tile(Border Container, Image Icon, TextBlock Time, ImageSource Color, ImageSource Gray, ProgressBar Progress, TextBlock Reduction, TextBlock Charges)
    {
        internal double MaximumRemaining { get; set; }
    }
    string lastStyle = "";
    internal string DisplayedCharges(int skill) => rows.TryGetValue(skill, out var row) ? row.Charges.Text : "";
    readonly DropShadowEffect readyGlow = new() { Color = Color.FromRgb(85, 214, 190), BlurRadius = 12, ShadowDepth = 0, Opacity = .9 };
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly WrapPanel list = new();
    readonly LiveEffectView effectView = new();
    readonly TrackedBuffView buffView = new();
    readonly LiveEffectTracker? effects;
    readonly Dictionary<int, Tile> rows = new();
    readonly TextBlock empty = new() { Text = "Right-click to choose skills", Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new(8) };
    readonly Preferences settings;
    readonly CooldownTracker tracker;
    readonly SkillNames names;
    readonly SkillIcons icons;
    readonly Func<bool> capturing;
    Action beginDrag;
    internal CooldownOverlayWindow(Preferences settings, CooldownTracker tracker, SkillNames names, SkillIcons icons, Func<bool> capturing, Action configure, Action save, LiveEffectTracker? effects = null)
    {
        this.settings = settings; this.tracker = tracker; this.names = names; this.icons = icons; this.capturing = capturing;
        this.effects = effects;
        readyGlow.Freeze();
        beginDrag = DragMove;
        Title = "Wingbeat cooldowns"; WindowStyle = WindowStyle.None; AllowsTransparency = true; ShowInTaskbar = false; Topmost = true;
        MinWidth = 64; MinHeight = 64;
        Width = double.IsFinite(settings.CooldownWidth) ? Math.Clamp(settings.CooldownWidth, 64, 700) : 300;
        Height = double.IsFinite(settings.CooldownHeight) ? Math.Clamp(settings.CooldownHeight, 64, 1000) : 120;
        if (settings.CooldownLeft is double x && settings.CooldownTop is double y && double.IsFinite(x) && double.IsFinite(y)) {
            Left = Math.Clamp(x, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + Math.Max(0, SystemParameters.VirtualScreenWidth - Width));
            Top = Math.Clamp(y, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + Math.Max(0, SystemParameters.VirtualScreenHeight - Height));
        }
        UiTheme.Apply(this, settings); Background = Brushes.Transparent;
        var shell = new Grid { Background = Brushes.Transparent }; Content = shell;
        var content = new StackPanel(); content.Children.Add(list); content.Children.Add(buffView); content.Children.Add(effectView);
        shell.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        shell.Children.Add(empty);
        var grip = new System.Windows.Controls.Primitives.ResizeGrip { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Opacity = .5 };
        shell.Children.Add(grip);
        // ScrollViewer handles bubbling mouse-down events before the outer grid.
        // Preview sees icon presses first; interactive resize/scroll controls keep theirs.
        shell.PreviewMouseDown += (_, e) => {
            if (e.ChangedButton != MouseButton.Left || !CanDragFrom(e.OriginalSource as DependencyObject)) return;
            e.Handled = true; beginDrag();
        };
        var menu = UiTheme.Menu(this);
        var edit = new MenuItem { Header = "Choose skills / cooldown settings" }; edit.Click += (_, _) => configure(); menu.Items.Add(edit);
        var locked = new MenuItem { Header = "Lock position", IsCheckable = true };
        locked.Click += (_, _) => { settings.CooldownLocked = locked.IsChecked; Refresh(); save(); }; menu.Items.Add(locked);
        var styles = new List<MenuItem>();
        foreach (string style in new[] { "Square", "Round", "Bars" }) {
            var option = new MenuItem { Header = style + " layout", IsCheckable = true };
            option.Click += (_, _) => { settings.CooldownStyle = style; Refresh(); save(); }; styles.Add(option); menu.Items.Add(option);
        }
        var glow = new MenuItem { Header = "Glow when ready", IsCheckable = true };
        glow.Click += (_, _) => { settings.CooldownReadyGlow = glow.IsChecked; Refresh(); save(); }; menu.Items.Add(glow);
        var showEffects = new MenuItem { Header = "Show effects on my character", IsCheckable = true };
        showEffects.Click += (_, _) => { settings.CooldownShowEffects = showEffects.IsChecked; Refresh(); save(); }; menu.Items.Add(showEffects);
        var close = new MenuItem { Header = "Close overlay" }; close.Click += (_, _) => Close(); menu.Items.Add(close);
        ContextMenu = menu;
        ContextMenuOpening += (_, _) => {
            locked.IsChecked = settings.CooldownLocked; glow.IsChecked = settings.CooldownReadyGlow;
            showEffects.IsChecked = settings.CooldownShowEffects;
            foreach (var option in styles) option.IsChecked = option.Header.ToString() == CooldownPresentation.Style(settings.CooldownStyle) + " layout";
        };
        timer.Tick += (_, _) => { Refresh(); grip.Visibility = settings.CooldownLocked ? Visibility.Collapsed : Visibility.Visible; };
        IsVisibleChanged += (_, _) => { if (IsVisible) { Refresh(); timer.Start(); } else timer.Stop(); };
        void SavePlacement() { settings.CooldownLeft = Left; settings.CooldownTop = Top; settings.CooldownWidth = Width; settings.CooldownHeight = Height; save(); }
        OverlayPlacement.Track(this, SavePlacement);
        Closed += (_, _) => { timer.Stop(); SavePlacement(); };
        Refresh(); grip.Visibility = settings.CooldownLocked ? Visibility.Collapsed : Visibility.Visible;
    }
    internal string DisplayedTime(int skill) => rows[skill].Time.Text;
    internal bool Dimmed(int skill) => rows[skill].Icon.Opacity < 1;
    internal int VisibleSkills => rows.Values.Count(r => r.Container.Visibility == Visibility.Visible);
    internal void CheckStyles(long now, int readySkill, Action<FrameworkElement, string> render)
    {
        string original = settings.CooldownStyle; bool glow = settings.CooldownReadyGlow;
        try {
            settings.CooldownReadyGlow = true;
            foreach (string style in new[] { "Square", "Round", "Bars" }) {
                settings.CooldownStyle = style; RefreshAt(now); UpdateLayout();
                var ready = rows[readySkill];
                var retained = ready.Container; RefreshAt(now);
                if (!ReferenceEquals(retained, rows[readySkill].Container) || ready.Container.Effect == null ||
                    ready.Container.Width != (style == "Bars" ? 250 : 52) ||
                    ready.Progress.Visibility != (style == "Bars" ? Visibility.Visible : Visibility.Collapsed) ||
                    ((Grid)ready.Container.Child).Clip is EllipseGeometry != (style == "Round"))
                    throw new InvalidOperationException("Cooldown layout, row reuse or ready glow is incorrect.");
                render(this, $"test-artifacts/Cooldown-{style}.png");
            }
            settings.CooldownReadyGlow = false; RefreshAt(now);
            if (rows[readySkill].Container.Effect != null) throw new InvalidOperationException("Ready glow could not be disabled.");
        } finally { settings.CooldownStyle = original; settings.CooldownReadyGlow = glow; RefreshAt(now); }
    }
    bool CanDragFrom(DependencyObject? source)
    {
        if (settings.CooldownLocked) return false;
        for (var node = source; node != null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is ScrollBar or Thumb or ResizeGrip or ButtonBase) return false;
        return source != null;
    }
    internal void CheckDragging()
    {
        var original = beginDrag; bool locked = settings.CooldownLocked;
        int requests = 0;
        beginDrag = () => requests++;
        try {
            settings.CooldownLocked = false;
            var icon = rows.Values.First().Icon;
            var press = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent };
            icon.RaiseEvent(press);
            if (requests != 1 || !press.Handled) throw new InvalidOperationException("Icon press did not reach the cooldown drag handler.");
            if (CanDragFrom(new ResizeGrip()) || CanDragFrom(new ScrollBar()) || CanDragFrom(new Thumb()))
                throw new InvalidOperationException("Cooldown dragging intercepts resize or scroll controls.");
            settings.CooldownLocked = true;
            icon.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
            if (requests != 1) throw new InvalidOperationException("Locked cooldown overlay still starts dragging.");
        } finally { beginDrag = original; settings.CooldownLocked = locked; }
    }
    internal void Refresh() => RefreshAt(Environment.TickCount64);
    internal void RefreshAt(long now)
    {
        ResizeMode = settings.CooldownLocked ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
        list.Cursor = settings.CooldownLocked ? Cursors.Arrow : Cursors.SizeAll;
        var selected = CooldownSkillChoice.Group(settings.CooldownSkills ?? [], names).Take(24).Select(c => c.Id).ToArray();
        string style = CooldownPresentation.Style(settings.CooldownStyle);
        bool buffs = settings.CooldownBuffs?.Length > 0;
        buffView.Visibility = buffs ? Visibility.Visible : Visibility.Collapsed;
        if (buffs) buffView.Update(BuffTracking.Rows(settings.CooldownBuffs!, effects?.Active(now) ?? [], capturing()));
        MinWidth = style == "Bars" || settings.CooldownShowEffects || buffs ? 260 : 64;
        if (Width < MinWidth) Width = MinWidth;
        empty.Visibility = selected.Length == 0 && !settings.CooldownShowEffects && !buffs ? Visibility.Visible : Visibility.Collapsed;
        effectView.Visibility = settings.CooldownShowEffects ? Visibility.Visible : Visibility.Collapsed;
        if (settings.CooldownShowEffects) effectView.Update(capturing() ? effects?.Active(now) ?? [] : [], names, icons);
        if (!rows.Keys.SequenceEqual(selected) || style != lastStyle) {
            lastStyle = style;
            rows.Clear(); list.Children.Clear();
            foreach (int id in selected) {
                var source = icons.Find(id) ?? SkillIcons.Placeholder;
                ImageSource gray = source;
                if (source is BitmapSource bitmap) { var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Gray8, null, 0); converted.Freeze(); gray = converted; }
                var icon = new Image { Source = source, Stretch = Stretch.UniformToFill };
                var time = new TextBlock { FontSize = 21, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 1, Opacity = 1 }, IsHitTestVisible = false };
                var tile = new Grid(); tile.Children.Add(icon); tile.Children.Add(time);
                var charges = new TextBlock { FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                    Background = UiTheme.Brush("#DD10151F"), Padding = new(3, 0, 3, 0),
                    HorizontalAlignment = style == "Bars" ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom, Margin = style == "Bars" ? new(30, 0, 0, 6) : new(0, 0, 8, 6),
                    Visibility = Visibility.Collapsed, IsHitTestVisible = false };
                var reduction = new TextBlock { Text = "↓", FontSize = 15, FontWeight = FontWeights.Bold,
                    Foreground = UiTheme.Brush("#65CBE9"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                    Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 0 }, Visibility = Visibility.Collapsed };
                tile.Children.Add(reduction);
                var progress = new ProgressBar { Minimum = 0, Maximum = 1, Height = 3, VerticalAlignment = VerticalAlignment.Bottom,
                    Background = UiTheme.Brush("#293443"), Foreground = UiTheme.Brush("#55D6BE"), BorderThickness = new(0), Visibility = Visibility.Collapsed };
                if (style == "Round") { tile.Clip = new EllipseGeometry(new Point(25, 25), 25, 25); }
                if (style == "Bars") {
                    icon.Width = 40; icon.Height = 40; icon.HorizontalAlignment = HorizontalAlignment.Left; icon.Margin = new(4, 0, 0, 0);
                    time.FontSize = 16; time.HorizontalAlignment = HorizontalAlignment.Right; time.Margin = new(0, 0, 10, 0);
                    var title = new TextBlock { Text = names.GetSkillName(id), Foreground = Brushes.White, FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center, Margin = new(52, 0, 65, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                    tile.Children.Add(title); progress.Visibility = Visibility.Visible; tile.Children.Add(progress);
                }
                tile.Children.Add(charges);
                var border = new Border { Width = style == "Bars" ? 250 : 52, Height = 52, Child = tile, Background = UiTheme.Brush("#B010151F"),
                    Margin = new(4), CornerRadius = new(style == "Round" ? 26 : 4), BorderThickness = new(1) };
                rows[id] = new(border, icon, time, source, gray, progress, reduction, charges); list.Children.Add(border);
            }
        }
        bool live = capturing();
        foreach (var (id, row) in rows) {
            var reading = live ? tracker.Reading(id, now, names, settings.CooldownFallbackSeconds) : ((double? Seconds, bool Estimated))(null, false);
            var charge = live ? tracker.ChargeDisplay(id, now) : null;
            // User-selected display lead, applied once after packet-age correction.
            // Keep recorded values and configured skill durations unchanged.
            double? remaining = CooldownPresentation.DisplaySeconds(reading.Seconds, settings);
            bool ready = charge != null ? charge.Count > 0 : remaining == 0;
            row.Charges.Text = charge == null ? "" : (charge.Estimated ? "~" : "") + charge.Count;
            row.Charges.Visibility = charge != null ? Visibility.Visible : Visibility.Collapsed;
            double? reduction = live && !reading.Estimated && !ready ? tracker.ReductionSeconds(id, now, names) : null;
            row.Reduction.Visibility = reduction > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (charge != null) row.MaximumRemaining = charge.DurationSeconds;
            else if (ready || remaining == null) row.MaximumRemaining = 0;
            else row.MaximumRemaining = Math.Max(row.MaximumRemaining, remaining.Value);
            CountdownBar.Update(row.Progress, CooldownPresentation.RemainingFraction(remaining, row.MaximumRemaining), remaining ?? 0);
            row.Container.BorderBrush = ready && settings.CooldownReadyGlow ? UiTheme.Brush("#55D6BE") : Brushes.Transparent;
            row.Container.Effect = ready && settings.CooldownReadyGlow ? readyGlow : null;
            row.Icon.Source = ready ? row.Color : row.Gray; row.Icon.Opacity = ready ? 1 : .45;
            row.Time.Text = remaining == null ? "?" : ready ? "" : (reading.Estimated ? "~" : "") + Math.Ceiling(remaining.Value).ToString();
            if (style == "Bars" && ready) row.Time.Text = "Ready";
            if (charge != null) row.Time.Text = remaining > 0 ? Math.Ceiling(remaining.Value).ToString() : charge.Count == 0 ? "?" : "";
            row.Container.ToolTip = names.GetSkillName(id) + "\n" + (!live ? "Start capture for cooldown tracking." : remaining == null ? "No matching cooldown yet. Configure fallback seconds to estimate from local casts." : reading.Estimated ? "Estimated from a local cast; reductions, charges and cancelled casts may differ." : ready ? "Ready" : "Server-reported cooldown") +
                (remaining != null && settings.CooldownOneSecondEarly ? "\nDisplay shown 1 second earlier (cooldown settings)." : "") +
                (reduction is double reduced ? $"\n↓ Latest server report shortened the previous countdown by {reduced:0.##}s. Cause not identified." : "") +
                (style == "Bars" ? "\nBar is relative to the longest countdown observed since this timer became active." : "") +
                (settings.CooldownLocked ? "\nPosition locked. Right-click to unlock." : "\nDrag to move. Right-click for settings.");
            if (charge != null) row.Container.ToolTip = names.GetSkillName(id) + $"\n{(charge.Estimated ? "Estimated" : "Reported")} charges: {charge.Count} / {charge.Capacity}\nRecharge: {charge.RemainingSeconds:0.0}s\nLast reported charges: {tracker.Charges(id, now)!.Count}\nRecovered charges are estimated from elapsed recharge intervals.";
            row.Container.Visibility = ready && !settings.CooldownShowReady && !(charge?.RemainingSeconds > 0) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
