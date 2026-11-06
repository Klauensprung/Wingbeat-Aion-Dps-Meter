using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowDaevanion()
    {
        var fight = Current;
        var captured = fight.Actors.Values.Where(a => a.Daevanion != null).ToArray();
        var rows = captured.SelectMany(a => a.Daevanion!.Boards.Select(b =>
            new DaevanionRow(a.Name, b.Board, b.Nodes.Length, b.Nodes.Order().ToArray()))).ToArray();
        var window = new Window { Title = global::AionDpsMeter.Local.Localization.T("Daevanion boards"), Owner = this, Width = 900, Height = 580, MinWidth = 700, MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var layout = new DockPanel { Margin = new(18) };
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        heading.Children.Add(UiSections.Heading("Daevanion boards", fight.Label,
            "Last captured board and node IDs. Board names and stat bonuses are not available in this view. Saved fights preserve their captured snapshot."));
        heading.Children.Add(new TextBlock { Text = captured.Length == 0 ? global::AionDpsMeter.Local.Localization.T("No Daevanion snapshot captured. Enter the game while capture is running.")
            : global::AionDpsMeter.Local.Localization.F($"{rows.Length:N0} boards across {captured.Length:N0} captured characters"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) });
        LiveTable Table() => new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
            EnableRowVirtualization = true, RowHeight = 36 };
        var table = Table(); var nodes = Table();
        void Column(LiveTable target, string title, string property, int width) => target.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = width });
        Column(table, "Character", "Character", 170); Column(table, "Board ID", "Board", 100); Column(table, "Nodes", "Count", 80);
        Column(nodes, "Captured node ID", "Id", 180);
        var details = new DockPanel { Margin = new(12, 0, 0, 0) };
        var selected = new TextBlock { Margin = new(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(selected, Dock.Top); details.Children.Add(selected); details.Children.Add(nodes);
        var panels = new Grid(); panels.ColumnDefinitions.Add(new() { Width = new(370) }); panels.ColumnDefinitions.Add(new());
        panels.Children.Add(table); Grid.SetColumn(details, 1); panels.Children.Add(details);
        table.SelectionChanged += (_, _) => {
            var row = table.SelectedItem as DaevanionRow;
            nodes.SetRows(row?.Nodes.Select(id => new DaevanionNodeRow(id)).ToArray() ?? []);
            selected.Text = row == null ? global::AionDpsMeter.Local.Localization.T("Select a board to inspect its nodes.") : row.Count == 0 ? global::AionDpsMeter.Local.Localization.F($"Board {row.Board}: no nodes captured.") : global::AionDpsMeter.Local.Localization.F($"Board {row.Board}: {row.Count:N0} captured nodes");
        };
        selected.Text = global::AionDpsMeter.Local.Localization.T("Select a board to inspect its nodes.");
        table.SetRows(rows); if (rows.Length > 0) table.SelectedIndex = 0;
        layout.Children.Add(panels); window.Content = layout; window.Show();
    }
    sealed record DaevanionRow(string Character, int Board, int Count, int[] Nodes);
    sealed record DaevanionNodeRow(int Id);
}
