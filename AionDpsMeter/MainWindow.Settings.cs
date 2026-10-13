using System.Windows;
using System.Windows.Controls;
using SharpPcap.LibPcap;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    SettingsWindow BuildSettings()
    {
        var window = new SettingsWindow(preferences);
        var startup = new StackPanel();
        startup.Children.Add(autoStart);
        startup.Children.Add(SettingsHint("Start capture using your saved adapter choice or Aion connection detection, then restore the overlays below."));
        var restoreMeter = new CheckBox { Content = "Restore the DPS overlay", IsChecked = preferences.OpenMeterOnStartup, Margin = new(8) };
        var restoreCooldowns = new CheckBox { Content = "Restore the cooldown overlay", IsChecked = preferences.OpenCooldownsOnStartup, Margin = new(8) };
        var restore = new StackPanel(); restore.Children.Add(restoreMeter); restore.Children.Add(restoreCooldowns);
        restore.Children.Add(SettingsHint("The DPS overlay needs a saved position. Cooldowns need at least one tracked skill. Positions are saved as you move each overlay."));
        void SaveStartup() {
            preferences.AutoStartConfigured = autoStart.IsChecked == true;
            preferences.OpenMeterOnStartup = restoreMeter.IsChecked == true;
            preferences.OpenCooldownsOnStartup = restoreCooldowns.IsChecked == true;
            restoreMeter.IsEnabled = restoreCooldowns.IsEnabled = preferences.AutoStartConfigured;
            SavePreferences();
        }
        restoreMeter.IsEnabled = restoreCooldowns.IsEnabled = preferences.AutoStartConfigured;
        autoStart.Click += (_, _) => SaveStartup(); restoreMeter.Click += (_, _) => SaveStartup(); restoreCooldowns.Click += (_, _) => SaveStartup();
        window.AddPage("Startup", UiIcon.Power, "Ready to play when you open Wingbeat.",
            SettingsCards(SettingsSection("Automatic startup", startup), SettingsSection("Reopen my overlays", restore)));

        var captureOptions = new StackPanel(); captureOptions.Children.Add(setupMessage);
        autoDetectAdapter.IsChecked = GameAdapterDetector.Automatic(preferences);
        autoDetectAdapter.Click += (_, _) => {
            preferences.AutoDetectAdapter = autoDetectAdapter.IsChecked == true; SavePreferences();
            if (preferences.AutoDetectAdapter == true) RefreshAdapters();
            else { LoadAdapters(); Refresh(); }
        };
        captureOptions.Children.Add(autoDetectAdapter);
        adapters.Width = double.NaN; adapters.HorizontalAlignment = HorizontalAlignment.Stretch;
        captureOptions.Children.Add(SettingsField("Network adapter", adapters));
        captureOptions.Children.Add(adapterStatus);
        captureOptions.Children.Add(SettingsHint("Automatic detection uses Aion's active TCP connections. Connect to a server first. Turn it off to choose an adapter yourself, including for a VPN or local proxy."));
        var captureActions = new WrapPanel();
        detectAdapterButton = IconButton(UiIcon.Refresh, "Detect game adapter", RefreshAdapters); captureActions.Children.Add(detectAdapterButton);
        captureActions.Children.Add(IconButton(UiIcon.Save, "Apply adapter", ApplyCapturePreferences));
        captureOptions.Children.Add(captureActions);
        var driver = new StackPanel(); driver.Children.Add(SettingsHint("Live capture requires Npcap. Once it is installed, refresh the adapter list."));
        driver.Children.Add(SettingsAction(UiIcon.Capture, "Npcap setup", () => NpcapSetup.CreateDialog(settingsWindow).ShowDialog()));
        window.AddPage("Capture", UiIcon.Capture, "Set up once, then use Start capture in the toolbar.",
            SettingsCards(SettingsSection("Your connection", captureOptions), SettingsSection("Capture driver", driver)));

        var meterOptions = new StackPanel(); meterOptions.Children.Add(pinLocal);
        pinLocal.Click += (_, _) => SetPinLocal();
        meterOptions.Children.Add(SettingsHint("By default, players are sorted by DPS. Pinning keeps your character at the top."));
        meterOptions.Children.Add(SettingsAction(UiIcon.Meter, "Toggle DPS overlay", ToggleOverlay));
        var cooldownOptions = new StackPanel();
        cooldownOptions.Children.Add(SettingsHint("Track selected skills as icons with a countdown. Open the skill picker to choose skills and adjust cooldown durations."));
        cooldownOptions.Children.Add(SettingsAction(UiIcon.Cooldowns, "Choose tracked skills", ConfigureCooldowns));
        cooldownOptions.Children.Add(SettingsAction(UiIcon.Cooldowns, "Toggle cooldown overlay", ToggleCooldownOverlay));
        window.AddPage("Overlays", UiIcon.Meter, "Click an overlay button again to hide it. Your position and tracking are kept.",
            SettingsCards(SettingsSection("DPS meter", meterOptions), SettingsSection("Cooldowns", cooldownOptions)));

        var colours = new StackPanel();
        themes.Width = 170; accent.Width = 170; backAttackColor.Width = 170;
        colours.Children.Add(SettingsField("Theme", themes, inline: true));
        colours.Children.Add(SettingsField("Accent colour", accent, inline: true));
        colours.Children.Add(SettingsField("Back-attack border", backAttackColor, inline: true));
        colours.Children.Add(SettingsHint("Use a hex colour such as #55D6BE. The back-attack border stays translucent."));
        colours.Children.Add(SettingsAction(UiIcon.Save, "Apply appearance", ApplyPreferences));
        var bars = new StackPanel(); customBars.Content = "Use class colours"; customBars.IsChecked = !preferences.OverlayCustomBars;
        customBars.Click += (_, _) => { preferences.OverlayCustomBars = customBars.IsChecked != true; SavePreferences(); Refresh(); };
        bars.Children.Add(customBars); bars.Children.Add(SettingsHint("Turn off to give each participant a unique colour."));
        window.AddPage("Appearance", UiIcon.Palette, "Make the meter feel at home on your screen.",
            SettingsCards(SettingsSection("Theme and highlights", colours), SettingsSection("Participant bars", bars)));

        var local = new StackPanel();
        local.Children.Add(SettingsHint("Fights, cached player details and settings are stored locally and survive app updates."));
        local.Children.Add(SettingsAction(UiIcon.Folder, "Open data folder", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppDataPaths.Root) { UseShellExecute = true })));
        local.Children.Add(SettingsAction(UiIcon.Folder, "Import skill names", ImportSkillNames));
        local.Children.Add(SettingsAction(UiIcon.Chart, "Preview demo", LoadDemo));
        window.AddPage("Local data", UiIcon.Folder, "Your fights and preferences stay on this PC.", SettingsCards(SettingsSection("Saved data and tools", local)));

        var advanced = new StackPanel();
        filter.Width = double.NaN; filter.HorizontalAlignment = HorizontalAlignment.Stretch; filter.TextWrapping = TextWrapping.Wrap;
        advanced.Children.Add(SettingsField("Capture filter", filter));
        decodeEu.Click += (_, _) => { preferences.DecodeEu = decodeEu.IsChecked == true; SavePreferences(); };
        recordSample.Content = "Save a PCAP sample on the next manual start";
        recordSample.ToolTip = "Applies to this session only. Automatic startup does not record a sample.";
        advanced.Children.Add(decodeEu); advanced.Children.Add(recordSample);
        advanced.Children.Add(SettingsHint("Capture changes take effect on the next start. PCAP samples are optional and saved only after you choose a file."));
        advanced.Children.Add(SettingsAction(UiIcon.Save, "Apply capture settings", ApplyCapturePreferences));
        window.AddPage("Advanced", UiIcon.Settings, "Capture options and diagnostics for troubleshooting.",
            SettingsCards(SettingsSection("Capture options", advanced), SettingsSection("Status", status)));
        return window;
    }

    static StackPanel SettingsCards(params UIElement[] cards)
    {
        var panel = new StackPanel(); foreach (var card in cards) panel.Children.Add(card); return panel;
    }
    static TextBlock SettingsHint(string text)
    {
        var hint = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new(8, 3, 8, 12), FontSize = 12, LineHeight = 18 };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); return hint;
    }
    static FrameworkElement SettingsField(string label, FrameworkElement control, bool inline = false)
    {
        var field = new Grid { Margin = new(4, 4, 4, 8) };
        if (inline) {
            field.ColumnDefinitions.Add(new()); field.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); Grid.SetColumn(control, 1);
        } else {
            field.RowDefinitions.Add(new() { Height = GridLength.Auto }); field.RowDefinitions.Add(new() { Height = GridLength.Auto }); Grid.SetRow(control, 1);
        }
        field.Children.Add(new Label { Content = label, Target = control, Padding = new(4, 0, 4, 4), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        if (!double.IsNaN(control.Width)) control.HorizontalAlignment = HorizontalAlignment.Left;
        field.Children.Add(control); System.Windows.Automation.AutomationProperties.SetName(control, label); return field;
    }
    static Button SettingsAction(UiIcon icon, string text, Action action)
    {
        var button = IconButton(icon, text, action); button.HorizontalAlignment = HorizontalAlignment.Left; return button;
    }
    void ToggleSettings()
    {
        if (settingsWindow.IsVisible) settingsWindow.Hide(); else ShowSettings();
    }
    void ShowSettings(string? page = null)
    {
        settingsWindow.Owner = this;
        if (page != null) settingsWindow.SelectPage(page);
        settingsWindow.Show(); settingsWindow.Activate();
    }
    void ApplyCapturePreferences()
    {
        if (adapterBusy) return;
        preferences.AutoDetectAdapter = autoDetectAdapter.IsChecked == true;
        if (adapters.SelectedItem is LibPcapLiveDevice adapter) preferences.Adapter = adapter.Name;
        preferences.Filter = filter.Text; preferences.DecodeEu = decodeEu.IsChecked == true;
        SavePreferences(); notice = "Capture settings saved. Changes apply the next time capture starts."; Refresh();
    }
}
