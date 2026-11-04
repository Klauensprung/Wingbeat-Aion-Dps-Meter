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
        restore.Children.Add(SettingsAction(UiIcon.Meter, "Arrange DPS meter and boss alerts", ShowOverlayPlacementSetup));
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
        CheckBox OverlayOption(string label, Func<bool> read, Action<bool> write) {
            var option = new CheckBox { Content = label, IsChecked = read(), Margin = new(8) };
            option.Click += (_, _) => { write(option.IsChecked == true); SavePreferences(); Refresh(); };
            window.Activated += (_, _) => option.IsChecked = read();
            return option;
        }
        meterOptions.Children.Add(OverlayOption("Show my skills below my DPS bar", () => preferences.OverlayShowSkills, value => preferences.OverlayShowSkills = value));
        meterOptions.Children.Add(OverlayOption("Compact meter rows", () => preferences.OverlayCompact, value => preferences.OverlayCompact = value));
        meterOptions.Children.Add(OverlayOption("Show target health", () => preferences.OverlayShowTargetHealth, value => preferences.OverlayShowTargetHealth = value));
        meterOptions.Children.Add(SettingsAction(UiIcon.Meter, "Toggle DPS overlay", ToggleOverlay));
        meterOptions.Children.Add(SettingsAction(UiIcon.Meter, "Toggle DPS2 overlay", ToggleSecondMeter));
        meterOptions.Children.Add(SettingsHint("DPS2 starts in party-health mode. Each meter has its own mode, position, size and lock. Choose modes from each overlay's dropdown."));
        var meterDisplay = new StackPanel();
        var metric = new ComboBox { ItemsSource = new[] { "Damage / DPS", "Healing / HPS", "Party health / HP + debuffs" }, SelectedIndex = preferences.OverlayPartyHealth ? 2 : preferences.OverlayHealing ? 1 : 0 };
        var scope = new ComboBox { ItemsSource = new[] { "Auto", "Party", "Self", "Players", "PvP", "All" }, SelectedItem = preferences.OverlayAllActors ? "All" : preferences.OverlayScope };
        bool syncingMeter = false;
        metric.SelectionChanged += (_, _) => { if (!syncingMeter) { preferences.OverlayPartyHealth = metric.SelectedIndex == 2; preferences.OverlayHealing = metric.SelectedIndex == 1; SavePreferences(); Refresh(); } };
        scope.SelectionChanged += (_, _) => { if (!syncingMeter && scope.SelectedItem is string selected) { preferences.OverlayScope = selected; preferences.OverlayAllActors = false; SavePreferences(); Refresh(); } };
        window.Activated += (_, _) => {
            syncingMeter = true;
            try { metric.SelectedIndex = preferences.OverlayPartyHealth ? 2 : preferences.OverlayHealing ? 1 : 0; scope.SelectedItem = preferences.OverlayAllActors ? "All" : preferences.OverlayScope; pinLocal.IsChecked = preferences.OverlayPinLocal; }
            finally { syncingMeter = false; }
        };
        meterDisplay.Children.Add(SettingsField("Metric", metric)); meterDisplay.Children.Add(SettingsField("Player scope", scope));
        meterDisplay.Children.Add(OverlayOption("Follow my in-game target", () => preferences.OverlayFollowGameTarget, value => preferences.OverlayFollowGameTarget = value));
        meterDisplay.Children.Add(SettingsHint("Auto follows party or PvP activity. My skills always shows your character. All includes NPCs and other actors."));
        meterDisplay.Children.Add(OverlayOption("Only show damage to encounter bosses", () => preferences.OverlayBossesOnly, value => preferences.OverlayBossesOnly = value));
        meterDisplay.Children.Add(OverlayOption("Lock meter position", () => preferences.OverlayLocked, value => preferences.OverlayLocked = value));
        var cooldownOptions = new StackPanel();
        cooldownOptions.Children.Add(SettingsHint("Track selected skills as icons with a countdown. Open the skill picker to choose skills and adjust cooldown durations."));
        cooldownOptions.Children.Add(SettingsAction(UiIcon.Cooldowns, "Choose tracked skills", ConfigureCooldowns));
        cooldownOptions.Children.Add(SettingsAction(UiIcon.Cooldowns, "Toggle cooldown overlay", ToggleCooldownOverlay));
        var visibility = new StackPanel();
        visibility.Children.Add(OverlayOption("Only show overlays over Aion 2 or Wingbeat", () => preferences.OverlaysGameOnly,
            value => { preferences.OverlaysGameOnly = value; RefreshCooldownVisibility(); }));
        visibility.Children.Add(SettingsHint("All overlays share this setting. Positions are kept when an overlay is hidden."));
        visibility.Children.Add(OverlayOption("Hide overlays when Aion 2's menu cursor appears",()=>preferences.HideOverlaysInGameMenus,
            value=>{preferences.HideOverlaysInGameMenus=value;RefreshCooldownVisibility();}));
        visibility.Children.Add(SettingsHint("For Aion 2 action controls: after observing the cursor hidden during gameplay, a visible menu cursor hides overlays after two seconds. Move onto an overlay or its menu to keep overlays visible; leaving gives another two seconds. Holding Alt also keeps them visible when Windows allows key detection. Classic/Aion 1 controls with an always-visible cursor are not supported by this detection."));
        var hideShortcut = new TextBox { IsReadOnly=true, Text=OverlayHotkey.Label(preferences.OverlayHideKey,preferences.OverlayHideModifiers), ToolTip="Click here and press a key combination. Use Ctrl, Alt, Shift, or Windows with another key." };
        hideShortcut.PreviewKeyDown += (_,e) => {
            var key=e.Key==System.Windows.Input.Key.System?e.SystemKey:e.Key;
            e.Handled=true;
            if(key is System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift or System.Windows.Input.Key.LWin or System.Windows.Input.Key.RWin)return;
            if(key==System.Windows.Input.Key.Escape){hideShortcut.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));return;}
            int modifiers=(int)System.Windows.Input.Keyboard.Modifiers;
            if(modifiers==0){overlayHotkeyStatus!.Text="Include Ctrl, Alt, Shift, or Windows.";return;}
            preferences.OverlayHideKey=System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);preferences.OverlayHideModifiers=modifiers;
            hideShortcut.Text=OverlayHotkey.Label(preferences.OverlayHideKey,modifiers);ApplyOverlayHotkey();SavePreferences();
        };
        visibility.Children.Add(SettingsField("Global hide/show hotkey",hideShortcut));
        overlayHotkeyStatus=SettingsHint("Hide/show all overlays: "+hideShortcut.Text);visibility.Children.Add(overlayHotkeyStatus);
        visibility.Children.Add(SettingsAction(UiIcon.Meter,"Hide / show all overlays",ToggleOverlayVisibility));
        visibility.Children.Add(SettingsAction(UiIcon.Close,"Disable hotkey",()=>{preferences.OverlayHideKey=0;hideShortcut.Text="Disabled";ApplyOverlayHotkey();SavePreferences();}));
        visibility.Children.Add(SettingsHint("Each overlay also has a Hide 60s button. It returns after one minute when its normal visibility conditions allow it."));
        var extraOverlays = new WrapPanel();
        foreach (var action in new (string Label, UiIcon Icon, Action Toggle)[] {
            ("Ping", UiIcon.Network, TogglePingOverlay), ("Loot", UiIcon.Folder, ToggleLootOverlay),
            ("Pet farming", UiIcon.Catalogue, TogglePetTracker), ("Pet map", UiIcon.Catalogue, TogglePetMap) })
            extraOverlays.Children.Add(SettingsAction(action.Icon, "Toggle " + action.Label, action.Toggle));
        var alerts = new StackPanel();
        alerts.Children.Add(OverlayOption("Boss casts, phases and HP preparation",()=>preferences.BossAlertsEnabled,value=>ConfigureBossAlerts(value,preview:false)));
        alerts.Children.Add(SettingsAction(UiIcon.Boss,"Position boss alerts",()=>ConfigureBossAlerts(true,true)));
        alerts.Children.Add(OverlayOption("Show loss-of-control alerts", () => preferences.ControlAlertsEnabled, value => ConfigureControlAlerts(value, preview: value)));
        alerts.Children.Add(SettingsAction(UiIcon.Capture, "Position control alerts", () => ConfigureControlAlerts(true, preview: true)));
        alerts.Children.Add(OverlayOption("Alert on party debuffs", () => preferences.PartyDebuffAlerts, value => preferences.PartyDebuffAlerts = value));
        var partyThreshold = new ComboBox { ItemsSource = new[] { 2, 3, 4, 5, 6, 8, 10 }, SelectedItem = preferences.PartyDebuffThreshold };
        partyThreshold.SelectionChanged += (_, _) => { if (partyThreshold.SelectedItem is int count) { preferences.PartyDebuffThreshold = count; SavePreferences(); } };
        alerts.Children.Add(SettingsField("Party members needed for a debuff alert", partyThreshold));
        alerts.Children.Add(OverlayOption("Notify when a pet levels up", () => preferences.PetLevelAlerts, value => preferences.PetLevelAlerts = value));
        alerts.Children.Add(OverlayOption("Notify when a pet joins your collection", () => preferences.PetCollectionAlerts, value => preferences.PetCollectionAlerts = value));
        alerts.Children.Add(SettingsAction(UiIcon.Cooldowns, "Timer setup", ShowReminderSettings));
        alerts.Children.Add(OverlayOption("Notify before field-boss respawns", () => preferences.FieldBossNotifications, value => preferences.FieldBossNotifications = value));
        alerts.Children.Add(SettingsAction(UiIcon.Boss, "Cached field-boss timers", ShowFieldBosses));
        window.AddPage("Overlays", UiIcon.Meter, "Your meter, skill breakdown and on-screen tools in one place.",
            SettingsCards(SettingsSection("DPS meter", meterOptions), SettingsSection("Metric and scope", meterDisplay), SettingsSection("Visibility", visibility), SettingsSection("Cooldowns", cooldownOptions),
                SettingsSection("Overlay scale", OverlayScale.Settings()), SettingsSection("Exploration overlays", extraOverlays)));
        window.AddPage("Alerts", UiIcon.Cooldowns, "Choose what deserves your attention during play.", SettingsCards(SettingsSection("Combat and event alerts", alerts)));

        var colours = new StackPanel();
        themes.Width = 170; accent.Width = 170; backAttackColor.Width = 170;
        colours.Children.Add(SettingsField("Theme", themes, inline: true));
        colours.Children.Add(SettingsField("Accent colour", accent, inline: true));
        colours.Children.Add(SettingsField("Back-attack border", backAttackColor, inline: true));
        colours.Children.Add(SettingsHint("Use a hex colour such as #55D6BE. The back-attack border stays translucent."));
        colours.Children.Add(SettingsAction(UiIcon.Save, "Apply appearance", ApplyPreferences));
        var bars = new StackPanel(); customBars.Content = "Use class colours"; customBars.IsChecked = !preferences.OverlayCustomBars;
        window.Activated += (_, _) => customBars.IsChecked = !preferences.OverlayCustomBars;
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
