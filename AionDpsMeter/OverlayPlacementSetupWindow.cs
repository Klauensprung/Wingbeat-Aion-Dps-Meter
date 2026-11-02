using System.Windows;
using System.Windows.Controls;

namespace AionDpsMeter.Local;

internal sealed class OverlayPlacementSetupWindow : Window
{
    internal OverlayPlacementSetupWindow(Preferences settings, Action finish, Action later)
    {
        Title = "Arrange your overlays"; Width = 440; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UiTheme.Apply(this, settings);
        var panel = new StackPanel { Margin = new(24) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "Make Wingbeat fit your screen", FontSize = 21,
            FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0,0,0,16) });
        panel.Children.Add(new TextBlock { Text = "1. Drag the DPS meter by its header. Resize it from a corner.\n\n2. Drag the boss-alert preview where you want warnings to appear.",
            TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new(0,0,0,16) });
        panel.Children.Add(new TextBlock { Text = "Done saves both positions and keeps boss alerts enabled. The alert preview disappears; real alerts appear only when needed. You can arrange them again in Settings → Startup.",
            TextWrapping = TextWrapping.Wrap, Margin = new(0,0,0,18) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var skip = new Button { Content = "Later", Padding = new(16,8,16,8), Margin = new(0,0,8,0) };
        skip.Click += (_,_) => later(); buttons.Children.Add(skip);
        var done = new Button { Content = "Done — save positions", Padding = new(16,8,16,8), IsDefault = true };
        done.Click += (_,_) => finish(); buttons.Children.Add(done); panel.Children.Add(buttons);
    }
}
