using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowWings()
    {
        var fight = Current;
        var rows = fight.Actors.Values.Where(a => a.Wings != null).SelectMany(a => {
            var state = a.Wings!; var owned = state.Owned.ToDictionary(w => w.Id);
            return owned.Keys.Concat(new[] { state.Equipped ?? 0, state.Skin ?? 0 }).Where(id => id > 0).Distinct().Order().Select(id =>
                new WingRow(a.Name, id, owned.TryGetValue(id, out var wing) ? wing.Level.ToString() : "Not captured",
                    state.Equipped == id ? "Equipped" : state.Equipped == null ? "Not captured" : "",
                    state.Skin == id ? "Selected skin" : state.Skin == null ? "Not captured" : ""));
        }).ToArray();
        var window = new Window { Title = global::AionDpsMeter.Local.Localization.T("Wings"), Owner = this, Width = 900, Height = 580, MinWidth = 700, MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var layout = new DockPanel { Margin = new(18) }; var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        heading.Children.Add(UiSections.Heading("Wings", fight.Label,
            "Last captured owned-wing levels and equipment changes. Cosmetic skins are separate from equipped wings. Names and stat bonuses are not yet available. An unobserved equipment choice remains unknown."));
        heading.Children.Add(new TextBlock { Text = rows.Length == 0 ? global::AionDpsMeter.Local.Localization.T("No wings captured in this fight. Enter the game while capture is running.")
            : global::AionDpsMeter.Local.Localization.F($"{rows.Length:N0} wing observations"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) });
        var table = new LiveTable { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, EnableRowVirtualization = true, RowHeight = 36 };
        void Column(string title, string property, int width) => table.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = width });
        Column("Character", "Character", 160); Column("Wing ID", "Id", 100); Column("Level", "Level", 110); Column("Equipment", "Equipment", 120); Column("Cosmetic skin", "Skin", 130);
        table.SetRows(rows); layout.Children.Add(table); window.Content = layout; window.Show();
    }
    sealed record WingRow(string Character, int Id, string Level, string Equipment, string Skin);
}
