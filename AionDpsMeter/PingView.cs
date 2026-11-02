using System.Windows;
using System.Windows.Controls;

namespace AionDpsMeter.Local;

internal sealed class PingView : StackPanel
{
    readonly TextBlock label = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new(7, 0, 0, 0) };
    readonly Border[] bars = new Border[4];
    PingDisplay? displayed;
    internal PingView()
    {
        Orientation = Orientation.Horizontal;
        for (int i = 0; i < bars.Length; i++) {
            bars[i] = new Border { Width = 4, Height = 4 + 3 * i, CornerRadius = new(1), Margin = new(1, 0, 1, 0), VerticalAlignment = VerticalAlignment.Bottom };
            Children.Add(bars[i]);
        }
        Children.Add(label);
    }
    internal void Update(PingReading? sample, long now)
    {
        var next = PingPresentation.Build(sample, now);
        if (next == displayed) return;
        displayed = next; label.Text = next.Text; label.Foreground = UiTheme.Brush(next.Color); ToolTip = next.Detail;
        for (int i = 0; i < bars.Length; i++) bars[i].Background = UiTheme.Brush(i < next.Bars ? next.Color : "#425168");
        System.Windows.Automation.AutomationProperties.SetName(this, next.Text);
    }
}
