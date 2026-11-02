using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowCollections()
    {
        var fight = Current;
        var rows = new List<CharacterCollectionRow>(); int snapshots = 0;
        foreach (var actor in fight.Actors.Values) {
            if (actor.Collections is not { } state) continue;
            void Add(string category, string valueLabel, CollectionList? list) {
                if (list == null) return; snapshots++;
                rows.AddRange(list.Entries.Select(e => new CharacterCollectionRow(actor.Name, category, e.Id, e.Value, valueLabel)));
            }
            if (state.Achievements is { } achievements) {
                snapshots++;
                rows.AddRange(achievements.Completed.Select(id => new CharacterCollectionRow(actor.Name, "Achievements", id, 1,
                    achievements.Complete ? "Completed" : "Completed (partial)")));
            }
            Add("Skins", "Count", state.Skins); Add("Guild decorations", "Equipped slot", state.Decorations); Add("Monoliths", "Tokens", state.Monoliths);
        }
        var window = new Window { Title = "Character collections", Owner = this, Width = 950, Height = 580, MinWidth = 750, MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var layout = new DockPanel { Margin = new(18) }; var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        heading.Children.Add(UiSections.Heading("Character collections", fight.Label,
            "Latest captured skin counts, equipped guild decoration slots, monolith tokens, and completed achievement IDs. IDs are shown until a verified name catalogue is available. These are snapshots, not loot gains."));
        var search = new TextBox { Tag = "Search character, collection or ID...", Margin = new(0, 0, 0, 10) }; heading.Children.Add(search);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) }; heading.Children.Add(status);
        var table = new LiveTable { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, EnableRowVirtualization = true, RowHeight = 36 };
        void Column(string title, string property, int width) => table.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = width });
        Column("Character", "Character", 160); Column("Collection", "Collection", 165); Column("ID", "Id", 100); Column("Value", "Value", 100); Column("Meaning", "Meaning", 145);
        void Filter() {
            string text = search.Text.Trim();
            var shown = rows.Where(r => r.Character.Contains(text, StringComparison.OrdinalIgnoreCase) || r.Collection.Contains(text, StringComparison.OrdinalIgnoreCase) || r.Id.ToString().Contains(text)).ToArray();
            table.SetRows(shown); status.Text = snapshots == 0 ? "No collection snapshots captured. Enter the game while capture is running." : $"{shown.Length:N0} entries shown; {snapshots:N0} collection snapshots captured";
        }
        search.TextChanged += (_, _) => Filter(); Filter(); layout.Children.Add(table); window.Content = layout; window.Show();
    }
    sealed record CharacterCollectionRow(string Character, string Collection, int Id, int Value, string Meaning);
}
