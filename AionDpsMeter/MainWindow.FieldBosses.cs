using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowFieldBosses()
    {
        var saved = viewed;
        var historical = new FieldBossTracker();
        if (saved != null) foreach (var item in saved.Network) historical.Apply(item);
        var window = new Window { Title = "Field boss timers", Owner = this, Width = 1050, Height = 560, MinWidth = 700, MinHeight = 350, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var layout = new DockPanel { Margin = new(18) };
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        heading.Children.Add(UiSections.Heading("Field boss timers", "Open the field-boss list in Aion to refresh these observations.",
            (saved == null ? "Live capture. " : "Saved fight: " + saved.Label + ". ") + "Experimental map-list data. An elapsed timer does not confirm a spawn. Unrecognized maps retain their IDs."));
        var search = new TextBox { Tag = "Search boss or map...", Margin = new(0, 0, 0, 8), ToolTip = "Search map, boss or map ID" }; heading.Children.Add(search);
        var status = new TextBlock { Margin = new(0, 0, 0, 8) }; heading.Children.Add(status);
        var table = new LiveTable { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, EnableRowVirtualization = true, RowHeight = 36 };
        void Column(string title, string property, int width) => table.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = width });
        Column("Map", "Map", 130); Column("Boss (reference)", "Boss", 210); Column("Observed state", "Status", 200);
        Column("Reported time", "ReportedTime", 130); Column("Remaining", "Remaining", 110); Column("Last observed", "Observed", 130);
        Column("Map ID", "MapId", 80); Column("Slot", "Slot", 50);
        void Update() {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            FieldBossTimerRow[] rows;
            if (saved != null) rows = historical.Rows(now); else lock (engine.Gate) rows = engine.FieldBosses.Rows(now);
            string query = search.Text.Trim();
            table.SetRows(rows.Where(r => r.Map.Contains(query, StringComparison.OrdinalIgnoreCase) || r.Boss.Contains(query, StringComparison.OrdinalIgnoreCase) || r.MapId.ToString().Contains(query)).ToArray());
            status.Text = rows.Length == 0 ? "No field-boss map lists captured." : $"{rows.Length} observed slots · local data only";
        }
        search.TextChanged += (_, _) => Update();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; timer.Tick += (_, _) => Update(); window.Closed += (_, _) => timer.Stop();
        layout.Children.Add(table); window.Content = layout; Update(); timer.Start(); window.Show();
    }
}
