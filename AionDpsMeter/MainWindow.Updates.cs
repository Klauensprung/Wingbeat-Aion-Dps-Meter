using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    Button? updateAvailable;
    ReleaseUpdate? latestRelease;
    bool checkingRelease;
    Velopack.UpdateManager? appUpdater;
    Velopack.UpdateInfo? downloadableUpdate;
    Velopack.VelopackAsset? downloadedUpdate;
    bool downloadingUpdate, restartForUpdate;
    void InitializeUpdates(Panel controls, bool demo)
    {
        updateAvailable = IconButton(UiIcon.Refresh, global::AionDpsMeter.Local.Localization.T("Update available"), UpdateApplication);
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
        if (checkingRelease || downloadingUpdate) return;
        checkingRelease = true;
        try {
            var result = await ReleaseUpdates.CheckAsync();
            if (shuttingDown) return;
            ApplyReleaseResult(result);
            downloadableUpdate = null;
            if (result.Available) {
                try {
                    appUpdater ??= new Velopack.UpdateManager(new Velopack.Sources.GithubSource("https://github.com/" + ReleaseUpdates.Repository, null, false));
                    if (appUpdater.IsInstalled) {
                        var candidate = await appUpdater.CheckForUpdatesAsync();
                        if (shuttingDown) return;
                        // Only offer the same stable version validated by our release check.
                        downloadableUpdate = candidate != null && ReleaseUpdates.ValidPackage(candidate.TargetFullRelease, result.Version) ? candidate : null;
                    }
                } catch (Exception ex) { LocalLog.Write("Update package check failed: " + ex.Message); }
            }
            if (showResult) {
                if (result.Available) {
                    if (downloadableUpdate != null || downloadedUpdate != null) { UpdateApplication(); return; }
                    if (MessageBox.Show(this, global::AionDpsMeter.Local.Localization.F($"Wingbeat {result.Version} is available (installed: {ReleaseUpdates.CurrentLabel}).\nOpen the GitHub download page?"), global::AionDpsMeter.Local.Localization.T("Wingbeat update"), MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                        OpenReleasePage();
                } else MessageBox.Show(this, global::AionDpsMeter.Local.Localization.F($"You are up to date. Installed: {ReleaseUpdates.CurrentLabel}."), global::AionDpsMeter.Local.Localization.T("Wingbeat update"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex) {
            LocalLog.Write("GitHub release check failed: " + ex.Message);
            if (showResult && !shuttingDown) MessageBox.Show(this, global::AionDpsMeter.Local.Localization.T("Could not check GitHub for updates. Please try again later."), global::AionDpsMeter.Local.Localization.T("Wingbeat update"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally { checkingRelease = false; }
    }
    void OpenReleasePage()
    {
        if (latestRelease is not { Available: true } release) return;
        // Legacy portable builds keep the browser download path.
        Process.Start(new ProcessStartInfo(release.Page.AbsoluteUri) { UseShellExecute = true });
    }
    async void UpdateApplication()
    {
        if (downloadingUpdate || shuttingDown) return;
        if (downloadedUpdate != null) {
            if (MessageBox.Show(this, Localization.T("Save your current session and restart Wingbeat to install the update?"), Localization.T("Wingbeat update"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            restartForUpdate = true;
            Close(); // The normal Closing handler must save successfully before arming the updater.
            if (!shuttingDown) restartForUpdate = false;
            return;
        }
        if (downloadableUpdate == null || appUpdater == null) { OpenReleasePage(); return; }
        if (MessageBox.Show(this, Localization.T("Download the update from GitHub? You can keep playing and restart when ready."), Localization.T("Wingbeat update"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        downloadingUpdate = true;
        updateAvailable!.IsEnabled = false;
        try {
            var target = downloadableUpdate;
            await appUpdater.DownloadUpdatesAsync(target, percent => Dispatcher.BeginInvoke(new Action(() => {
                if (!shuttingDown && downloadedUpdate == null) updateAvailable.Content = Localization.F($"Downloading update: {percent}%");
            })));
            if (shuttingDown) return;
            downloadedUpdate = target.TargetFullRelease;
            updateAvailable.Content = Localization.T("Restart to update");
            updateAvailable.ToolTip = Localization.T("The update is downloaded. Your session will be saved before restarting.");
        } catch (Exception ex) {
            LocalLog.Write("Update download failed: " + ex.Message);
            if (!shuttingDown) {
                updateAvailable.Content = Localization.T("Update available");
                MessageBox.Show(this, Localization.T("The update could not be downloaded or verified. Your current version is unchanged. Please try again or download it from GitHub."), Localization.T("Wingbeat update"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
        } finally { downloadingUpdate = false; if (!shuttingDown) updateAvailable.IsEnabled = true; }
    }
    void ArmUpdateAfterSave()
    {
        if (restartForUpdate && downloadedUpdate != null && appUpdater != null)
            appUpdater.WaitExitThenApplyUpdates(downloadedUpdate, silent: false, restart: true);
    }
    void ApplyReleaseResult(ReleaseUpdate result)
    {
        latestRelease = result;
        updateAvailable!.Visibility = result.Available ? Visibility.Visible : Visibility.Collapsed;
        updateAvailable.ToolTip = global::AionDpsMeter.Local.Localization.F($"Version {result.Version} is available. Open its GitHub download page in your browser.");
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
