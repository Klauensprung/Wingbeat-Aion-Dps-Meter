using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowShopPurchases()
    {
        ShopPurchaseProgress[] captured; lock (engine.Gate) captured = engine.ShopPurchases.Snapshot();
        ShowShopPurchases(captured);
    }
    void ShowShopPurchases(ShopPurchaseProgress[] captured)
    {
        var rows = captured.Select(r => new { r.Server, Owner = r.Character.Length == 0 ? "Server-wide" : r.Character, r.Goods, r.Count,
            Coverage = r.BaselineKnown ? "Captured total" : "Observed additions only",
            Reset = r.ResetAt is long reset ? DateTimeOffset.FromUnixTimeMilliseconds(reset).ToLocalTime().ToString("dd MMM HH:mm") + (r.InferredReset ? " (scheduled)" : "") : "Not captured",
            Captured = DateTimeOffset.FromUnixTimeMilliseconds(r.Timestamp).ToLocalTime().ToString("dd MMM HH:mm") }).ToArray();
        var window = new Window { Title = "Shop purchase counters", Owner = this, Width = 900, Height = 480, MinWidth = 850, MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var layout = new DockPanel { Margin = new(18) };
        var heading = UiSections.Heading("Shop purchase counters", "Last captured counters",
            "Last captured purchase counters by owner and shop goods ID. Verified checklist purchases use their server or character scope. Scheduled resets use the reference global-server calendar; other missing reset times stay unknown. Counters are separate from item gains in Loot history. Readings with known reset times are saved locally; unknown-reset readings last until the app closes.");
        DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        if (rows.Length == 0) { var empty = new TextBlock { Text = "No purchase counters captured. Open an in-game shop while capture is running.", Margin = new(0, 0, 0, 12) }; DockPanel.SetDock(empty, Dock.Top); layout.Children.Add(empty); }
        var table = new LiveTable { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, EnableRowVirtualization = true, RowHeight = 36 };
        foreach (var (label, field, width) in new[] { ("Server", "Server", 80), ("Owner", "Owner", 150), ("Goods ID", "Goods", 130), ("Count", "Count", 80), ("Coverage", "Coverage", 220), ("Reset", "Reset", 210), ("Captured", "Captured", 140) })
            table.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(field), Width = width });
        table.SetRows(rows); layout.Children.Add(table); window.Content = layout; window.Show();
    }
}
