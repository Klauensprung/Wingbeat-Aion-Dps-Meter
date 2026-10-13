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
    sealed record Tile(Border Container, Image Icon, TextBlock Time, ImageSource Color, ImageSource Gray);
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly WrapPanel list = new();
    readonly Dictionary<int, Tile> rows = new();
    readonly TextBlock empty = new() { Text = "Right-click to choose skills", Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new(8) };
    readonly Preferences settings;
    readonly CooldownTracker tracker;
    readonly SkillNames names;
    readonly SkillIcons icons;
    readonly Func<bool> capturing;
    Action beginDrag;
    internal CooldownOverlayWindow(Preferences settings, CooldownTracker tracker, SkillNames names, SkillIcons icons, Func<bool> capturing, Action configure, Action save)
    {
        this.settings = settings; this.tracker = tracker; this.names = names; this.icons = icons; this.capturing = capturing;
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
        shell.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
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
        var close = new MenuItem { Header = "Close overlay" }; close.Click += (_, _) => Close(); menu.Items.Add(close);
        ContextMenu = menu;
        ContextMenuOpening += (_, _) => locked.IsChecked = settings.CooldownLocked;
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
        empty.Visibility = selected.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!rows.Keys.SequenceEqual(selected)) {
            rows.Clear(); list.Children.Clear();
            foreach (int id in selected) {
                var source = icons.Find(id) ?? SkillIcons.Placeholder;
                ImageSource gray = source;
                if (source is BitmapSource bitmap) { var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Gray8, null, 0); converted.Freeze(); gray = converted; }
                var icon = new Image { Source = source, Stretch = Stretch.UniformToFill };
                var time = new TextBlock { FontSize = 21, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 1, Opacity = 1 }, IsHitTestVisible = false };
                var tile = new Grid(); tile.Children.Add(icon); tile.Children.Add(time);
                var border = new Border { Width = 52, Height = 52, Child = tile, Background = UiTheme.Brush("#B010151F"), Margin = new(3), CornerRadius = new(3), ClipToBounds = true };
                rows[id] = new(border, icon, time, source, gray); list.Children.Add(border);
            }
        }
        bool live = capturing();
        foreach (var (id, row) in rows) {
            var reading = live ? tracker.Reading(id, now, names, settings.CooldownFallbackSeconds) : ((double? Seconds, bool Estimated))(null, false);
            // User-selected display lead, applied once after packet-age correction.
            // Keep recorded values and configured skill durations unchanged.
            double? remaining = CooldownPresentation.DisplaySeconds(reading.Seconds, settings);
            bool ready = remaining == 0;
            row.Icon.Source = ready ? row.Color : row.Gray; row.Icon.Opacity = ready ? 1 : .45;
            row.Time.Text = remaining == null ? "?" : ready ? "" : (reading.Estimated ? "~" : "") + Math.Ceiling(remaining.Value).ToString();
            row.Container.ToolTip = names.GetSkillName(id) + "\n" + (!live ? "Start capture for cooldown tracking." : remaining == null ? "No matching cooldown yet. Configure fallback seconds to estimate from local casts." : reading.Estimated ? "Estimated from a local cast; reductions, charges and cancelled casts may differ." : ready ? "Ready" : "Server-reported cooldown") +
                (remaining != null && settings.CooldownOneSecondEarly ? "\nDisplay shown 1 second earlier (cooldown settings)." : "") +
                (settings.CooldownLocked ? "\nPosition locked. Right-click to unlock." : "\nDrag to move. Right-click for settings.");
            row.Container.Visibility = ready && !settings.CooldownShowReady ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
