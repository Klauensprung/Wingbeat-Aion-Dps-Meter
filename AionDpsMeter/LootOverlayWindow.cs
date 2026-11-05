using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AionDpsMeter.Local;

internal sealed class LootOverlayWindow : Window
{
    readonly StackPanel rows=new();
    LootItemRow[] displayed=[];
    internal bool HasRecentItems { get; private set; }
    internal static LootItemRow[] Recent(LootLedger.OverlayView view,long now)=>view.Rows.Where(r=>r.Last<=now&&now-r.Last<10000).ToArray();
    internal LootOverlayWindow(Preferences settings, Action save, Action showLoot)
    {
        Title = "Wingbeat loot"; Width = 330; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        UiTheme.Apply(this, settings); Background = Brushes.Transparent;
        var body = new StackPanel { Margin = new(12) };
        body.Children.Add(new TextBlock { Text = "LOOT", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = UiTheme.Brush(settings.Accent) });
        body.Children.Add(rows);
        Content = new Border { Child = body, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new(0), CornerRadius = new(8) };
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
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var recent=capturing?Recent(view,now):[];
        HasRecentItems=recent.Length>0;
        double age=recent.Length>0?now-recent.Max(r=>r.Last):10000;
        // Stay readable for eight seconds, then fade out over the final two.
        OverlayMotion.Number(this,OpacityProperty,Math.Clamp((10000-age)/2000,0,1));
        if(displayed.SequenceEqual(recent))return;
        displayed=recent;
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
