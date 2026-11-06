using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowVisibleEquipment()
    {
        var fight = Current; var rows = VisibleEquipment.Rows(fight);
        var window = new Window { Title = global::AionDpsMeter.Local.Localization.T("Visible equipment"), Owner = this, Width = 1050, Height = 580, MinWidth = 700, MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var layout = new DockPanel { Margin = new(18) }; var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        heading.Children.Add(UiSections.Heading("Visible equipment", fight.Label,
            "Last captured appearance items and skins. These slots do not describe a complete stat-bearing gear build. Partial updates may omit unchanged items."));
        var search = new TextBox { Tag = "Search character, item or skin...", Margin = new(0, 0, 0, 10) }; heading.Children.Add(search);
        var status = new TextBlock { Margin = new(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap }; heading.Children.Add(status);
        var table = new LiveTable { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
            EnableRowVirtualization = true, EnableColumnVirtualization = true, RowHeight = 36, HeadersVisibility = DataGridHeadersVisibility.Column };
        void Column(string title, string property, double width) => table.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = width });
        Column("Character", "Character", 150); Column("Item", "Item", 220); Column("Enchant", "Enchant", 90);
        Column("Skin", "Skin", 200); Column("Dyes", "Dyes", 210); Column("Slot", "Slot", 110); Column("Visibility", "Visibility", 80); Column("Capture", "Coverage", 190);
        void Filter() {
            string text = search.Text.Trim();
            var shown = rows.Where(r => r.Character.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                r.Item.Contains(text, StringComparison.OrdinalIgnoreCase) || r.Skin.Contains(text, StringComparison.OrdinalIgnoreCase)).ToArray();
            table.SetRows(shown); status.Text = rows.Length == 0 ? global::AionDpsMeter.Local.Localization.T("No appearance equipment captured. Enter a zone while capturing, or open a saved fight with equipment data.")
                : global::AionDpsMeter.Local.Localization.F($"{shown.Length:N0} items · last captured snapshot");
        }
        search.TextChanged += (_, _) => Filter(); layout.Children.Add(table); window.Content = layout; Filter(); window.Show();
    }
}
