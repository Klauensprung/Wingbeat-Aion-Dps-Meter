using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AionDpsMeter.Local;

internal sealed class PetMapOverlayWindow : Window
{
    readonly PetSpawnPlot plot = new() { Height = 300 };
    readonly TextBlock status = new() { FontSize = 10, Foreground = UiTheme.Brush("#93A2B8"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 8) };
    readonly TextBlock detail = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    IReadOnlyList<NearbyPet>? previousRows;
    CapturedPosition? previousPosition;
    string? previousMap;
    internal PetMapOverlayWindow(Preferences settings, Action save)
    {
        Title = global::AionDpsMeter.Local.Localization.T("Wingbeat pet map"); Width = 410; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        UiTheme.Apply(this, settings); Background = Brushes.Transparent;
        var body = new StackPanel { Margin = new(12) };
        var title = new TextBlock { Text = global::AionDpsMeter.Local.Localization.T("PET MAP"), FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = UiTheme.Brush(settings.Accent), Padding = new(0, 2, 0, 6), ToolTip = global::AionDpsMeter.Local.Localization.T("Drag heading to move. Scroll map to zoom; drag map to pan.") };
        title.MouseLeftButtonDown += (_, e) => { if (!settings.PetMapLocked) { DragMove(); e.Handled = true; } };
        body.Children.Add(title); body.Children.Add(status); body.Children.Add(plot); body.Children.Add(detail);
        var prediction = new CheckBox { Content = global::AionDpsMeter.Local.Localization.T("Estimate my position"), IsChecked = settings.PetMapPredictPosition,
            Foreground = UiTheme.Brush("#E7EDF7"), Margin = new(0, 2, 0, 6), ToolTip = global::AionDpsMeter.Local.Localization.T("Use nearby entities and movement to estimate position. The blue area shows uncertainty in world units.") };
        prediction.Click += (_, _) => { settings.PetMapPredictPosition = prediction.IsChecked == true; save(); };
        body.Children.Insert(1, prediction);
        Content = new Border { Child = body, Background = UiTheme.Brush("#ED10151F"), BorderBrush = UiTheme.Brush("#293443"), BorderThickness = new(1), CornerRadius = new(8) };
        if (settings.PetMapLeft is double left && settings.PetMapTop is double top && double.IsFinite(left) && double.IsFinite(top)) {
            Left = Math.Clamp(left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + Math.Max(0, SystemParameters.VirtualScreenWidth - Width));
            Top = Math.Clamp(top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + Math.Max(0, SystemParameters.VirtualScreenHeight - 450));
        }
        plot.Selected += point => {
            var pets = point == null ? [] : previousRows?.Where(p => p.Point == point).ToArray() ?? [];
            detail.Text = pets.Length == 0 ? global::AionDpsMeter.Local.Localization.T("Select a dot for pet and soul details. Blue cross: your position.") :
                string.Join("\n", pets.Select(p => global::AionDpsMeter.Local.Localization.F($"{p.Name} · {p.Needed} souls · {p.DistanceText} world units")));
        };
        var menu = UiTheme.Menu(this);
        var locked = new MenuItem { Header = "Lock position", IsCheckable = true, IsChecked = settings.PetMapLocked };
        locked.Click += (_, _) => { settings.PetMapLocked = locked.IsChecked; save(); }; menu.Items.Add(locked);
        var fit = new MenuItem { Header = "Fit pets and position" }; fit.Click += (_, _) => plot.FitWithPlayer(20000); menu.Items.Add(fit);
        var center = new MenuItem { Header = "Center my position" }; center.Click += (_, _) => plot.CenterPlayer(); menu.Items.Add(center);
        var phases = new List<MenuItem>();
        for (int goal = 1; goal <= 3; goal++) {
            int selectedGoal = goal;
            var item = new MenuItem { Header = goal == 1 ? "Phase 1: collect" : $"Phase {goal}: level {goal}", IsCheckable = true };
            phases.Add(item); menu.Items.Add(item);
            item.Click += (_, _) => { settings.PetTrackerGoal = selectedGoal; save(); };
        }
        menu.Opened += (_, _) => { for (int i = 0; i < phases.Count; i++) phases[i].IsChecked = Math.Clamp(settings.PetTrackerGoal, 1, 3) == i + 1; };
        var close = new MenuItem { Header = "Close pet map" }; close.Click += (_, _) => Close(); menu.Items.Add(close); ContextMenu = menu;
        OverlayPlacement.Track(this, () => { settings.PetMapLeft = Left; settings.PetMapTop = Top; save(); });
    }
    internal void SetupSample(){status.Visibility=Visibility.Collapsed;detail.Visibility=Visibility.Collapsed;}
    internal void Update(IReadOnlyList<NearbyPet> pets, CapturedPosition? position, long now, bool capturing, int goal, MapPositionPresentation? presentation = null)
    {
        string? map = LocalPositionTracker.MapKey(position?.MapId ?? 0);
        status.Text = global::AionDpsMeter.Local.Localization.F($"Phase {Math.Clamp(goal, 1, 3)} · {(capturing ? Localization.T("Live capture") : Localization.T("Capture stopped"))}\n") +
            (map == null ? global::AionDpsMeter.Local.Localization.T("No position mapped for this area.") : global::AionDpsMeter.Local.Localization.F($"Position captured {Math.Max(0, now - position!.Timestamp) / 1000:N0}s ago · updates on loading / effects you cast\nReference spawns; creature availability is unknown."));
        if (presentation != null) status.Text = global::AionDpsMeter.Local.Localization.F($"Phase {Math.Clamp(goal, 1, 3)} · {(capturing ? Localization.T("Live capture") : Localization.T("Capture stopped"))}\n{(map == null ? Localization.T("No map available for this position.") : presentation.Status)}\nReference spawns; creature availability is unknown.");
        bool changedSite = map != previousMap || previousRows == null || !pets.Select(p => p.Point).ToHashSet().SetEquals(previousRows.Select(p => p.Point));
        previousRows = pets;
        if (changedSite) {
            previousRows = pets; previousMap = map;
            plot.SetSite(map == null ? null : new PetSpawnSite(0, map, "Nearby farming", pets.Select(p => p.Point).Distinct().ToArray()));
            plot.SetPlayer(map == null ? null : position, presentation?.Estimate?.Accuracy ?? 0); plot.FitWithPlayer(20000);
        } else plot.SetPlayer(map == null ? null : position, presentation?.Estimate?.Accuracy ?? 0);
        previousPosition = position;
        if (pets.Count == 0) detail.Text = global::AionDpsMeter.Local.Localization.T("No known farming needs in this area for the selected phase.");
    }
}
