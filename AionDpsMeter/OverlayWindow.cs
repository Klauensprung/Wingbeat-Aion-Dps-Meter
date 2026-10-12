using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AionDpsMeter.Local;

public sealed class OverlayWindow : Window
{
    readonly MeterView meter = new() { Margin = new(7, 4, 7, 2), RowBackground = UiTheme.Brush("#801A2230") };
    readonly MeterView ownMeter = new() { Margin = new(7, 4, 7, 0), Visibility = Visibility.Collapsed, RowBackground = UiTheme.Brush("#801A2230") };
    readonly ScrollViewer actorScroll;
    readonly BackAttackIndicator backAttack = new();
    readonly DispatcherTimer backAttackTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    readonly Border backAttackBorder = new() { BorderBrush = UiTheme.Brush("#88E7CF7D"), BorderThickness = new(2),
        CornerRadius = new(4), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly ParticipantColors participantColors = new();
    readonly StackPanel header = new() { Background = UiTheme.Brush("#6010151F") };
    readonly TextBlock moveGrip = new() { Text = "⋮⋮", FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Foreground = UiTheme.Brush("#93A2B8") };
    readonly TextBlock duration = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new(4, 0, 8, 0) };
    readonly TextBlock encounter = new() { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock total = new() { FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new(10, 7, 10, 4) };
    readonly TextBlock energy = new() { FontSize = 10, Margin = new(10, 0, 20, 8) };
    readonly TextBlock pingStatus = new() { FontSize = 10, Margin = new(10, 0, 20, 4), ToolTip = "Last observed local client-clock echo estimate. Other players' ping is not supplied by this protocol." };
    readonly Button mode, moveLock;
    readonly Action? reset;
    readonly Action? saveSettings;
    readonly ResizeGrip resizeGrip = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Width = 14, Height = 14, Opacity = .5 };
    Preferences preferences = new();
    Fight? fight;
    TotalRow[]? totals;
    PvpData? pvp;
    string lastTheme = "", lastAccent = "";
    string? lastBackAttackColor;
    bool positioned;
    public int RowCount => ownMeter.RowCount + meter.RowCount;
    internal string CurrentScope { get; private set; } = "";

    public OverlayWindow(Action? reset = null, Action<int>? actorSelected = null, Action? saveSettings = null)
    {
        this.reset = reset; this.saveSettings = saveSettings;
        Title = "Wingbeat Overlay"; Width = 390; Height = 530; MinWidth = 310; MinHeight = 185;
        Topmost = true; ShowInTaskbar = false; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip;
        AllowsTransparency = true; Background = Brushes.Transparent;
        FontFamily = new("Segoe UI"); UseLayoutRounding = true; SnapsToDevicePixels = true;
        var shell = new Border { Padding = new(1, 3, 1, 1) };
        var layers = new Grid(); layers.Children.Add(shell); layers.Children.Add(backAttackBorder); Content = layers;
        var root = new DockPanel(); shell.Child = root;
        // Tint only the controls and bars: unused space stays clear and text
        // retains full opacity. The header's tint also keeps it easy to drag.
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        // Buttons consume their own mouse-down events. Everything else in the
        // header, including the full encounter row and its padding, can drag.
        header.MouseLeftButtonDown += (_, e) => {
            if (e.ChangedButton != MouseButton.Left || preferences.OverlayLocked) return;
            e.Handled = true; DragMove();
        };
        var toolbar = new DockPanel { Margin = new(6, 4, 5, 0), LastChildFill = true, Background = Brushes.Transparent };
        mode = SmallButton("DAMAGE  ▾", "Choose damage, healing and display options", ShowOptions); mode.FontWeight = FontWeights.Bold; mode.HorizontalAlignment = HorizontalAlignment.Left;
        var close = SmallButton("×", "Close overlay", Close); close.FontSize = 19;
        moveLock = SmallButton("◇", "Lock or unlock overlay position", () => { preferences.OverlayLocked = !preferences.OverlayLocked; RefreshMeter(); saveSettings?.Invoke(); });
        var clear = SmallButton("↻", "Save and start a new fight", () => this.reset?.Invoke()); clear.FontSize = 18;
        foreach (var button in new[] { close, moveLock, clear }) { DockPanel.SetDock(button, Dock.Right); toolbar.Children.Add(button); }
        DockPanel.SetDock(duration, Dock.Right); toolbar.Children.Add(duration); toolbar.Children.Add(mode);
        var moveRow = new DockPanel(); DockPanel.SetDock(moveGrip, Dock.Left);
        moveRow.Children.Add(moveGrip); moveRow.Children.Add(encounter);
        AutomationProperties.SetName(moveGrip, "Move overlay");
        AutomationProperties.SetAutomationId(moveGrip, "OverlayMoveGrip");
        header.Children.Add(toolbar);
        header.Children.Add(new Border { Child = moveRow, Background = Brushes.Transparent, MinHeight = 27, Padding = new(10, 0, 10, 6) });
        // Keep the local row outside the scrolling opponents/party list so it
        // stays visible even when the overlay only has room for a single row.
        DockPanel.SetDock(ownMeter, Dock.Top); root.Children.Add(ownMeter);
        var footer = new Grid { Background = UiTheme.Brush("#6010151F") }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var footerText = new StackPanel(); footerText.Children.Add(total); footerText.Children.Add(pingStatus); footerText.Children.Add(energy); footer.Children.Add(footerText);
        footer.Children.Add(resizeGrip);
        actorScroll = new ScrollViewer { Content = meter, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        root.Children.Add(actorScroll);
        meter.ActorSelected += id => actorSelected?.Invoke(id);
        ownMeter.ActorSelected += id => actorSelected?.Invoke(id);
        backAttackTimer.Tick += (_, _) => RefreshBackAttackBorder();
        backAttackTimer.Start();
        Closed += (_, _) => {
            backAttackTimer.Stop();
            preferences.OverlayWidth = Width; preferences.OverlayHeight = Height;
            preferences.OverlayLeft = Left; preferences.OverlayTop = Top; saveSettings?.Invoke();
        };
    }
    static Button SmallButton(string text, string tip, Action action)
    {
        var button = new Button { Content = text, ToolTip = tip, Padding = new(7, 4, 7, 4), Margin = new(1), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, FontSize = 11 };
        button.Click += (_, _) => action(); return button;
    }
    void ShowOptions()
    {
        var menu = CreateOptions(); mode.ContextMenu = menu; menu.IsOpen = true;
    }
    internal ContextMenu CreateOptions()
    {
        var menu = UiTheme.Menu(this, mode); menu.Placement = PlacementMode.Bottom;
        void Item(string title, bool selected, Action action)
        {
            var item = new MenuItem { Header = title, IsCheckable = true, IsChecked = selected };
            item.Click += (_, _) => { action(); RefreshMeter(); saveSettings?.Invoke(); }; menu.Items.Add(item);
        }
        Item("Damage · DPS", !preferences.OverlayHealing, () => preferences.OverlayHealing = false);
        Item("Healing · HPS", preferences.OverlayHealing, () => preferences.OverlayHealing = true);
        menu.Items.Add(new Separator());
        string scope = preferences.OverlayAllActors ? "All" : preferences.OverlayScope;
        void Scope(string title, string value) => Item(title, scope == value, () => { preferences.OverlayScope = value; preferences.OverlayAllActors = false; });
        Scope("Automatic · party / PvP", "Auto");
        Scope("PvP · you and opponents", "PvP");
        Scope("Party / players", "Party");
        Scope("All actors", "All");
        menu.Items.Add(new Separator());
        Item("Pin my character first", preferences.OverlayPinLocal, () => preferences.OverlayPinLocal = !preferences.OverlayPinLocal);
        Item("Compact rows", preferences.OverlayCompact, () => preferences.OverlayCompact = !preferences.OverlayCompact);
        Item("Class colours", !preferences.OverlayCustomBars, () => preferences.OverlayCustomBars = !preferences.OverlayCustomBars);
        Item("Lock position", preferences.OverlayLocked, () => preferences.OverlayLocked = !preferences.OverlayLocked);
        return menu;
    }
    public void Update(Fight value, Preferences settings, string unusedEnergy = "", TotalRow[]? rows = null, PvpData? pvpData = null)
    {
        bool changed = !ReferenceEquals(fight, value);
        if (lastBackAttackColor != settings.BackAttackColor) {
            var brush = new SolidColorBrush(BackAttackColour(settings.BackAttackColor)); brush.Freeze();
            backAttackBorder.BorderBrush = brush; lastBackAttackColor = settings.BackAttackColor;
        }
        preferences = settings; fight = value; totals = rows ?? (changed ? Analysis.Totals(value) : totals) ?? [];
        pvp = pvpData ?? (changed ? PvpAnalysis.Build(value) : pvp) ?? PvpAnalysis.Build(value);
        if (!positioned)
        {
            Width = double.IsFinite(settings.OverlayWidth) ? Math.Clamp(settings.OverlayWidth, 310, 900) : 390;
            Height = double.IsFinite(settings.OverlayHeight) ? Math.Clamp(settings.OverlayHeight, 185, 1000) : 530;
            if (settings.OverlayLeft is double left && settings.OverlayTop is double top && double.IsFinite(left) && double.IsFinite(top))
            {
                Left = Math.Clamp(left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + Math.Max(0, SystemParameters.VirtualScreenWidth - Width));
                Top = Math.Clamp(top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + Math.Max(0, SystemParameters.VirtualScreenHeight - Height));
            }
            positioned = true;
        }
        if (lastTheme != settings.Theme || lastAccent != settings.Accent)
        {
            UiTheme.Apply(this, settings); lastTheme = settings.Theme; lastAccent = settings.Accent;
            // Shared themes set an opaque window surface for the main app.
            Background = Brushes.Transparent;
            duration.Foreground = encounter.Foreground = energy.Foreground = UiTheme.Brush("#93A2B8"); total.Foreground = UiTheme.Brush("#E7EDF7");
        }
        RefreshMeter();
    }
    void RefreshMeter()
    {
        if (fight == null || totals == null || pvp == null) return;
        backAttack.Observe(fight, Environment.TickCount64, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        RefreshBackAttackBorder();
        string scope = preferences.OverlayAllActors ? "All" : preferences.OverlayScope;
        bool showPvp = scope == "PvP" || (scope is not ("All" or "Party") && pvp.HasOpponents);
        var data = showPvp ? PvpAnalysis.Meter(fight, pvp, preferences.OverlayHealing, pinLocal: preferences.OverlayPinLocal) :
            MeterPresentation.Build(fight, totals, preferences.OverlayHealing, scope == "All", pinLocal: preferences.OverlayPinLocal);
        if (!preferences.OverlayCustomBars)
            data = data with { Rows = data.Rows.Select(r => r with { Color = MeterPresentation.ClassColorFor(r.Job, r.Color) }).ToArray() };
        else data = participantColors.Apply(fight, data);
        CurrentScope = data.Scope;
        pingStatus.Text = fight.LocalPing is { } sample ? $"Local ping (last estimate)   ~{sample.Milliseconds} ms" : "Local ping   --";
        pingStatus.Foreground = UiTheme.Brush(fight.LocalPing?.Milliseconds switch { < 60 => "#55D6BE", < 100 => "#EABF70", < 200 => "#EFAB78", >= 200 => "#EF8792", _ => "#93A2B8" });
        mode.Content = (showPvp ? "PVP · " : "") + (preferences.OverlayHealing ? "HEALING" : "DAMAGE") + "  ▾";
        moveLock.Content = preferences.OverlayLocked ? "◆" : "◇";
        moveLock.ToolTip = preferences.OverlayLocked ? "Unlock overlay position" : "Lock overlay position";
        header.Cursor = preferences.OverlayLocked ? Cursors.Arrow : Cursors.SizeAll;
        header.ToolTip = preferences.OverlayLocked ? "Position locked — use the diamond button to unlock" : "Drag the header to move the overlay";
        moveGrip.Opacity = preferences.OverlayLocked ? .35 : 1;
        ResizeMode = preferences.OverlayLocked ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
        resizeGrip.Visibility = preferences.OverlayLocked ? Visibility.Collapsed : Visibility.Visible;
        duration.Text = showPvp ? MeterPresentation.Duration(pvp.HasOpponents ? pvp.Seconds : 0) : MeterPresentation.Duration(fight);
        encounter.Text = (fight.Demo ? "DEMO  ·  " : "") + fight.Label + "  ·  " + data.Scope;
        var own = data.Rows.Where(r => preferences.OverlayPinLocal && r.Local).ToArray();
        var others = data.Rows.Where(r => !preferences.OverlayPinLocal || !r.Local).ToArray();
        ownMeter.Update(own, preferences.OverlayCompact, preferences.OverlayHealing, fight.HasEvents, showClassSymbols: true);
        ownMeter.Visibility = own.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        actorScroll.Visibility = own.Length > 0 && others.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        meter.Update(others, preferences.OverlayCompact, preferences.OverlayHealing, fight.HasEvents, startPosition: own.Length > 0 ? 2 : 1, showClassSymbols: true);
        total.Text = showPvp && !preferences.OverlayHealing ? $"YOU   {MeterPresentation.Number(pvp.YourDamage)}     ·     ENEMY   {MeterPresentation.Number(pvp.EnemyDamage)}" :
            $"TOTAL   {MeterPresentation.Number(data.Amount)}     ·     {MeterPresentation.Number(data.Rate)} {(preferences.OverlayHealing ? "HPS" : "DPS")}";
        energy.Text = fight.Energy == null ? "Wingbeat  ·  local" : $"Loot energy   {fight.Energy.Daily:N0} daily  /  {fight.Energy.Bonus:N0} bonus";
    }
    static Color BackAttackColour(string? value)
    {
        Color colour;
        try { colour = (Color)ColorConverter.ConvertFromString(value ?? "#E7CF7D"); }
        catch { colour = Color.FromRgb(231, 207, 125); }
        return Color.FromArgb(136, colour.R, colour.G, colour.B);
    }
    void RefreshBackAttackBorder() => backAttackBorder.Visibility = backAttack.Active(Environment.TickCount64) ? Visibility.Visible : Visibility.Collapsed;
    internal void CheckBackAttackBorder(bool expected)
    {
        if ((backAttackBorder.Visibility == Visibility.Visible) != expected || backAttackBorder.IsHitTestVisible ||
            backAttackBorder.BorderBrush is not SolidColorBrush brush || brush.Color.A is 0 or 255 || brush.Color != BackAttackColour(preferences.BackAttackColor))
            throw new InvalidOperationException("Back-attack border state or transparency is incorrect.");
    }
    internal void CheckModes()
    {
        bool previousPin = preferences.OverlayPinLocal;
        preferences.OverlayPinLocal = false; RefreshMeter();
        if (ownMeter.RowCount != 0 || meter.RowCount != 10) throw new InvalidOperationException("Default ranking still pins the local row.");
        CreateOptions().Items.OfType<MenuItem>().Single(i => i.Header?.ToString() == "Pin my character first").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        if (!preferences.OverlayPinLocal || ownMeter.RowCount != 1 || meter.RowCount != 9) throw new InvalidOperationException("Pin toggle did not move the local row.");
        preferences.OverlayPinLocal = previousPin; RefreshMeter();
        void CheckRows() { foreach (var bar in ownMeter.Children.OfType<MeterBar>().Concat(meter.Children.OfType<MeterBar>())) bar.CheckPresentation(); }
        CheckRows();
        bool previousCustom = preferences.OverlayCustomBars;
        var colourToggle = CreateOptions().Items.OfType<MenuItem>().Single(i => i.Header?.ToString() == "Class colours");
        colourToggle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        if (preferences.OverlayCustomBars == previousCustom || CreateOptions().Items.OfType<MenuItem>().Single(i => i.Header?.ToString() == "Class colours").IsChecked != !preferences.OverlayCustomBars)
            throw new InvalidOperationException("Class colour toggle did not update the overlay preferences and menu.");
        CheckRows();
        preferences.OverlayCustomBars = previousCustom; RefreshMeter();
        CheckRows();
        bool previous = preferences.OverlayHealing;
        preferences.OverlayHealing = true; RefreshMeter();
        if (RowCount != 10 || ownMeter.RowCount != 1) throw new InvalidOperationException("Healing overlay did not retain all ten party members and the local row.");
        preferences.OverlayHealing = previous; RefreshMeter();
        if (RowCount != 10) throw new InvalidOperationException("Damage overlay did not retain ten party members.");
    }
    internal void CheckPinnedScrolling()
    {
        foreach (var bar in ownMeter.Children.OfType<MeterBar>().Concat(meter.Children.OfType<MeterBar>())) bar.CheckPresentation();
        var before = ownMeter.TranslatePoint(new Point(), this);
        actorScroll.ScrollToEnd(); UpdateLayout();
        var after = ownMeter.TranslatePoint(new Point(), this);
        if (ownMeter.RowCount != 1 || before != after || after.Y < 0 || after.Y + ownMeter.ActualHeight > ActualHeight)
            throw new InvalidOperationException("Local player row moved out of view when scrolling.");
    }
}
