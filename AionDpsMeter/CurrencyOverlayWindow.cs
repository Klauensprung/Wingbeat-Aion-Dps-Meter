using System.Windows;
using System.Windows.Controls;

namespace AionDpsMeter.Local;

internal sealed class CurrencyOverlayWindow : Window
{
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 10) };
    readonly StackPanel table = new() { Name = "CurrencyRows" };
    CurrencyBalance[] previous = [];
    readonly Preferences preferences;
    readonly Action? save;
    readonly Button choose = new() { Content = "Choose currencies", Padding = new(8, 4, 8, 4), Margin = new(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
    internal CurrencyOverlayWindow(Preferences preferences, Action? save = null)
    {
        this.preferences = preferences; this.save = save;
        Title = "Wingbeat currencies"; Width = 460; Height = 380; MinWidth = 420; MinHeight = 320;
        if (double.IsFinite(preferences.CurrencyWidth)) Width = Math.Clamp(preferences.CurrencyWidth, MinWidth, Math.Max(MinWidth, SystemParameters.VirtualScreenWidth));
        if (double.IsFinite(preferences.CurrencyHeight)) Height = Math.Clamp(preferences.CurrencyHeight, MinHeight, Math.Max(MinHeight, SystemParameters.VirtualScreenHeight));
        if (preferences.CurrencyLeft is double left && preferences.CurrencyTop is double top && double.IsFinite(left) && double.IsFinite(top)) {
            Left = Math.Clamp(left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + Math.Max(0, SystemParameters.VirtualScreenWidth - Width));
            Top = Math.Clamp(top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + Math.Max(0, SystemParameters.VirtualScreenHeight - Height));
        }
        Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        UiTheme.Apply(this, preferences);
        var layout = new DockPanel { Margin = new(12) };
        var heading = new TextBlock { Text = "CURRENT CURRENCIES", FontWeight = FontWeights.SemiBold, FontSize = 12 };
        DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        DockPanel.SetDock(choose, Dock.Bottom); layout.Children.Add(choose);
        choose.Click += OnChoose;
        DockPanel.SetDock(status, Dock.Top); layout.Children.Add(status);
        var headers = new DockPanel { Height = 28 };
        var balanceHeader = new TextBlock { Text = "Balance", Width = 166, TextAlignment = TextAlignment.Right, Padding = new Thickness(0, 0, 8, 0), FontWeight = FontWeights.SemiBold };
        DockPanel.SetDock(balanceHeader, Dock.Right); headers.Children.Add(balanceHeader);
        headers.Children.Add(new TextBlock { Text = "Currency", FontWeight = FontWeights.SemiBold });
        DockPanel.SetDock(headers, Dock.Top); layout.Children.Add(headers);
        layout.Children.Add(new ScrollViewer { Content = table, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = layout;
        Closed += (_, _) => {
            choose.Click -= OnChoose;
            if (choose.ContextMenu is { } menu) { menu.IsOpen = false; menu.Items.Clear(); menu.PlacementTarget = null; }
            choose.ContextMenu = null;
            table.Children.Clear();
            // Release displayed balances and detached visual content on close.
            layout.Children.Clear(); Content = null;
            previous = [];
        };
        if (save != null) {
            void SavePlacement() {
                if (WindowState != WindowState.Normal) return;
                preferences.CurrencyLeft = Left; preferences.CurrencyTop = Top;
                preferences.CurrencyWidth = Width; preferences.CurrencyHeight = Height; save();
            }
            OverlayPlacement.Track(this, SavePlacement);
            Closed += (_, _) => { if (IsLoaded && double.IsFinite(Left) && double.IsFinite(Top)) SavePlacement(); };
        }
    }
    void OnChoose(object sender, RoutedEventArgs e) => Choose();
    void Choose()
    {
        var menu = UiTheme.Menu(this);
        var all = new MenuItem { Header = "Show all captured currencies", IsCheckable = true, IsChecked = preferences.CurrencyShown == null };
        all.Click += (_, _) => { preferences.CurrencyShown = null; save?.Invoke(); RenderRows(); }; menu.Items.Add(all);
        menu.Items.Add(new Separator());
        foreach (var balance in previous) {
            var item = new MenuItem { Header = ItemNames.Default.Find(balance.ItemId), Tag = balance.ItemId, IsCheckable = true,
                IsChecked = preferences.CurrencyShown == null || preferences.CurrencyShown.Contains(balance.ItemId) };
            item.Click += (_, _) => {
                var selected = (preferences.CurrencyShown ?? previous.Select(b => b.ItemId).ToArray()).ToHashSet();
                if (item.IsChecked) selected.Add(balance.ItemId); else selected.Remove(balance.ItemId);
                preferences.CurrencyShown = selected.Where(id => id is >= 930100000 and <= 930100999 or 512000014).Order().ToArray();
                save?.Invoke(); RenderRows();
            };
            menu.Items.Add(item);
        }
        if (previous.Length == 0) menu.Items.Add(new MenuItem { Header = "Capture balances to choose currencies", IsEnabled = false });
        choose.ContextMenu = menu; menu.PlacementTarget = choose; menu.IsOpen = true;
    }
    void RenderRows()
    {
        var selected = preferences.CurrencyShown?.ToHashSet();
        var rows = previous.Where(b => selected == null || selected.Contains(b.ItemId)).ToArray();
        table.Children.Clear();
        foreach (var balance in rows) {
            var row = new DockPanel { Height = 32, Tag = balance.ItemId };
            var count = new TextBlock { Text = balance.Count.ToString("N0"), Width = 150, TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            DockPanel.SetDock(count, Dock.Right); row.Children.Add(count);
            string name = ItemNames.Default.Find(balance.ItemId);
            row.Children.Add(new TextBlock { Text = name, ToolTip = name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            table.Children.Add(row);
        }
        choose.Content = selected == null ? "Choose currencies · All" : $"Choose currencies · {rows.Length} shown";
    }
    internal void Update(CurrencyBalance[] balances, bool capturing, string? character)
    {
        status.Text = (capturing ? "Live capture" : "Capture stopped — last captured balances") +
            (string.IsNullOrEmpty(character) ? "" : " · " + character) + "\n" +
            (balances.Length == 0 ? "No balances captured for the current character. Enter the game with capture running; unknown balances are not zero." :
                "Current holdings, including spending. Loot history shows gains separately.");
        if (previous.SequenceEqual(balances)) return;
        previous = balances.ToArray();
        RenderRows();
    }
}
