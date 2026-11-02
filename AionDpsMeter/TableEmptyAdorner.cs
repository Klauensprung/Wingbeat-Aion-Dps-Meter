using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace AionDpsMeter.Local;

// Does not replace the table template, collection or virtualized scrolling.
internal sealed class TableEmptyAdorner : Adorner
{
    readonly Grid content = new() { IsHitTestVisible = false, ClipToBounds = true };
    readonly TextBlock title = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = UiTheme.Brush("#E7EDF7") };
    readonly TextBlock hint = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new(0, 6, 0, 0), Foreground = UiTheme.Brush("#93A2B8") };
    internal TableEmptyAdorner(LiveTable table) : base(table)
    {
        IsHitTestVisible = false;
        var message = new StackPanel { MaxWidth = 420, Margin = new(16, 8, 16, 8), VerticalAlignment = VerticalAlignment.Center };
        message.Children.Add(title); message.Children.Add(hint); content.Children.Add(message); AddVisualChild(content);
    }
    internal void Update(string heading, string detail) { title.Text = heading; hint.Text = detail; }
    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => index == 0 ? content : throw new ArgumentOutOfRangeException(nameof(index));
    protected override Size MeasureOverride(Size constraint) { content.Measure(new(Math.Max(0, AdornedElement.RenderSize.Width), Math.Max(0, AdornedElement.RenderSize.Height - 42))); return AdornedElement.RenderSize; }
    protected override Size ArrangeOverride(Size size) { content.Arrange(new Rect(0, Math.Min(42, size.Height), size.Width, Math.Max(0, size.Height - 42))); return size; }
}
