using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace AionDpsMeter.Local;

internal sealed class SkillDamageDisplay : IValueConverter
{
    internal static SkillRow[] Shares(IEnumerable<SkillRow> source)
    {
        var rows = source.ToArray(); double total = rows.Sum(r => r.Damage);
        return rows.Select(r => r with { DamageShare = total > 0 ? 100d * r.Damage / total : 0 }).ToArray();
    }
    internal static HealingRow[] Shares(IEnumerable<HealingRow> source)
    {
        var rows = source.ToArray(); double total = rows.Sum(r => r.Healing);
        return rows.Select(r => r with { HealingShare = total > 0 ? 100d * r.Healing / total : 0 }).ToArray();
    }
    internal static void Apply(DataGrid grid, string accent, string shareProperty = nameof(SkillRow.DamageShare))
    {
        grid.Background = Brushes.Transparent;
        grid.RowBackground = grid.AlternatingRowBackground = Brushes.Transparent;
        var style = new Style(typeof(DataGridRow));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Text")));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new Binding(shareProperty) { Converter = new SkillDamageDisplay(), ConverterParameter = accent }));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        foreach (var property in new[] { UIElement.IsMouseOverProperty, DataGridRow.IsSelectedProperty }) {
            var trigger = new Trigger { Property = property, Value = true };
            trigger.Setters.Add(new Setter(Control.BorderBrushProperty, UiTheme.Brush(accent)));
            style.Triggers.Add(trigger);
        }
        grid.RowStyle = style;
        grid.Resources["SkillDamageAccent"] = accent;
        grid.Resources["ContributionProperty"] = shareProperty;
        grid.LoadingRow -= BindRow; grid.LoadingRow += BindRow;
        for (int i = 0; i < grid.Items.Count; i++)
            if (grid.ItemContainerGenerator.ContainerFromIndex(i) is DataGridRow row) BindBackground(grid, row);
    }
    static void BindRow(object? sender, DataGridRowEventArgs e) => BindBackground((DataGrid)sender!, e.Row);
    static void BindBackground(DataGrid grid, DataGridRow row)
    {
        // A local binding takes precedence over DataGrid's transferred row backgrounds.
        row.SetBinding(Control.BackgroundProperty, new Binding((string)grid.Resources["ContributionProperty"]) {
            Converter = new SkillDamageDisplay(), ConverterParameter = grid.Resources["SkillDamageAccent"]
        });
    }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double fraction = value is double share && double.IsFinite(share) ? Math.Clamp(share / 100, 0, 1) : 0;
        var color = (Color)ColorConverter.ConvertFromString(parameter as string ?? "#55D6BE");
        var group = new DrawingGroup();
        using (var drawing = group.Open()) {
            // Keep full relative bounds even when the damage bar is short or empty.
            drawing.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 1, 1));
            if (fraction > 0) drawing.DrawRectangle(new SolidColorBrush(Color.FromArgb(65, color.R, color.G, color.B)), null, new Rect(0, 0, fraction, 1));
        }
        var brush = new DrawingBrush(group) { Stretch = Stretch.Fill }; brush.Freeze(); return brush;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
