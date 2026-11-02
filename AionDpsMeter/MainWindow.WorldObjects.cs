using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowMapEvents()
    {
        MapEventProgress[] rows; lock (engine.Gate) rows = engine.MapEvents.Snapshot();
        ShowMapEvents(rows);
    }
    void ShowMapEvents(MapEventProgress[] rows)
    {
        var window = new Window { Title = "Map events", Owner = this, Width = 780, Height = 480, MinWidth = 620, MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var layout = new DockPanel { Margin = new(18) }; var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        heading.Children.Add(UiSections.Heading("Map events", "Current capture",
            "Joined events with their last captured stage and step. Updates without a captured join cannot identify an event reliably. Event completion is separate from dungeon completion. Reopen to refresh."));
        heading.Children.Add(new TextBlock { Text = $"{rows.Length:N0} joined events", Margin = new(0, 0, 0, 12) });
        var table = new LiveTable { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, EnableRowVirtualization = true, RowHeight = 36 };
        table.Columns.Add(new DataGridTextColumn { Header = "Event", Binding = new Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 150 });
        foreach (var (label, field, width) in new[] { ("ID", "EventId", 90), ("Stage", "Stage", 80), ("Step", "Step", 80), ("Status", "Completion", 110) })
            table.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(field), Width = width });
        table.SetRows(rows); layout.Children.Add(table); window.Content = layout; window.Show();
    }
    void ShowGathering()
    {
        var fight = Current;
        var history = archive.Where(f => !f.Demo).Append(LiveSnapshot()).Where(f => !f.Demo).ToArray();
        var window = new Window { Title = "Gathering summary", Owner = this, Width = 680, Height = 480, MinWidth = 520, MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var layout = new DockPanel { Margin = new(18) }; var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        heading.Children.Add(UiSections.Heading("Gathering summary", fight.Label,
            "Confirmed rewarded gathers across characters. Tracking totals are saved locally and survive DPS resets and deleted fights. Reset tracking starts these counters again; fight summaries stay intact. Saved-history totals cover only retained records. Objects must have been seen before gathering; item quantities remain in Loot history. Refresh to see new gathers."));
        var controls = new WrapPanel { Margin = new(0, 0, 0, 12) }; heading.Children.Add(controls);
        var scope = new ComboBox { Width = 220, Margin = new(0, 0, 8, 0), ItemsSource = new[] { "Selected fight", "Saved records and current capture", "Tracking since reset" }, SelectedIndex = 0 };
        var refresh = new Button { Content = "Refresh", Margin = new(0, 0, 8, 0) };
        var reset = new Button { Content = "Reset tracking", IsEnabled = false, ToolTip = "Reset gathering tracking across all characters. Saved fight summaries are unchanged." };
        controls.Children.Add(scope); controls.Children.Add(refresh); controls.Children.Add(reset);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) }; heading.Children.Add(status);
        var table = new LiveTable { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, EnableRowVirtualization = true, RowHeight = 36 };
        table.Columns.Add(new DataGridTextColumn { Header = "Object", Binding = new Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 130 });
        table.Columns.Add(new DataGridTextColumn { Header = "Object ID", Binding = new Binding("ObjectId"), Width = 100 });
        table.Columns.Add(new DataGridTextColumn { Header = "Confirmed gathers", Binding = new Binding("Count"), Width = 140 });
        table.Columns.Add(new DataGridTextColumn { Header = "Records", Binding = new Binding("Records"), Width = 80 });
        void RefreshRows() {
            reset.IsEnabled = scope.SelectedIndex == 2;
            if (scope.SelectedIndex == 2) {
                var tracked = gatheringTracking.Snapshot();
                table.SetRows(tracked.Counts.OrderBy(p => p.Key).Select(p => new { ObjectId = p.Key, Name = WorldObjectNames.Default.Find(p.Key), Count = (decimal)p.Value, Records = "" }).ToArray());
                status.Text = $"{tracked.Counts.Values.Sum(v => (decimal)v):N0} confirmed gathers across all characters since {DateTimeOffset.FromUnixTimeMilliseconds(tracked.Since).ToLocalTime():g}" +
                    (tracked.Omitted > 0 ? $"; {tracked.Omitted:N0} gathers omitted at tracking limits" : "");
                if (gatheringTracking.LoadError != null) status.Text += " " + gatheringTracking.LoadError;
                return;
            }
            var overview = Gathering.Summarize(scope.SelectedIndex == 1 ? history : [fight]);
            table.SetRows(overview.Rows.Select(r => new { r.ObjectId, Name = WorldObjectNames.Default.Find(r.ObjectId), r.Count, r.Records }).ToArray());
            status.Text = $"{overview.Rows.Sum(r => r.Count):N0} confirmed gathers across {overview.Records:N0} " + (overview.Records == 1 ? "record" : "records") +
                (overview.OmittedGathers > 0 ? $"; {overview.OmittedGathers:N0} gathers not included because limits were reached" : "");
        }
        scope.SelectionChanged += (_, _) => RefreshRows();
        refresh.Click += (_, _) => {
            fight = Current;
            history = archive.Where(f => !f.Demo).Append(LiveSnapshot()).Where(f => !f.Demo).ToArray();
            RefreshRows();
        };
        reset.Click += (_, _) => {
            gatheringTracking.Reset(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()); RefreshRows();
            try { gatheringTracking.Save(); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { status.Text += " Reset could not be saved yet; autosave will retry."; }
        };
        RefreshRows();
        layout.Children.Add(table); window.Content = layout; window.Show();
    }
    void ShowWorldObjects()
    {
        WorldObjectReading[] rows; int map;
        lock (engine.Gate) { rows = engine.WorldObjects.Snapshot(); map = engine.WorldObjects.MapId; }
        ShowWorldObjects(rows, map);
    }
    void ShowWorldObjects(WorldObjectReading[] rows, int map)
    {
        var window = new Window { Title = "Nearby world objects", Owner = this, Width = 900, Height = 560, MinWidth = 800, MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var layout = new DockPanel { Margin = new(18) }; var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        heading.Children.Add(UiSections.Heading("Nearby world objects", map > 0 ? $"Map {map}" : "Map not captured",
            "Objects reported in the current capture. Known gathering objects use local names; unknown objects retain their IDs. Disappearance does not prove a successful gather. Reopen to refresh this snapshot."));
        heading.Children.Add(new TextBlock { Text = $"{rows.Length:N0} objects currently tracked", Margin = new(0, 0, 0, 12) });
        var table = new LiveTable { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, EnableRowVirtualization = true, RowHeight = 36 };
        table.Columns.Add(new DataGridTextColumn { Header = "Object", Binding = new Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 130 });
        foreach (var (title, property, width) in new[] { ("Object ID", "ObjectId", 100), ("Entity", "EntityId", 85), ("X", "X", 75), ("Y", "Y", 75), ("Z", "Z", 75), ("Interactable", "CanInteract", 110), ("Phase", "Phase", 65) })
            table.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = width });
        table.SetRows(rows.Select(r => new { r.ObjectId, Name = WorldObjectNames.Default.Find(r.ObjectId), r.EntityId, r.X, r.Y, r.Z, r.CanInteract, r.Phase }).ToArray());
        layout.Children.Add(table); window.Content = layout; window.Show();
    }
}
