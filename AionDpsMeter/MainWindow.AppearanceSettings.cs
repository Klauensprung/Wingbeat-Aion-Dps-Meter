using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    sealed record AppearanceSettingRow(string Group, string Field, string Setting, string Value, Brush Swatch, bool IsColor);
    void ShowAppearanceSettings(string label, string data, Window? owner = null, string? gender = null)
    {
        var window = new Window { Title = "Appearance colors and settings", Owner = owner ?? this, Width = 820, Height = 600,
            MinWidth = 650, MinHeight = 430, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var layout = new DockPanel { Margin = new(18) };
        var heading = UiSections.Heading("Colors and settings", label,
            "Colors can be copied as RGB hex values. Known sliders use the creator's 0–100 scale. Choices and toggles show their captured values; unidentified numbers are marked raw. This view does not change your character.");
        DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        AppearanceSettingRow[] rows = []; string? error = null;
        try {
            rows = AppearanceSettings.Decode(data).Select(r => AppearancePalette.Resolve(r, gender)).Select(r => {
                Brush swatch = r.Color == null ? Brushes.Transparent : (Brush)new BrushConverter().ConvertFromString(r.Color)!;
                if (swatch.CanFreeze) swatch.Freeze();
                string name = AppearanceDisplay.Name(r, gender);
                return new AppearanceSettingRow(r.Group, r.Field, name, AppearanceDisplay.Value(r), swatch, r.Color != null);
            }).ToArray();
        } catch (InvalidDataException) { error = "This appearance data is incomplete or uses an unsupported format. Its original data is still available in the preset export."; }
        var search = new TextBox { Tag = "Search color or setting...", ToolTip = "Search setting name, field or color value", Margin = new(0, 0, 0, 10) };
        DockPanel.SetDock(search, Dock.Top); layout.Children.Add(search);
        var filters = new WrapPanel { Margin = new(0, 0, 0, 10) };
        var group = new ComboBox { ItemsSource = new[] { "Colors", "All settings", "Body", "Face", "Customize" }, SelectedIndex = 0, Width = 180, Margin = new(0, 0, 10, 0) };
        var copy = new Button { Content = "Copy value", IsEnabled = false, Padding = new(12, 6, 12, 6) };
        filters.Children.Add(group); filters.Children.Add(copy); DockPanel.SetDock(filters, Dock.Top); layout.Children.Add(filters);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) };
        DockPanel.SetDock(status, Dock.Top); layout.Children.Add(status);
        var table = new LiveTable { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, EnableRowVirtualization = true, RowHeight = 34 };
        var swatchCell = new FrameworkElementFactory(typeof(Border));
        swatchCell.SetValue(Border.WidthProperty, 28d); swatchCell.SetValue(Border.HeightProperty, 20d);
        swatchCell.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        swatchCell.SetBinding(Border.BackgroundProperty, new Binding("Swatch"));
        table.Columns.Add(new DataGridTemplateColumn { Header = "Color", Width = 65, CellTemplate = new DataTemplate { VisualTree = swatchCell } });
        table.Columns.Add(new DataGridTextColumn { Header = "Group", Binding = new Binding("Group"), Width = 100 });
        table.Columns.Add(new DataGridTextColumn { Header = "Setting", Binding = new Binding("Setting"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 240 });
        table.Columns.Add(new DataGridTextColumn { Header = "Value", Binding = new Binding("Value"), Width = 170 });
        table.SelectionChanged += (_, _) => copy.IsEnabled = table.SelectedItem is AppearanceSettingRow;
        void Refresh() {
            string query = search.Text.Trim(), category = group.SelectedItem?.ToString() ?? "Colors";
            var selected = rows.Where(r => (category == "All settings" || category == "Colors" && r.IsColor || category == r.Group) &&
                (query.Length == 0 || r.Setting.Contains(query, StringComparison.OrdinalIgnoreCase) || r.Field.Contains(query, StringComparison.OrdinalIgnoreCase) || r.Value.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
            table.SetRows(selected);
            status.Text = error ?? $"{selected.Length} settings / {rows.Count(r => r.IsColor)} captured colors";
        }
        copy.Click += (_, _) => {
            if (table.SelectedItem is not AppearanceSettingRow row) return;
            try { Clipboard.SetText(row.Value); status.Text = $"Copied {row.Value}"; }
            catch (System.Runtime.InteropServices.ExternalException) { status.Text = "Clipboard is busy. Please try again."; }
        };
        search.TextChanged += (_, _) => Refresh(); group.SelectionChanged += (_, _) => Refresh(); Refresh();
        layout.Children.Add(table); window.Content = layout; window.Show();
    }
}
