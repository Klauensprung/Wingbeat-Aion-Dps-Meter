using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace AionDpsMeter.Local;

internal static class NpcapSetup
{
    internal const string DownloadUrl = "https://npcap.com/#download";

    internal static bool IsLoadFailure(Exception error) =>
        error is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException ||
        (error.InnerException != null && IsLoadFailure(error.InnerException));

    internal static Window CreateDialog(Window owner)
    {
        var dialog = new Window {
            Owner = owner, Title = "Set up Npcap", Width = 490, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false, FontFamily = owner.FontFamily, FontSize = 14,
            Resources = owner.Resources, Background = owner.Background, Foreground = owner.Foreground
        };
        var panel = new StackPanel { Margin = new(24) }; dialog.Content = panel;
        panel.Children.Add(new TextBlock { Text = "Npcap is needed for live capture", FontSize = 20, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock {
            Text = "Install Npcap from its official website, then restart Wingbeat. If Npcap is already installed, repair or reinstall it.\n\nYou can still browse saved fights and use the demo without it.",
            TextWrapping = TextWrapping.Wrap, Margin = new(0, 14, 0, 12)
        });
        panel.Children.Add(new TextBlock { Text = DownloadUrl, Margin = new(0, 0, 0, 16) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var later = new Button { Content = "Not now", IsCancel = true, Padding = new(14, 8, 14, 8), Margin = new(4) };
        later.Click += (_, _) => dialog.Close();
        var download = new Button { Content = "Open download page", IsDefault = true, Padding = new(14, 8, 14, 8), Margin = new(4) };
        download.Click += (_, _) => {
            try { Process.Start(new ProcessStartInfo(DownloadUrl) { UseShellExecute = true }); dialog.Close(); }
            catch { MessageBox.Show(dialog, "Couldn't open your browser. Visit " + DownloadUrl + " to download Npcap.", "Npcap download", MessageBoxButton.OK, MessageBoxImage.Information); }
        };
        buttons.Children.Add(later); buttons.Children.Add(download); panel.Children.Add(buttons);
        return dialog;
    }
}
