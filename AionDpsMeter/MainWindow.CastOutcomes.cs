using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowCastOutcomes()
    {
        var fight = Current;
        var summary = CastOutcomes.Build(fight);
        var rows = summary.Rows.Select(r => new { Character = fight.Name(r.ActorId), Skill = skills.GetSkillName(r.SkillCode), r.SkillCode,
            Kind = r.Kind == "charge" ? "Charge" : "Cast", r.Starts, r.Interrupted, r.Cancelled, r.Failed, r.NoStopRecorded }).ToArray();
        var window = new Window { Title = "Cast outcomes", Owner = this, Width = 1120, Height = 540, MinWidth = 900, MinHeight = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var layout = new DockPanel { Margin = new(18) };
        var heading = UiSections.Heading("Cast outcomes", fight.Label,
            "Captured cast and charge starts for this fight. No stop recorded includes actions that may have completed, remained pending, or had a missing stop packet; it is not a success count. Reopen to refresh a live fight.");
        DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        var search = new TextBox { Tag = "Search character or skill...", ToolTip = "Search character, skill name, skill ID or cast type", Margin = new(0, 0, 0, 10) };
        DockPanel.SetDock(search, Dock.Top); layout.Children.Add(search);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) };
        DockPanel.SetDock(status, Dock.Top); layout.Children.Add(status);
        var table = new LiveTable { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, EnableRowVirtualization = true, RowHeight = 34 };
        table.Columns.Add(new DataGridTextColumn { Header = "Character", Binding = new Binding("Character"), Width = 140 });
        table.Columns.Add(new DataGridTextColumn { Header = "Skill", Binding = new Binding("Skill"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 165 });
        foreach (var (label, field, width) in new[] { ("Type", "Kind", 70), ("Starts", "Starts", 65), ("Interrupted", "Interrupted", 110),
            ("Cancelled", "Cancelled", 85), ("Failed", "Failed", 65), ("No stop recorded", "NoStopRecorded", 135), ("Skill ID", "SkillCode", 95) })
            table.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(field), Width = width });
        void Refresh() {
            string query = search.Text.Trim();
            var selected = rows.Where(r => query.Length == 0 || r.Character.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.Skill.Contains(query, StringComparison.OrdinalIgnoreCase) || r.SkillCode.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.Kind.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            table.SetRows(selected);
            status.Text = rows.Length == 0 ? "No cast starts captured for this fight." : $"{selected.Length:N0} entries · {selected.Sum(r => (long)r.Starts):N0} starts · {selected.Sum(r => (long)r.Interrupted):N0} interrupted";
            if (summary.OmittedStarts > 0) status.Text += $" · {summary.OmittedStarts:N0} starts could not be attributed or retained.";
        }
        search.TextChanged += (_, _) => Refresh(); Refresh();
        layout.Children.Add(table); window.Content = layout; window.Show();
    }
}
