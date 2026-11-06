using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AionDpsMeter.Local;

internal sealed class ControlOverlayWindow : Window
{
    readonly TextBlock text = new() { FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = UiTheme.Brush("#FFABB4"),
        TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new(14, 10, 14, 10) };
    long previewUntil;
    bool dragging;
    internal bool PreviewActive => dragging || Environment.TickCount64 < previewUntil;
    internal void Preview() => previewUntil = Environment.TickCount64 + 30000;
    internal ControlOverlayWindow(Preferences settings, Action save)
    {
        Title = global::AionDpsMeter.Local.Localization.T("Wingbeat control alerts"); Width = 330; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        UiTheme.Apply(this, settings); Background = Brushes.Transparent;
        Content = new Border { Child = text, Background = UiTheme.Brush("#E610151F"), BorderBrush = UiTheme.Brush("#A0556575"), BorderThickness = new(1), CornerRadius = new(8) };
        Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - Width) / 2;
        Top = SystemParameters.WorkArea.Top + SystemParameters.WorkArea.Height * .35;
        if (settings.ControlLeft is double left && settings.ControlTop is double top && double.IsFinite(left) && double.IsFinite(top)) {
            Left = Math.Clamp(left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + Math.Max(0, SystemParameters.VirtualScreenWidth - Width));
            Top = Math.Clamp(top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + Math.Max(0, SystemParameters.VirtualScreenHeight - 100));
        }
        MouseLeftButtonDown += (_, e) => {
            if (settings.ControlLocked) return;
            dragging = true; try { DragMove(); e.Handled = true; } finally { dragging = false; }
        };
        var menu = UiTheme.Menu(this);
        var locked = new MenuItem { Header = "Lock position", IsCheckable = true, IsChecked = settings.ControlLocked };
        locked.Click += (_, _) => { settings.ControlLocked = locked.IsChecked; save(); }; menu.Items.Add(locked);
        var preview = new MenuItem { Header = "Preview placement for 30 seconds" }; preview.Click += (_, _) => Preview(); menu.Items.Add(preview);
        var close = new MenuItem { Header = "Disable control alerts" }; close.Click += (_, _) => Close(); menu.Items.Add(close); ContextMenu = menu;
        OverlayPlacement.Track(this, () => { settings.ControlLeft = Left; settings.ControlTop = Top; save(); });
    }
    internal void Update(ControlEffectDisplay[] effects)
    {
        text.Text = effects.Length == 0 ? global::AionDpsMeter.Local.Localization.T("Move control alerts here\n30-second placement preview") : string.Join("\n", effects.Select(e => e.Text));
        text.ToolTip = global::AionDpsMeter.Local.Localization.T("Captured local effects identified by the reference catalogue. Unknown duration stays visible until a removal event. Right-click to lock or disable.");
    }
}
