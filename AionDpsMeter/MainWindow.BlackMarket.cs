using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowBlackMarket() => ShowBlackMarket(() => { lock (engine.Gate) return engine.BlackMarket.Snapshot(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()); });
    void ShowBlackMarket(Func<BlackMarketStock[]> read)
    {
        var window = new Window { Title = "Black market", Owner = this, Width = 1100, Height = 560, MinWidth = 1060, MinHeight = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var layout = new DockPanel { Margin = new(18) };
        var heading = UiSections.Heading("Black market", "Current captured offers", "Open the in-game black market while capture is running. Offers update automatically and disappear at their captured refresh time. Purchases mark a slot sold out; received items appear separately in Loot history. Known tabs show their names. Currency IDs remain visible until their units are verified.");
        DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        var status = new TextBlock { Margin = new(0, 0, 0, 12) }; DockPanel.SetDock(status, Dock.Top); layout.Children.Add(status);
        var table = new LiveTable { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, EnableRowVirtualization = true, RowHeight = 36 };
        table.Columns.Add(new DataGridTextColumn { Header = "Item", Binding = new Binding("Item"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 190 });
        foreach (var (label, field, width) in new[] { ("Tab", "Tab", 120), ("Slot", "Slot", 65), ("Quantity", "Quantity", 85), ("Price", "Price", 95), ("Base price", "BasePrice", 95), ("Currency ID", "Currency", 100), ("Status", "Status", 90), ("Refresh", "Refresh", 120) })
            table.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(field), Width = width });
        void Refresh() {
            var stocks = read();
            var rows = stocks.SelectMany(s => s.Entries.Select(e => new { Tab = s.Tab switch { 201 => "Specials (201)", 203 => "Skins (203)", 204 => "Pets (204)", _ => $"Tab {s.Tab}" }, e.Slot, Item = ItemNames.Default.Find(e.Item), e.Quantity, e.Price, e.BasePrice, e.Currency,
                Status = e.SoldOut ? "Sold out" : "Available", Refresh = DateTimeOffset.FromUnixTimeMilliseconds(s.RefreshAt).ToLocalTime().ToString("dd MMM HH:mm") })).ToArray();
            table.SetRows(rows); status.Text = stocks.Length == 0 ? "No current offers captured. Open the in-game black market." : $"{rows.Length:N0} offers across {stocks.Length:N0} tabs";
        }
        Refresh(); layout.Children.Add(table); window.Content = layout;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; timer.Tick += (_, _) => Refresh();
        window.Closed += (_, _) => timer.Stop(); window.Show(); timer.Start();
    }
}
