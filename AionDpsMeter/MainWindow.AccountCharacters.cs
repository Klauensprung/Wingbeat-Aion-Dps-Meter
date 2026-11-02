using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowAccountCharacters() => ShowAccountCharacters(accountCharacters.Snapshot());
    void ShowAccountCharacters(SavedAccountCharacter[] characters)
    {
        var rows = characters.Select(r => new { Source = r, r.Server, r.Character.Id, r.Character.Name, Class = r.CapturedJob ?? CapturedClass.Name(r.Character.Template),
            r.Character.Level, r.Character.Guild, CP = r.Character.CombatPower, GearScore = r.Character.GearScore?.ToString() ?? "Unknown",
            Captured = DateTimeOffset.FromUnixTimeMilliseconds(r.Timestamp).ToLocalTime().ToString("dd MMM HH:mm") }).ToArray();
        var window = new Window { Title = "Saved characters", Owner = this, Width = 1050, Height = 520, MinWidth = 900, MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var layout = new DockPanel { Margin = new(18) };
        var heading = UiSections.Heading("Saved characters", $"{rows.Length} last-known characters",
            "Character details captured in-game and saved on this PC. Values can change in-game; the capture time shows when each row was last updated. Missing characters stay as last-known records. Reopen to refresh.");
        DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        if (rows.Length == 0) { var empty = new TextBlock { Text = "No login roster captured yet. Start capture before logging into the game.", Margin = new(0, 0, 0, 12) }; DockPanel.SetDock(empty, Dock.Top); layout.Children.Add(empty); }
        var table = new LiveTable { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, EnableRowVirtualization = true, RowHeight = 36 };
        foreach (var (label, field, width) in new[] { ("Character", "Name", 160), ("Server", "Server", 80), ("Class", "Class", 105), ("Level", "Level", 80), ("Gear score", "GearScore", 95), ("CP", "CP", 100), ("Guild", "Guild", 150), ("Captured", "Captured", 140), ("Character ID", "Id", 160) })
            table.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(field), Width = width });
        var appearance = new Button { Content = "View appearance", IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Left, Padding = new(12, 6, 12, 6), Margin = new(0, 0, 0, 10) };
        DockPanel.SetDock(appearance, Dock.Top); layout.Children.Add(appearance);
        table.SelectionChanged += (_, _) => appearance.IsEnabled = rows.FirstOrDefault(r => ReferenceEquals(r, table.SelectedItem)) is { } selected && SavedAppearance.View(selected.Source) != null;
        appearance.Click += (_, _) => {
            var row = rows.FirstOrDefault(r => ReferenceEquals(r, table.SelectedItem))?.Source;
            if (row == null || SavedAppearance.View(row) is not { } snapshot) return;
            ShowAppearancePresets(() => snapshot);
        };
        table.SetRows(rows); layout.Children.Add(table); window.Content = layout; window.Show();
    }
}
