using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    Button? updateAvailable;
    ReleaseUpdate? latestRelease;
    bool checkingRelease;
    void InitializeUpdates(Panel controls, bool demo)
    {
        updateAvailable = IconButton(UiIcon.Refresh, "Update available", OpenReleasePage);
        updateAvailable.Padding = new(8, 4, 8, 4); updateAvailable.MinHeight = 26; updateAvailable.FontSize = 11;
        updateAvailable.Visibility = Visibility.Collapsed;
        updateAvailable.SetResourceReference(Control.ForegroundProperty, "Accent");
        controls.Children.Add(updateAvailable);
        if (!demo) ContentRendered += CheckReleaseOnStartup;
    }
    async void CheckReleaseOnStartup(object? sender, EventArgs args)
    {
        ContentRendered -= CheckReleaseOnStartup;
        await CheckForUpdates(false);
    }
    async Task CheckForUpdates(bool showResult)
    {
        if (checkingRelease) return;
        checkingRelease = true;
        try {
            var result = await ReleaseUpdates.CheckAsync();
            if (shuttingDown) return;
            ApplyReleaseResult(result);
            if (showResult) {
                if (result.Available) {
                    if (MessageBox.Show(this, $"Wingbeat {result.Version} is available (installed: {ReleaseUpdates.CurrentLabel}).\nOpen the GitHub download page?", "Wingbeat update", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                        OpenReleasePage();
                } else MessageBox.Show(this, $"You are up to date. Installed: {ReleaseUpdates.CurrentLabel}.", "Wingbeat update", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex) {
            LocalLog.Write("GitHub release check failed: " + ex.Message);
            if (showResult && !shuttingDown) MessageBox.Show(this, "Could not check GitHub for updates. Please try again later.", "Wingbeat update", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally { checkingRelease = false; }
    }
    void OpenReleasePage()
    {
        if (latestRelease is not { Available: true } release) return;
        // The browser handles the download. Wingbeat never retrieves an asset,
        // replaces binaries, starts an installer or changes the running version.
        Process.Start(new ProcessStartInfo(release.Page.AbsoluteUri) { UseShellExecute = true });
    }
    void ApplyReleaseResult(ReleaseUpdate result)
    {
        latestRelease = result;
        updateAvailable!.Visibility = result.Available ? Visibility.Visible : Visibility.Collapsed;
        updateAvailable.ToolTip = $"Version {result.Version} is available. Open its GitHub download page in your browser.";
    }
    void CheckReleaseUi()
    {
        var previous = latestRelease;
        double width = Width;
        try {
            ApplyReleaseResult(new("99.0.0", true, new($"https://github.com/{ReleaseUpdates.Repository}/releases/tag/99.0.0")));
            Width = MinWidth; UpdateLayout();
            var position = updateAvailable!.TranslatePoint(new Point(), this);
            if (!updateAvailable.IsVisible || updateAvailable.ActualWidth < 60 || position.X < 0 || position.X + updateAvailable.ActualWidth > ActualWidth)
                throw new InvalidOperationException("The GitHub update link is not usable at minimum window width.");
            Render(this, "test-artifacts/Update-available.png");
            ApplyReleaseResult(new(ReleaseUpdates.CurrentLabel, false, new($"https://github.com/{ReleaseUpdates.Repository}/releases/latest")));
            if (updateAvailable.Visibility != Visibility.Collapsed || !CreateMoreMenu().Items.OfType<MenuItem>().Any(i => i.Header?.ToString() == "Check for updates…"))
                throw new InvalidOperationException("The update state or manual release check is missing.");
        }
        finally {
            latestRelease = previous; updateAvailable!.Visibility = previous?.Available == true ? Visibility.Visible : Visibility.Collapsed;
            Width = width; UpdateLayout();
        }
    }
}
