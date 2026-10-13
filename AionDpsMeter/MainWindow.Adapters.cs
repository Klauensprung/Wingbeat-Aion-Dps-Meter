using System.Windows;
using System.Windows.Controls;
using SharpPcap.LibPcap;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    readonly bool demoMode;
    readonly CheckBox autoDetectAdapter = new() { Content = "Detect adapter from Aion automatically", Margin = new(8) };
    readonly TextBlock adapterStatus = new() { Margin = new(8), TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    Button? detectAdapterButton;
    bool adapterBusy;
    AdapterDetection? adapterDetection;

    void UpdateAdapterControls()
    {
        bool available = !adapterBusy && capture == null;
        adapters.IsEnabled = available && !GameAdapterDetector.Automatic(preferences);
        autoDetectAdapter.IsEnabled = available;
        if (detectAdapterButton != null) {
            detectAdapterButton.IsEnabled = available;
            UiIcons.SetLabel(detectAdapterButton, UiIcon.Refresh, GameAdapterDetector.Automatic(preferences) ? "Detect game adapter" : "Refresh adapters");
        }
        start.IsEnabled = !adapterBusy;
    }
    async Task<bool> DetectGameAdapterAsync()
    {
        adapters.SelectedItem = null; adapterDetection = null;
        adapterStatus.Text = "Looking for Aion's active connection...";
        var names = adapters.Items.Cast<LibPcapLiveDevice>().Select(d => d.Name).ToArray();
        AdapterDetection result;
        try {
            result = demoMode ? new(AdapterDetectionStatus.GameNotRunning) :
                await Task.Run(() => GameAdapterDetector.Detect(names)).WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch { result = new(AdapterDetectionStatus.Failed); }
        if (shuttingDown) return false;
        adapterDetection = result;
        adapters.SelectedItem = result.Status == AdapterDetectionStatus.Found
            ? adapters.Items.Cast<LibPcapLiveDevice>().FirstOrDefault(d => string.Equals(d.Name, result.Adapter, StringComparison.OrdinalIgnoreCase)) : null;
        notice = adapterStatus.Text = result.Message;
        if (result.Status == AdapterDetectionStatus.Found) setupMessage.Visibility = Visibility.Collapsed;
        Refresh(); return adapters.SelectedItem != null;
    }
    async void RefreshAdapters()
    {
        if (adapterBusy || capture != null) return;
        adapterBusy = true; UpdateAdapterControls();
        try {
            LoadAdapters();
            if (!npcapMissing && GameAdapterDetector.Automatic(preferences)) await DetectGameAdapterAsync();
            else Refresh();
        }
        catch { if (!shuttingDown) adapterStatus.Text = new AdapterDetection(AdapterDetectionStatus.Failed).Message; }
        finally { adapterBusy = false; if (!shuttingDown) UpdateAdapterControls(); }
    }
    async void StartRequested()
    {
        if (adapterBusy || shuttingDown) return;
        try {
            if (capture != null) { StopAndSave(); return; }
            adapterBusy = true; UpdateAdapterControls();
            if (GameAdapterDetector.Automatic(preferences)) {
                LoadAdapters();
                if (!npcapMissing && !await DetectGameAdapterAsync()) {
                    if (!shuttingDown) ShowSetup(adapterStatus.Text);
                    return;
                }
            }
            if (!shuttingDown) Start();
        }
        catch (Exception ex) { LocalLog.Write("Capture start/stop failed: " + ex); if (!shuttingDown) ShowSetup("Capture could not start or stop. " + ex.Message); }
        finally { adapterBusy = false; if (!shuttingDown) UpdateAdapterControls(); }
    }
    void CheckAdapterUi()
    {
        var saved = preferences;
        try {
            preferences = new() { AutoDetectAdapter = true }; autoDetectAdapter.IsChecked = true; UpdateAdapterControls();
            if (adapters.IsEnabled || !autoDetectAdapter.IsEnabled || !start.IsEnabled) throw new Exception("Automatic adapter controls are not available.");
            // With no installed driver, Start opens the interactive Npcap dialog.
            // Driver-independent startup policy is covered by the self-tests.
            if (!npcapMissing) {
                StartRequested();
                if (capture != null || adapterBusy || adapterDetection?.Status != AdapterDetectionStatus.GameNotRunning || !settingsWindow.IsVisible)
                    throw new Exception("An undetected game must open setup without capturing an arbitrary adapter.");
            }
            autoDetectAdapter.IsChecked = false;
            autoDetectAdapter.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            if (GameAdapterDetector.Automatic(preferences) || !adapters.IsEnabled) throw new Exception("Manual adapter selection is unavailable after opting out.");
            adapterBusy = true; UpdateAdapterControls();
            if (adapters.IsEnabled || autoDetectAdapter.IsEnabled || start.IsEnabled || detectAdapterButton!.IsEnabled)
                throw new Exception("Adapter detection permits concurrent start or adapter changes.");
        }
        finally {
            adapterBusy = false; preferences = saved; autoDetectAdapter.IsChecked = GameAdapterDetector.Automatic(preferences);
            settingsWindow.Hide(); LoadAdapters(); SavePreferences(); Refresh();
        }
    }
}
