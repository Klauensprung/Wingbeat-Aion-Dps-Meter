using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AionDpsMeter.Local;

internal sealed class LootOverlayWindow : Window
{
    readonly TextBlock status = new() { FontSize = 10, Foreground = UiTheme.Brush("#93A2B8"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 8) };
    readonly StackPanel rows = new();
    LootLedger.OverlayView? lastView;
    bool? wasCapturing;
    internal LootOverlayWindow(Preferences settings, Action save, Action showLoot)
    {
        Title = "Wingbeat loot"; Width = 330; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        UiTheme.Apply(this, settings); Background = Brushes.Transparent;
        var body = new StackPanel { Margin = new(12) };
        body.Children.Add(new TextBlock { Text = "LOOT", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = UiTheme.Brush(settings.Accent) });
        body.Children.Add(status); body.Children.Add(rows);
        Content = new Border { Child = body, Background = UiTheme.Brush("#ED10151F"), BorderBrush = UiTheme.Brush("#293443"), BorderThickness = new(1), CornerRadius = new(8) };
        if (settings.LootLeft is double left && settings.LootTop is double top && double.IsFinite(left) && double.IsFinite(top)) {
            Left = Math.Clamp(left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + Math.Max(0, SystemParameters.VirtualScreenWidth - Width));
            Top = Math.Clamp(top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + Math.Max(0, SystemParameters.VirtualScreenHeight - 360));
        }
        MouseLeftButtonDown += (_, e) => { if (!settings.LootLocked) { DragMove(); e.Handled = true; } };
        var menu = UiTheme.Menu(this);
        var locked = new MenuItem { Header = "Lock position", IsCheckable = true, IsChecked = settings.LootLocked };
        locked.Click += (_, _) => { settings.LootLocked = locked.IsChecked; save(); }; menu.Items.Add(locked);
        var history = new MenuItem { Header = "Open loot history…" }; history.Click += (_, _) => showLoot(); menu.Items.Add(history);
        var close = new MenuItem { Header = "Close loot overlay" }; close.Click += (_, _) => Close(); menu.Items.Add(close); ContextMenu = menu;
        OverlayPlacement.Track(this, () => { settings.LootLeft = Left; settings.LootTop = Top; save(); });
    }
    internal void Update(LootLedger.OverlayView view, bool capturing)
    {
        if (ReferenceEquals(lastView, view) && wasCapturing == capturing) return;
        lastView = view; wasCapturing = capturing;
        var recent = view.Rows;
        status.Text = (capturing ? "Live capture" : "Capture stopped") + (recent.Length > 0 ? $" · {recent[0].Character}\nLatest item totals in this loot session" : " · waiting for attributed pickups");
        if (view.OmittedItems) status.Text += " · some item entries omitted at memory limit";
        rows.Children.Clear();
        foreach (var item in recent) {
            var grid = new Grid { Margin = new(0, 4, 0, 4), ToolTip = $"{item.Item}\n{item.Map} ({item.MapId}) · {item.Pickups:N0} pickups\nLast: {item.LastTime}" };
            grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var labels = new StackPanel();
            labels.Children.Add(new TextBlock { Text = item.Item, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 });
            labels.Children.Add(new TextBlock { Text = item.Map, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 10, Foreground = UiTheme.Brush("#93A2B8") });
            grid.Children.Add(labels);
            var amount = new TextBlock { Text = $"×{item.Quantity:N0}", FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(amount, 1); grid.Children.Add(amount); rows.Children.Add(grid);
        }
    }
}
