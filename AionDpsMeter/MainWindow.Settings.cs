using System.Windows;
using System.Windows.Controls;
using SharpPcap.LibPcap;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    SettingsWindow BuildSettings()
    {
        var window = new SettingsWindow(preferences);
        var languageOptions=new StackPanel();
        var languages=new ComboBox{ItemsSource=new[]{"Automatic (Windows language)","Deutsch","English"},SelectedIndex=preferences.Language=="de"?1:preferences.Language=="en"?2:0};
        languages.SelectionChanged+=(_,_)=>{preferences.Language=languages.SelectedIndex switch{1=>"de",2=>"en",_=>"auto"};SavePreferences();};
        languageOptions.Children.Add(SettingsField(Localization.T("Interface language"),languages));
        var gameLanguages=new ComboBox{ItemsSource=new[]{"English","Deutsch"},SelectedIndex=preferences.GameLanguage=="de"?1:0};
        gameLanguages.SelectionChanged+=(_,_)=>{preferences.GameLanguage=gameLanguages.SelectedIndex==1?"de":"en";SavePreferences();};
        languageOptions.Children.Add(SettingsField(Localization.T("Game language"),gameLanguages));
        languageOptions.Children.Add(SettingsHint(Localization.T("Game language selects skill names independently of the interface. Missing German names keep the original name.")));
        languageOptions.Children.Add(SettingsHint(global::AionDpsMeter.Local.Localization.T("Language changes apply after restarting Wingbeat. Automatic uses the Windows display language; other languages use English.")));
        languageOptions.Children.Add(SettingsAction(UiIcon.Power,global::AionDpsMeter.Local.Localization.T("Restart Wingbeat"),()=>{
            SavePreferences();
            void Restart(object? sender,EventArgs args){Closed-=Restart;System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!){WorkingDirectory=AppContext.BaseDirectory,UseShellExecute=true});}
            Closed+=Restart;Close();if(IsVisible)Closed-=Restart;
        }));
        window.AddPage("Language",UiIcon.Settings,"Choose the language used by Wingbeat.",SettingsCards(SettingsSection(global::AionDpsMeter.Local.Localization.T("Language"),languageOptions)));
        var startup = new StackPanel();
        startup.Children.Add(autoStart);
        startup.Children.Add(SettingsHint(global::AionDpsMeter.Local.Localization.T("Start capture using your saved adapter choice or Aion connection detection, then restore the overlays below.")));
        var restoreMeter = new CheckBox { Content = global::AionDpsMeter.Local.Localization.T("Restore the DPS overlay"), IsChecked = preferences.OpenMeterOnStartup, Margin = new(8) };
        var restoreCooldowns = new CheckBox { Content = global::AionDpsMeter.Local.Localization.T("Restore the cooldown overlay"), IsChecked = preferences.OpenCooldownsOnStartup, Margin = new(8) };
        var restore = new StackPanel(); restore.Children.Add(restoreMeter); restore.Children.Add(restoreCooldowns);
        restore.Children.Add(SettingsHint(global::AionDpsMeter.Local.Localization.T("The DPS overlay needs a saved position. Cooldowns need at least one tracked skill. Positions are saved as you move each overlay.")));
        restore.Children.Add(SettingsAction(UiIcon.Meter, global::AionDpsMeter.Local.Localization.T("Arrange DPS meter and boss alerts"), ShowOverlayPlacementSetup));
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
            SettingsCards(SettingsSection(global::AionDpsMeter.Local.Localization.T("Automatic startup"), startup), SettingsSection(global::AionDpsMeter.Local.Localization.T("Reopen my overlays"), restore)));

        var captureOptions = new StackPanel(); captureOptions.Children.Add(setupMessage);
        autoDetectAdapter.IsChecked = GameAdapterDetector.Automatic(preferences);
        autoDetectAdapter.Click += (_, _) => {
            preferences.AutoDetectAdapter = autoDetectAdapter.IsChecked == true; SavePreferences();
            if (preferences.AutoDetectAdapter == true) RefreshAdapters();
            else { LoadAdapters(); Refresh(); }
        };
        captureOptions.Children.Add(autoDetectAdapter);
        adapters.Width = double.NaN; adapters.HorizontalAlignment = HorizontalAlignment.Stretch;
        captureOptions.Children.Add(SettingsField(global::AionDpsMeter.Local.Localization.T("Network adapter"), adapters));
        captureOptions.Children.Add(adapterStatus);
        captureOptions.Children.Add(SettingsHint(global::AionDpsMeter.Local.Localization.T("Automatic detection uses Aion's active TCP connections. Connect to a server first. Turn it off to choose an adapter yourself, including for a VPN or local proxy.")));
        var captureActions = new WrapPanel();
        detectAdapterButton = IconButton(UiIcon.Refresh, global::AionDpsMeter.Local.Localization.T("Detect game adapter"), RefreshAdapters); captureActions.Children.Add(detectAdapterButton);
        captureActions.Children.Add(IconButton(UiIcon.Save, global::AionDpsMeter.Local.Localization.T("Apply adapter"), ApplyCapturePreferences));
        captureOptions.Children.Add(captureActions);
        var driver = new StackPanel(); driver.Children.Add(SettingsHint(global::AionDpsMeter.Local.Localization.T("Live capture requires Npcap. Once it is installed, refresh the adapter list.")));
        driver.Children.Add(SettingsAction(UiIcon.Capture, global::AionDpsMeter.Local.Localization.T("Npcap setup"), () => NpcapSetup.CreateDialog(settingsWindow).ShowDialog()));
        window.AddPage("Capture", UiIcon.Capture, "Set up once, then use Start capture in the toolbar.",
            SettingsCards(SettingsSection(global::AionDpsMeter.Local.Localization.T("Your connection"), captureOptions), SettingsSection(global::AionDpsMeter.Local.Localization.T("Capture driver"), driver)));

        var meterOptions = new StackPanel(); meterOptions.Children.Add(pinLocal);
        pinLocal.Click += (_, _) => SetPinLocal();
        meterOptions.Children.Add(SettingsHint(global::AionDpsMeter.Local.Localization.T("By default, players are sorted by DPS. Pinning keeps your character at the top.")));
        CheckBox OverlayOption(string label, Func<bool> read, Action<bool> write) {
            var option = new CheckBox { Content = Localization.T(label), IsChecked = read(), Margin = new(8) };
            option.Click += (_, _) => { write(option.IsChecked == true); SavePreferences(); Refresh(); };
            window.Activated += (_, _) => option.IsChecked = read();
            return option;
        }
        meterOptions.Children.Add(OverlayOption("Show my skills below my DPS bar", () => preferences.OverlayShowSkills, value => preferences.OverlayShowSkills = value));
        meterOptions.Children.Add(OverlayOption("Compact meter rows", () => preferences.OverlayCompact, value => preferences.OverlayCompact = value));
        meterOptions.Children.Add(OverlayOption("Show target health", () => preferences.OverlayShowTargetHealth, value => preferences.OverlayShowTargetHealth = value));
        meterOptions.Children.Add(SettingsAction(UiIcon.Meter, global::AionDpsMeter.Local.Localization.T("Toggle DPS overlay"), ToggleOverlay));
        meterOptions.Children.Add(SettingsAction(UiIcon.Meter, global::AionDpsMeter.Local.Localization.T("Toggle DPS2 overlay"), ToggleSecondMeter));
        meterOptions.Children.Add(SettingsHint(global::AionDpsMeter.Local.Localization.T("DPS2 starts in party-health mode. Each meter has its own mode, position, size and lock. Choose modes from each overlay's dropdown.")));
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
        meterDisplay.Children.Add(SettingsField(global::AionDpsMeter.Local.Localization.T("Metric"), metric)); meterDisplay.Children.Add(SettingsField(global::AionDpsMeter.Local.Localization.T("Player scope"), scope));
        meterDisplay.Children.Add(OverlayOption("Follow my in-game target", () => preferences.OverlayFollowGameTarget, value => preferences.OverlayFollowGameTarget = value));
        meterDisplay.Children.Add(SettingsHint(global::AionDpsMeter.Local.Localization.T("Auto follows party or PvP activity. My skills always shows your character. All includes NPCs and other actors.")));
        meterDisplay.Children.Add(OverlayOption("Only show damage to encounter bosses", () => preferences.OverlayBossesOnly, value => preferences.OverlayBossesOnly = value));
        meterDisplay.Children.Add(OverlayOption("Lock meter position", () => preferences.OverlayLocked, value => preferences.OverlayLocked = value));
        var cooldownOptions = new StackPanel();
        cooldownOptions.Children.Add(SettingsHint(global::AionDpsMeter.Local.Localization.T("Track selected skills as icons with a countdown. Open the skill picker to choose skills and adjust cooldown durations.")));
        cooldownOptions.Children.Add(SettingsAction(UiIcon.Cooldowns, global::AionDpsMeter.Local.Localization.T("Choose tracked skills"), ConfigureCooldowns));
        cooldownOptions.Children.Add(SettingsAction(UiIcon.Cooldowns, global::AionDpsMeter.Local.Localization.T("Toggle cooldown overlay"), ToggleCooldownOverlay));
        var visibility = new StackPanel();
        visibility.Children.Add(OverlayOption("Only show overlays over Aion 2 or Wingbeat", () => preferences.OverlaysGameOnly,
            value => { preferences.OverlaysGameOnly = value; RefreshCooldownVisibility(); }));
        visibility.Children.Add(SettingsHint(global::AionDpsMeter.Local.Localization.T("All overlays share this setting. Positions are kept when an overlay is hidden.")));
        visibility.Children.Add(OverlayOption("Hide overlays when Aion 2's menu cursor appears",()=>preferences.HideOverlaysInGameMenus,
            value=>{preferences.HideOverlaysInGameMenus=value;RefreshCooldownVisibility();}));
        visibility.Children.Add(SettingsHint(global::AionDpsMeter.Local.Localization.T("For Aion 2 action controls: after observing the cursor hidden during gameplay, a visible menu cursor hides overlays after two seconds. Move onto an overlay or its menu to keep overlays visible; leaving gives another two seconds. Holding Alt also keeps them visible when Windows allows key detection. Classic/Aion 1 controls with an always-visible cursor are not supported by this detection.")));
        var hideShortcut = new TextBox { IsReadOnly=true, Text=OverlayHotkey.Label(preferences.OverlayHideKey,preferences.OverlayHideModifiers), ToolTip=global::AionDpsMeter.Local.Localization.T("Click here and press a key combination. Use Ctrl, Alt, Shift, or Windows with another key.") };
        hideShortcut.PreviewKeyDown += (_,e) => {
            var key=e.Key==System.Windows.Input.Key.System?e.SystemKey:e.Key;
            e.Handled=true;
            if(key is System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift or System.Windows.Input.Key.LWin or System.Windows.Input.Key.RWin)return;
            if(key==System.Windows.Input.Key.Escape){hideShortcut.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));return;}
            int modifiers=(int)System.Windows.Input.Keyboard.Modifiers;
            if(modifiers==0){overlayHotkeyStatus!.Text=global::AionDpsMeter.Local.Localization.T("Include Ctrl, Alt, Shift, or Windows.");return;}
            preferences.OverlayHideKey=System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);preferences.OverlayHideModifiers=modifiers;
            hideShortcut.Text=OverlayHotkey.Label(preferences.OverlayHideKey,modifiers);ApplyOverlayHotkey();SavePreferences();
        };
        visibility.Children.Add(SettingsField(global::AionDpsMeter.Local.Localization.T("Global hide/show hotkey"),hideShortcut));
        overlayHotkeyStatus=SettingsHint("Hide/show all overlays: "+hideShortcut.Text);visibility.Children.Add(overlayHotkeyStatus);
        visibility.Children.Add(SettingsAction(UiIcon.Meter,global::AionDpsMeter.Local.Localization.T("Hide / show all overlays"),ToggleOverlayVisibility));
        visibility.Children.Add(SettingsAction(UiIcon.Close,global::AionDpsMeter.Local.Localization.T("Disable hotkey"),()=>{preferences.OverlayHideKey=0;hideShortcut.Text=global::AionDpsMeter.Local.Localization.T("Disabled");ApplyOverlayHotkey();SavePreferences();}));
        visibility.Children.Add(SettingsHint(global::AionDpsMeter.Local.Localization.T("Each overlay also has a Hide 60s button. It returns after one minute when its normal visibility conditions allow it.")));
        var extraOverlays = new WrapPanel();
        foreach (var action in new (string Label, UiIcon Icon, Action Toggle)[] {
            ("Ping", UiIcon.Network, TogglePingOverlay), ("Loot", UiIcon.Folder, ToggleLootOverlay),
            ("Pet farming", UiIcon.Catalogue, TogglePetTracker), ("Pet map", UiIcon.Catalogue, TogglePetMap) })
            extraOverlays.Children.Add(SettingsAction(action.Icon, "Toggle " + action.Label, action.Toggle));
        var alerts = new StackPanel();
        alerts.Children.Add(OverlayOption("Boss casts, phases and HP preparation",()=>preferences.BossAlertsEnabled,value=>ConfigureBossAlerts(value,preview:false)));
        alerts.Children.Add(SettingsAction(UiIcon.Boss,global::AionDpsMeter.Local.Localization.T("Position boss alerts"),()=>ConfigureBossAlerts(true,true)));
        alerts.Children.Add(OverlayOption("Show loss-of-control alerts", () => preferences.ControlAlertsEnabled, value => ConfigureControlAlerts(value, preview: value)));
        alerts.Children.Add(SettingsAction(UiIcon.Capture, global::AionDpsMeter.Local.Localization.T("Position control alerts"), () => ConfigureControlAlerts(true, preview: true)));
        alerts.Children.Add(OverlayOption("Alert on party debuffs", () => preferences.PartyDebuffAlerts, value => preferences.PartyDebuffAlerts = value));
        var partyThreshold = new ComboBox { ItemsSource = new[] { 2, 3, 4, 5, 6, 8, 10 }, SelectedItem = preferences.PartyDebuffThreshold };
        partyThreshold.SelectionChanged += (_, _) => { if (partyThreshold.SelectedItem is int count) { preferences.PartyDebuffThreshold = count; SavePreferences(); } };
        alerts.Children.Add(SettingsField(global::AionDpsMeter.Local.Localization.T("Party members needed for a debuff alert"), partyThreshold));
        alerts.Children.Add(OverlayOption("Notify when a pet levels up", () => preferences.PetLevelAlerts, value => preferences.PetLevelAlerts = value));
        alerts.Children.Add(OverlayOption("Notify when a pet joins your collection", () => preferences.PetCollectionAlerts, value => preferences.PetCollectionAlerts = value));
        alerts.Children.Add(SettingsAction(UiIcon.Cooldowns, global::AionDpsMeter.Local.Localization.T("Timer setup"), ShowReminderSettings));
        alerts.Children.Add(OverlayOption("Notify before field-boss respawns", () => preferences.FieldBossNotifications, value => preferences.FieldBossNotifications = value));
        alerts.Children.Add(SettingsAction(UiIcon.Boss, global::AionDpsMeter.Local.Localization.T("Cached field-boss timers"), ShowFieldBosses));
        window.AddPage("Overlays", UiIcon.Meter, "Your meter, skill breakdown and on-screen tools in one place.",
            SettingsCards(SettingsSection(global::AionDpsMeter.Local.Localization.T("DPS meter"), meterOptions), SettingsSection(global::AionDpsMeter.Local.Localization.T("Metric and scope"), meterDisplay), SettingsSection(global::AionDpsMeter.Local.Localization.T("Visibility"), visibility), SettingsSection(global::AionDpsMeter.Local.Localization.T("Cooldowns"), cooldownOptions),
                SettingsSection(global::AionDpsMeter.Local.Localization.T("Overlay scale"), OverlayScale.Settings()), SettingsSection(global::AionDpsMeter.Local.Localization.T("Exploration overlays"), extraOverlays)));
        window.AddPage("Alerts", UiIcon.Cooldowns, "Choose what deserves your attention during play.", SettingsCards(SettingsSection(global::AionDpsMeter.Local.Localization.T("Combat and event alerts"), alerts)));

        var colours = new StackPanel();
        themes.Width = 170; accent.Width = 170; backAttackColor.Width = 170;
        colours.Children.Add(SettingsField(global::AionDpsMeter.Local.Localization.T("Theme"), themes, inline: true));
        colours.Children.Add(SettingsField(global::AionDpsMeter.Local.Localization.T("Accent colour"), accent, inline: true));
        colours.Children.Add(SettingsField(global::AionDpsMeter.Local.Localization.T("Back-attack border"), backAttackColor, inline: true));
        colours.Children.Add(SettingsHint(global::AionDpsMeter.Local.Localization.T("Use a hex colour such as #55D6BE. The back-attack border stays translucent.")));
        colours.Children.Add(SettingsAction(UiIcon.Save, global::AionDpsMeter.Local.Localization.T("Apply appearance"), ApplyPreferences));
        var bars = new StackPanel(); customBars.Content = global::AionDpsMeter.Local.Localization.T("Use class colours"); customBars.IsChecked = !preferences.OverlayCustomBars;
        window.Activated += (_, _) => customBars.IsChecked = !preferences.OverlayCustomBars;
        customBars.Click += (_, _) => { preferences.OverlayCustomBars = customBars.IsChecked != true; SavePreferences(); Refresh(); };
        bars.Children.Add(customBars); bars.Children.Add(SettingsHint(global::AionDpsMeter.Local.Localization.T("Turn off to give each participant a unique colour.")));
        window.AddPage("Appearance", UiIcon.Palette, "Make the meter feel at home on your screen.",
            SettingsCards(SettingsSection(global::AionDpsMeter.Local.Localization.T("Theme and highlights"), colours), SettingsSection(global::AionDpsMeter.Local.Localization.T("Participant bars"), bars)));

        var local = new StackPanel();
        local.Children.Add(SettingsHint(global::AionDpsMeter.Local.Localization.T("Fights, cached player details and settings are stored locally and survive app updates.")));
        local.Children.Add(SettingsAction(UiIcon.Folder, global::AionDpsMeter.Local.Localization.T("Open data folder"), () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppDataPaths.Root) { UseShellExecute = true })));
        local.Children.Add(SettingsAction(UiIcon.Folder, global::AionDpsMeter.Local.Localization.T("Import skill names"), ImportSkillNames));
        local.Children.Add(SettingsAction(UiIcon.Chart, global::AionDpsMeter.Local.Localization.T("Preview demo"), LoadDemo));
        window.AddPage("Local data", UiIcon.Folder, "Your fights and preferences stay on this PC.", SettingsCards(SettingsSection(global::AionDpsMeter.Local.Localization.T("Saved data and tools"), local)));

        var advanced = new StackPanel();
        filter.Width = double.NaN; filter.HorizontalAlignment = HorizontalAlignment.Stretch; filter.TextWrapping = TextWrapping.Wrap;
        advanced.Children.Add(SettingsField(global::AionDpsMeter.Local.Localization.T("Capture filter"), filter));
        decodeEu.Click += (_, _) => { preferences.DecodeEu = decodeEu.IsChecked == true; SavePreferences(); };
        recordSample.Content = global::AionDpsMeter.Local.Localization.T("Save a PCAP sample on the next manual start");
        recordSample.ToolTip = global::AionDpsMeter.Local.Localization.T("Applies to this session only. Automatic startup does not record a sample.");
        advanced.Children.Add(decodeEu); advanced.Children.Add(recordSample);
        advanced.Children.Add(SettingsHint(global::AionDpsMeter.Local.Localization.T("Capture changes take effect on the next start. PCAP samples are optional and saved only after you choose a file.")));
        advanced.Children.Add(SettingsAction(UiIcon.Save, global::AionDpsMeter.Local.Localization.T("Apply capture settings"), ApplyCapturePreferences));
        window.AddPage("Advanced", UiIcon.Settings, "Capture options and diagnostics for troubleshooting.",
            SettingsCards(SettingsSection(global::AionDpsMeter.Local.Localization.T("Capture options"), advanced), SettingsSection(global::AionDpsMeter.Local.Localization.T("Status"), status)));
        return window;
    }

    static StackPanel SettingsCards(params UIElement[] cards)
    {
        var panel = new StackPanel(); foreach (var card in cards) panel.Children.Add(card); return panel;
    }
    static TextBlock SettingsHint(string text)
    {
        var hint = new TextBlock { Text = Localization.T(text), TextWrapping = TextWrapping.Wrap, Margin = new(8, 3, 8, 12), FontSize = 12, LineHeight = 18 };
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
        SavePreferences(); notice = global::AionDpsMeter.Local.Localization.T("Capture settings saved. Changes apply the next time capture starts."); Refresh();
    }
}
