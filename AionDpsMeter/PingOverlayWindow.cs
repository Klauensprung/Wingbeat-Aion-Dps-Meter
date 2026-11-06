using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AionDpsMeter.Local;

internal sealed class PingOverlayWindow : Window
{
    readonly PingView view = new() { Margin = new(10, 8, 10, 8) };
    internal PingOverlayWindow(Preferences settings, Action save)
    {
        Title = global::AionDpsMeter.Local.Localization.T("Wingbeat ping"); SizeToContent = SizeToContent.WidthAndHeight; ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        UiTheme.Apply(this, settings); Background = Brushes.Transparent;
        Content = new Border { Child = view, Background = UiTheme.Brush("#E610151F"), CornerRadius = new(7) };
        if (settings.PingLeft is double left && settings.PingTop is double top && double.IsFinite(left) && double.IsFinite(top)) {
            Left = Math.Clamp(left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + Math.Max(0, SystemParameters.VirtualScreenWidth - 240));
            Top = Math.Clamp(top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + Math.Max(0, SystemParameters.VirtualScreenHeight - 40));
        }
        MouseLeftButtonDown += (_, e) => { if (!settings.PingLocked) { DragMove(); e.Handled = true; } };
        var menu = UiTheme.Menu(this);
        var locked = new MenuItem { Header = "Lock position", IsCheckable = true, IsChecked = settings.PingLocked };
        locked.Click += (_, _) => { settings.PingLocked = locked.IsChecked; save(); }; menu.Items.Add(locked);
        var close = new MenuItem { Header = "Close ping overlay" }; close.Click += (_, _) => Close(); menu.Items.Add(close); ContextMenu = menu;
        OverlayPlacement.Track(this, () => { settings.PingLeft = Left; settings.PingTop = Top; save(); });
    }
    internal void Update(PingReading? sample, long now) => view.Update(sample, now);
}
