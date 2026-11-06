using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowWeeklyPurchases()
    {
        WeeklyPurchaseRow[] ReadRows() {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var characters = accountCharacters.Snapshot().Select(r => (r.Server, r.Character.Name)).ToList();
        var live = LiveSnapshot();
        if (live.Actors.GetValueOrDefault(live.LocalPlayer) is { Player: true, Server: > 0 } local) characters.Add((local.Server, local.Name));
        return ChecklistTasks.Complete(ChecklistTasks.Summarize(shopPurchases.Snapshot(), now).Concat(crafting.Rows(now)), characters, now);
        }
        ShowWeeklyPurchases(ReadRows(), ReadRows);
    }
    void ShowWeeklyPurchases(WeeklyPurchaseRow[] rows, Func<WeeklyPurchaseRow[]>? readRows = null, bool daily = false)
    {
        var window = new Window { Title = daily ? global::AionDpsMeter.Local.Localization.T("Duty missions") : global::AionDpsMeter.Local.Localization.T("Weekly tasks"), Owner = this, Width = 1050, Height = 600, MinWidth = 1000, MinHeight = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var layout = new DockPanel { Margin = new(18) };
        var heading = UiSections.Heading(daily ? "Duty missions" : "Weekly tasks", daily ? "Daily target: 5 rewarded missions" : "Saved purchase and crafting progress",
            "Missing data is Not captured, never an assumed zero. Server-wide tasks are shared; character tasks stay separate. Select a task to enter or clear current-period progress. A later captured update replaces a local entry. Manual changes never alter loot or item counts. Reopen to refresh.");
        DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        if (rows.Length == 0) { var empty = new TextBlock { Text = global::AionDpsMeter.Local.Localization.T("No characters known yet. Start capture and enter the game to populate weekly tasks."), Margin = new(0, 0, 0, 12) }; DockPanel.SetDock(empty, Dock.Top); layout.Children.Add(empty); }
        var searchLabel = new TextBlock { Text = global::AionDpsMeter.Local.Localization.T("Search character, server or task"), Margin = new(0, 0, 0, 4) };
        DockPanel.SetDock(searchLabel, Dock.Top); layout.Children.Add(searchLabel);
        var search = new TextBox { Margin = new(0, 0, 0, 12), ToolTip = global::AionDpsMeter.Local.Localization.T("Search character, server or task") };
        DockPanel.SetDock(search, Dock.Top); layout.Children.Add(search);
        var table = new LiveTable { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, EnableRowVirtualization = true, RowHeight = 36 };
        var controls = new StackPanel { Margin = new(0, 12, 0, 0) }; DockPanel.SetDock(controls, Dock.Bottom); layout.Children.Add(controls);
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; controls.Children.Add(actions);
        actions.Children.Add(new TextBlock { Text = global::AionDpsMeter.Local.Localization.T("Progress"), VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 8, 0) });
        var amount = new TextBox { Width = 70, ToolTip = global::AionDpsMeter.Local.Localization.T("Current-period progress"), Margin = new(0, 0, 8, 0) }; actions.Children.Add(amount);
        var save = new Button { Content = global::AionDpsMeter.Local.Localization.T("Save progress"), IsEnabled = false, Margin = new(0, 0, 8, 0), Padding = new(10, 5, 10, 5) }; actions.Children.Add(save);
        var clear = new Button { Content = global::AionDpsMeter.Local.Localization.T("Clear progress"), IsEnabled = false, Padding = new(10, 5, 10, 5) }; actions.Children.Add(clear);
        var feedback = new TextBlock { Text = global::AionDpsMeter.Local.Localization.T("Select a task to edit its current-period progress."), TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) }; controls.Children.Add(feedback);
        table.SelectionChanged += (_, _) => {
            var row = table.SelectedItem as WeeklyPurchaseRow;
            save.IsEnabled = clear.IsEnabled = row != null && ChecklistTasks.ResetForTask(row.Task, row.Server, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) != null;
            amount.Text = row?.Count?.ToString() ?? "";
            feedback.Text = row == null ? global::AionDpsMeter.Local.Localization.T("Select a task to edit its current-period progress.") : !save.IsEnabled ? global::AionDpsMeter.Local.Localization.T("This server's reset calendar is unknown; manual entry is unavailable.") : global::AionDpsMeter.Local.Localization.F($"Enter 0–{row.Target} for {row.Owner}. This applies to the current reset period.");
        };
        foreach (var (label, field, width) in new[] { ("Server", "Server", 80), ("Owner", "Owner", 140), ("Task", "Task", 220), ("Progress", "Progress", 155), ("Status", "Status", 200), ("Reset", "Reset", 140) })
            table.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(field), Width = width });
        void Filter() { string query = search.Text.Trim(); long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); table.SetRows(rows.Select(r => checklistAdjustments.Apply(r, now)).Where(r => query.Length == 0 || r.Owner.Contains(query, StringComparison.OrdinalIgnoreCase) || r.Task.Contains(query, StringComparison.OrdinalIgnoreCase) || r.Server.ToString().Contains(query)).ToArray()); }
        void Edit(bool remove) {
            if (table.SelectedItem is not WeeklyPurchaseRow selected) return;
            if (!remove && (!int.TryParse(amount.Text, out int parsed) || parsed < 0 || parsed > selected.Target)) { feedback.Text = global::AionDpsMeter.Local.Localization.F($"Enter a whole number from 0 to {selected.Target}."); return; }
            try {
                rows = readRows?.Invoke() ?? rows;
                var current = rows.Single(r => r.Server == selected.Server && r.Owner == selected.Owner && r.Task == selected.Task);
                checklistAdjustments.SetAndSave(current, remove ? null : int.Parse(amount.Text), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                Filter(); feedback.Text = remove ? global::AionDpsMeter.Local.Localization.T("Progress cleared locally until the next captured update.") : global::AionDpsMeter.Local.Localization.T("Progress saved locally for this reset period.");
            } catch (Exception ex) { LocalLog.Write("Checklist adjustment failed: " + ex); feedback.Text = global::AionDpsMeter.Local.Localization.T("Could not save progress: ") + ex.Message; }
        }
        save.Click += (_, _) => Edit(false); clear.Click += (_, _) => Edit(true);
        search.TextChanged += (_, _) => Filter(); Filter(); layout.Children.Add(table); window.Content = layout; window.Show();
    }
}
