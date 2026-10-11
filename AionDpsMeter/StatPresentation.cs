using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace AionDpsMeter.Local;

// Original vector badges, bundled in code and available for every stat ID.
internal sealed class StatPresentation : IValueConverter
{
    static readonly Dictionary<string, ImageSource> Icons = new();
    static (string Shape, string Color) Symbol(int id) => id switch {
        19 or 317 or 31 or 33 or 425 or 56 or 50 => ("M 5,19 L 18,6 19,3 16,4 3,17 M 3,13 L 10,20 M 3,20 L 6,17", "#EABF70"),
        282 => ("M 14,2 L 5,13 11,13 9,22 20,9 13,9 Z", "#69CCF0"),
        44 or 442 or 443 => ("M 12,2 L 14,9 22,12 14,15 12,22 9,15 2,12 9,9 Z", "#C29AF0"),
        483 or 485 or 487 or 489 => ("M 5,19 C 2,7 10,3 21,3 C 21,14 16,22 5,19 M 5,19 L 16,8", "#91CC69"),
        28 or 32 or 379 or 520 or 589 or 102 => ("M 4,19 L 4,14 8,14 8,19 Z M 10,19 L 10,9 14,9 14,19 Z M 16,19 L 16,4 20,4 20,19 Z", "#55D6BE"),
        _ => ("M 4,6 L 20,6 M 4,12 L 20,12 M 4,18 L 20,18 M 8,3 L 8,9 M 16,9 L 16,15 M 10,15 L 10,21", "#93A2B8")
    };
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var (shape, color) = Symbol(value is int id ? id : 0);
        if (Icons.TryGetValue(shape, out var cached)) return cached;
        var group = new DrawingGroup();
        using (var drawing = group.Open()) {
            var tint = (Color)ColorConverter.ConvertFromString(color);
            drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(25, tint.R, tint.G, tint.B)), null, new Rect(-5, -5, 34, 34), 7, 7);
            drawing.DrawGeometry(null, new Pen(new SolidColorBrush(tint), 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, Geometry.Parse(shape));
        }
        var image = new DrawingImage(group); image.Freeze(); Icons[shape] = image; return image;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    internal static DataGridColumn Column(string property)
    {
        var row = new FrameworkElementFactory(typeof(DockPanel));
        row.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        if (property == "Stat") {
            var icon = new FrameworkElementFactory(typeof(Image));
            icon.SetValue(FrameworkElement.WidthProperty, 34d); icon.SetValue(FrameworkElement.HeightProperty, 34d);
            icon.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 12, 0)); icon.SetValue(DockPanel.DockProperty, Dock.Left);
            icon.SetBinding(Image.SourceProperty, new Binding("StatId") { Converter = new StatPresentation() }); row.AppendChild(icon);
            var text = new FrameworkElementFactory(typeof(StackPanel));
            var name = new FrameworkElementFactory(typeof(TextBlock)); name.SetBinding(TextBlock.TextProperty, new Binding("Stat"));
            name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); name.SetValue(TextBlock.FontSizeProperty, 13d); text.AppendChild(name);
            var detail = new FrameworkElementFactory(typeof(TextBlock)); detail.SetBinding(TextBlock.TextProperty, new Binding("Detail"));
            detail.SetValue(TextBlock.FontSizeProperty, 10d); detail.SetValue(TextBlock.ForegroundProperty, UiTheme.Brush("#93A2B8")); text.AppendChild(detail); row.AppendChild(text);
        } else {
            var number = new FrameworkElementFactory(typeof(TextBlock)); number.SetBinding(TextBlock.TextProperty, new Binding("Value"));
            number.SetValue(TextBlock.FontSizeProperty, 19d); number.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            number.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Right); number.SetValue(TextBlock.ForegroundProperty, UiTheme.Brush("#75DCC8")); row.AppendChild(number);
        }
        return new DataGridTemplateColumn { Header = property, SortMemberPath = property, MinWidth = property == "Stat" ? 260 : 140,
            Width = new DataGridLength(property == "Stat" ? 3 : 1, DataGridLengthUnitType.Star), CellTemplate = new DataTemplate { VisualTree = row } };
    }
}
