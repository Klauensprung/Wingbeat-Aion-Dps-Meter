using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace AionDpsMeter.Local;

internal static class UiSections
{
    internal static StackPanel Heading(string title, string subtitle, string? details = null)
    {
        var panel = new StackPanel { Margin = new(0, 0, 0, 16) };
        panel.Children.Add(new TextBlock { Text = Localization.T(title), FontSize = 24, FontWeight = FontWeights.SemiBold });
        var summary = new TextBlock { Text = Localization.T(subtitle), TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 0), FontSize = 12 };
        summary.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); panel.Children.Add(summary);
        if (!string.IsNullOrWhiteSpace(details)) {
            var text = new TextBlock { Text = Localization.T(details), TextWrapping = TextWrapping.Wrap, Margin = new(0, 2, 0, 8), FontSize = 12 };
            text.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            panel.Children.Add(new Expander { Header = "How this works", Content = text, Margin = new(0, 6, 0, 0) });
        }
        return panel;
    }

    internal static FrameworkElement Field(string label, Control control, double width = 230)
    {
        var panel = new StackPanel { Width = width, Margin = new(0, 0, 12, 10) };
        var caption = new Label { Content = Localization.T(label), Target = control, Padding = new(0, 0, 0, 5), FontSize = 11 };
        caption.SetResourceReference(Control.ForegroundProperty, "Muted");
        AutomationProperties.SetName(control, label);
        control.Margin = new(0); control.MinHeight = 32;
        panel.Children.Add(caption); panel.Children.Add(control); return panel;
    }

    internal static Border TableSection(string title, UIElement table)
    {
        var panel = new DockPanel();
        var heading = new TextBlock { Text = Localization.T(title), FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 10) };
        DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading); panel.Children.Add(table);
        return UiTheme.Card(panel);
    }
}
