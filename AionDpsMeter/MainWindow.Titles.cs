using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowTitles()
    {
        var fight = Current;
        var captured = fight.Actors.Values.Where(a => a.Titles != null).ToArray();
        var rows = captured.SelectMany(a => {
            var state = a.Titles!; var owned = state.Owned.ToHashSet();
            return owned.Concat(state.Equipped.Select(t => t.Id)).Where(id => id > 0).Distinct().Order().Select(id =>
                new TitleRow(a.Name, id, owned.Contains(id) ? "Owned" : "Not in owned list",
                    string.Join(", ", state.Equipped.Where(t => t.Id == id).Select(t => Titles.Slot(t.Slot)))));
        }).ToArray();
        var window = new Window { Title = "Titles", Owner = this, Width = 850, Height = 580, MinWidth = 700, MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var layout = new DockPanel { Margin = new(18) }; var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        heading.Children.Add(UiSections.Heading("Titles", fight.Label,
            "Captured title ownership and Attack, Defense, and Other equipment slots. Title names and bonuses are not yet available. An empty captured list differs from no snapshot."));
        var search = new TextBox { Tag = "Search character, title ID or equipped slot...", Margin = new(0, 0, 0, 10) }; heading.Children.Add(search);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) }; heading.Children.Add(status);
        var table = new LiveTable { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, EnableRowVirtualization = true, RowHeight = 36 };
        void Column(string title, string property, int width) => table.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = width });
        Column("Character", "Character", 170); Column("Title ID", "Id", 100); Column("Ownership", "Ownership", 150); Column("Equipped slots", "Slots", 200);
        void Filter() {
            string text = search.Text.Trim();
            var shown = rows.Where(r => r.Character.Contains(text, StringComparison.OrdinalIgnoreCase) || r.Id.ToString().Contains(text) || r.Slots.Contains(text, StringComparison.OrdinalIgnoreCase)).ToArray();
            table.SetRows(shown); status.Text = captured.Length == 0 ? "No title snapshot captured. Enter the game while capture is running."
                : $"{shown.Length:N0} titles shown; {captured.Length:N0} character snapshots captured";
        }
        search.TextChanged += (_, _) => Filter(); Filter(); layout.Children.Add(table); window.Content = layout; window.Show();
    }
    sealed record TitleRow(string Character, int Id, string Ownership, string Slots);
}
