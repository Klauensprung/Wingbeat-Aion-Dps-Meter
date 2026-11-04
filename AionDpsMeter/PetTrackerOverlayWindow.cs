using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AionDpsMeter.Local;

internal sealed class PetTrackerOverlayWindow : Window
{
    readonly TextBlock status = new() { FontSize = 10, Foreground = UiTheme.Brush("#93A2B8"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 8) };
    readonly StackPanel rows = new();
    readonly StackPanel collected = new();
    PetGatheringRow[] lastCollected = [];
    readonly Action<int> showMap;
    IReadOnlyList<NearbyPet>? previousRows;
    internal PetTrackerOverlayWindow(Preferences settings, Action save, Action<int> map, Action details)
    {
        showMap = map;
        Title = "Wingbeat pet tracker"; Width = 350; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        UiTheme.Apply(this, settings); Background = Brushes.Transparent;
        var body = new StackPanel { Margin = new(12) };
        var title = new TextBlock { Text = "PET FARMING", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = UiTheme.Brush(settings.Accent), Padding = new(0, 2, 0, 6) };
        title.MouseLeftButtonDown += (_, e) => { if (!settings.PetTrackerLocked) { DragMove(); e.Handled = true; } };
        title.ToolTip = "Drag this heading to move. Right-click for farming phase and position lock.";
        body.Children.Add(title); body.Children.Add(collected); body.Children.Add(status); body.Children.Add(rows);
        Content = new Border { Child = body, Background = UiTheme.Brush("#ED10151F"), BorderBrush = UiTheme.Brush("#293443"), BorderThickness = new(1), CornerRadius = new(8) };
        if (settings.PetTrackerLeft is double left && settings.PetTrackerTop is double top && double.IsFinite(left) && double.IsFinite(top)) {
            Left = Math.Clamp(left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + Math.Max(0, SystemParameters.VirtualScreenWidth - Width));
            Top = Math.Clamp(top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + Math.Max(0, SystemParameters.VirtualScreenHeight - 400));
        }
        var menu = UiTheme.Menu(this);
        var automatic = new MenuItem { Header = "Show when collecting pet souls", IsCheckable = true, IsChecked = settings.PetGatheringAutoShow };
        automatic.Click += (_, _) => { settings.PetGatheringAutoShow = automatic.IsChecked; save(); }; menu.Items.Add(automatic);
        var locked = new MenuItem { Header = "Lock position", IsCheckable = true, IsChecked = settings.PetTrackerLocked };
        locked.Click += (_, _) => { settings.PetTrackerLocked = locked.IsChecked; save(); }; menu.Items.Add(locked);
        var phases = new List<MenuItem>();
        for (int goal = 1; goal <= 3; goal++) {
            int selectedGoal = goal;
            var item = new MenuItem { Header = goal == 1 ? "Phase 1: collect" : $"Phase {goal}: level {goal}", IsCheckable = true,
                IsChecked = Math.Clamp(settings.PetTrackerGoal, 1, 3) == goal };
            phases.Add(item); menu.Items.Add(item);
            item.Click += (_, _) => { settings.PetTrackerGoal = selectedGoal; foreach (var phase in phases) phase.IsChecked = ReferenceEquals(phase, item); save(); };
        }
        menu.Opened += (_, _) => { for (int i = 0; i < phases.Count; i++) phases[i].IsChecked = Math.Clamp(settings.PetTrackerGoal, 1, 3) == i + 1; };
        var all = new MenuItem { Header = "Open nearby pet farming…" }; all.Click += (_, _) => details(); menu.Items.Add(all);
        var close = new MenuItem { Header = "Close pet tracker" }; close.Click += (_, _) => Close(); menu.Items.Add(close); ContextMenu = menu;
        OverlayPlacement.Track(this, () => { settings.PetTrackerLeft = Left; settings.PetTrackerTop = Top; save(); });
    }
    internal void UpdateCollected(PetGatheringRow[] pets)
    {
        if (lastCollected.SequenceEqual(pets)) return;
        lastCollected = pets; collected.Children.Clear();
        if (pets.Length == 0) return;
        collected.Children.Add(new TextBlock { Text = "RECENTLY COLLECTED", FontSize = 10, Foreground = UiTheme.Brush("#93A2B8"), Margin = new(0, 6, 0, 6) });
        foreach (var pet in pets) {
            collected.Children.Add(new TextBlock { Text = pet.Name + (pet.Gained > 0 ? $"  +{pet.Gained} collected" : "  Collected"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            collected.Children.Add(new TextBlock { Text = pet.Progress, FontSize = 12, Foreground = UiTheme.Brush("#55D6BE"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 3, 0, 10) });
        }
    }
    internal void Update(IReadOnlyList<NearbyPet> pets, CapturedPosition? position, long now, bool capturing, int goal)
    {
        string context = capturing ? "Live capture" : "Capture stopped";
        status.Text = $"Phase {Math.Clamp(goal, 1, 3)} · {context}\n" + (position == null ? "Waiting for a captured position." :
            LocalPositionTracker.MapKey(position.MapId) == null ? "No spawn mapping for this area." :
            $"Position captured {Math.Max(0, now - position.Timestamp) / 1000:N0}s ago · updates on loading / effects you cast\nStraight-line world units; creatures may be absent.");
        if (pets.Count == 0) status.Text += "\nNo known farming needs nearby. Open farming to include unknown progress.";
        if (ReferenceEquals(pets, previousRows)) return;
        previousRows = pets; rows.Children.Clear();
        foreach (var pet in pets) {
            var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var name = new TextBlock { Text = pet.Name, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis }; grid.Children.Add(name);
            var amount = new TextBlock { Text = pet.DistanceText + " u", Margin = new(10, 0, 0, 0), FontSize = 12, Foreground = UiTheme.Brush("#93A2B8") }; Grid.SetColumn(amount, 1); grid.Children.Add(amount);
            var button = new Button { Content = grid, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new(0, 3, 0, 3),
                ToolTip = $"{pet.Name} · {pet.Needed} souls needed\n{pet.Map} · {pet.Coordinates}\nOpen reference map" };
            button.Click += (_, _) => showMap(pet.Id); rows.Children.Add(button);
        }
    }
}
