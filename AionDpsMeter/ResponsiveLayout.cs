using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AionDpsMeter.Local;

// Auto-sized controls above a bounded, growing content row. Child order can still
// be composed with Children.Insert without measuring data grids at infinite height.
internal sealed class PageLayout : Grid
{
    internal static readonly DependencyProperty WeightProperty = DependencyProperty.RegisterAttached("Weight", typeof(double), typeof(PageLayout),
        new FrameworkPropertyMetadata(0d));
    internal static T Fill<T>(T child, double weight = 1) where T : UIElement { child.SetValue(WeightProperty, weight); return child; }
    protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
    {
        base.OnVisualChildrenChanged(visualAdded, visualRemoved);
        RowDefinitions.Clear();
        foreach (UIElement child in Children) {
            // WPF temporarily leaves an empty slot while removing a visual.
            if (child is null) continue;
            SetRow(child, RowDefinitions.Count);
            double weight = (double)child.GetValue(WeightProperty);
            RowDefinitions.Add(new() { Height = weight > 0 ? new GridLength(weight, GridUnitType.Star) : GridLength.Auto });
        }
    }
}

// The outer scrollbar is only needed below the view's usable minimum. Supplying
// an actual viewport height lets inner star rows and virtualized grids resize.
internal sealed class ViewportHost : ScrollViewer
{
    readonly FrameworkElement page;
    readonly double minimum;
    internal ViewportHost(FrameworkElement page, double minimum = 260)
    {
        this.page = page; this.minimum = minimum; Content = page;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto; HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        page.Height = minimum;
        Loaded += (_, _) => Fit();
        ScrollChanged += (_, e) => { if (ReferenceEquals(e.OriginalSource, this) && e.ViewportHeightChange != 0) Fit(); };
    }
    void Fit()
    {
        double height = Math.Max(minimum, ViewportHeight);
        if (double.IsFinite(height) && Math.Abs(page.Height - height) > .1) page.Height = height;
    }
}

internal sealed class SplitView : Grid
{
    internal FrameworkElement Upper { get; }
    internal FrameworkElement Lower { get; }
    readonly RowDefinition upperRow, lowerRow;
    readonly double initialShare;
    internal SplitView(FrameworkElement upper, FrameworkElement lower, double upperShare = .5, double upperMinimum = 80, double lowerMinimum = 80)
    {
        Upper = upper; Lower = lower; initialShare = upperShare;
        upperRow = new() { Height = new GridLength(upperShare, GridUnitType.Star), MinHeight = upperMinimum };
        lowerRow = new() { Height = new GridLength(1 - upperShare, GridUnitType.Star), MinHeight = lowerMinimum };
        RowDefinitions.Add(upperRow); RowDefinitions.Add(new() { Height = new GridLength(9) }); RowDefinitions.Add(lowerRow);
        MinHeight = upperMinimum + lowerMinimum + 9;
        SetRow(upper, 0); Children.Add(upper); SetRow(lower, 2); Children.Add(lower);
        var divider = new GridSplitter { Height = 7, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center,
            ResizeDirection = GridResizeDirection.Rows, ResizeBehavior = GridResizeBehavior.PreviousAndNext, Cursor = Cursors.SizeNS,
            ShowsPreview = true, Focusable = true, KeyboardIncrement = 10, ToolTip = "Drag to resize panels. Arrow keys adjust; double-click to restore the default split." };
        divider.SetResourceReference(BackgroundProperty, "Edge");
        System.Windows.Automation.AutomationProperties.SetName(divider, "Resize upper and lower panels");
        divider.MouseDoubleClick += (_, e) => { Reset(); e.Handled = true; };
        SetRow(divider, 1); Children.Add(divider);
        if (lower is Expander expander) {
            GridLength expandedHeight = lowerRow.Height;
            void ExpansionChanged() {
                if (expander.IsExpanded) { lowerRow.Height = expandedHeight; lowerRow.MinHeight = lowerMinimum; RowDefinitions[1].Height = new(9); divider.Visibility = Visibility.Visible; }
                else { if (lowerRow.Height.IsStar) expandedHeight = lowerRow.Height; lowerRow.Height = GridLength.Auto; lowerRow.MinHeight = 0; RowDefinitions[1].Height = new(0); divider.Visibility = Visibility.Collapsed; }
            }
            expander.Expanded += (_, _) => ExpansionChanged(); expander.Collapsed += (_, _) => ExpansionChanged(); ExpansionChanged();
        }
    }
    internal void Reset() { upperRow.Height = new(initialShare, GridUnitType.Star); lowerRow.Height = new(1 - initialShare, GridUnitType.Star); }
    internal static ScrollViewer Scroll(UIElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch };
}
