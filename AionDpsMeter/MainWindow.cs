using System.Collections;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using SharpPcap.LibPcap;

namespace AionDpsMeter.Local;

public sealed class Preferences
{
    public bool AutoStartConfigured { get; set; } = true;
    public bool OpenMeterOnStartup { get; set; } = true;
    public bool OpenCooldownsOnStartup { get; set; } = true;
    public bool DecodeEu { get; set; } = true;
    public Dictionary<int, double> CooldownFallbackSeconds { get; set; } = new();
    public int[] CooldownSkills { get; set; } = [];
    public bool CooldownShowReady { get; set; } = true;
    public bool CooldownOneSecondEarly { get; set; } = true;
    public bool CooldownLocked { get; set; }
    public double CooldownWidth { get; set; } = 300;
    public double CooldownHeight { get; set; } = 120;
    public double? CooldownLeft { get; set; }
    public double? CooldownTop { get; set; }
    public string Theme { get; set; } = "Midnight";
    public string Accent { get; set; } = "#55D6BE";
    public string BackAttackColor { get; set; } = "#E7CF7D";
    public string Filter { get; set; } = "tcp and not port 443 and not port 80 and not port 53 and not port 22";
    public string Adapter { get; set; } = "";
    public bool? AutoDetectAdapter { get; set; }
    public bool OverlayHealing { get; set; }
    public bool OverlayAllActors { get; set; }
    public string OverlayScope { get; set; } = "Auto";
    public bool OverlayCustomBars { get; set; }
    public bool OverlayCompact { get; set; } = true;
    public bool OverlayPinLocal { get; set; }
    public bool OverlayLocked { get; set; }
    public double OverlayWidth { get; set; } = 390;
    public double OverlayHeight { get; set; } = 530;
    public double? OverlayLeft { get; set; }
    public double? OverlayTop { get; set; }
}

public sealed partial class MainWindow : Window
{
    static readonly string[] MainViews = ["Encounter", "Cooldowns", "Party", "Skills", "Healing", "PvP", "Rotations", "Player stats", "Saved fights", "Trends"];
    static readonly string[] MoreViews = ["Openers", "Boss analysis", "Boss catalogue", "Report card", "Top 5%", "Top cohort", "Network events"];
    readonly FightStore store = new(AppDataPaths.FilePath("fights"));
    readonly Dictionary<string, DataGrid> grids = new();
    readonly TextBlock status = new() { Margin = new(10), TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    readonly TextBlock summary = new() { FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 4) };
    readonly TextBlock sessionState = new() { FontSize = 11, Margin = new(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock footer = new() { FontSize = 11, Margin = new(22, 10, 22, 12) };
    readonly TextBlock damageValue = new(), dpsValue = new(), durationValue = new();
    readonly TextBlock viewTitle = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new(178, 0, 0, 0) };
    readonly Expander renamePanel = new() { Header = "Encounter name", Visibility = Visibility.Collapsed, Margin = new(24, 0, 24, 10) };
    readonly Button showLive, clearFilter;
    readonly EncounterView encounterView;
    readonly BossAnalysisView bossView;
    readonly MeterView overview = new() { Margin = new(8, 8, 8, 12) };
    readonly MeterView pvpMeter = new() { Margin = new(8, 8, 8, 12) };
    readonly TextBlock pvpSummary = new() { Margin = new(8, 6, 8, 10), FontSize = 12, TextWrapping = TextWrapping.Wrap };
    readonly SettingsWindow settingsWindow;
    readonly Button meterButton, cooldownButton, settingsButton, moreButton, newFightButton;
    readonly ComboBox adapters = new() { Width = 330, DisplayMemberPath = "Description", Margin = new(4) };
    readonly ComboBox themes = new() { Width = 110, Margin = new(4), ItemsSource = new[] { "Midnight", "Slate", "Forest", "Ember", "Violet" } };
    readonly ComboBox backAttackColor = new() { Width = 110, Margin = new(4), IsEditable = true, ItemsSource = new[] { "#E7CF7D", "#FF6464", "#55D6BE", "#7C9EF7", "#B087E9" }, ToolTip = "Back-attack border colour: choose a preset or enter a hex colour. Transparency is preserved." };
    readonly TextBox accent = new() { Width = 90, Margin = new(4) };
    readonly TextBox filter = new() { Width = 455, Margin = new(4) };
    readonly TextBox encounter = new() { Text = "Local fight", Width = 180, Margin = new(4) };
    readonly TextBox character = new() { Width = 150, Margin = new(4), ToolTip = "Filter details by character name; blank shows everyone" };
    readonly TabControl tabs = new() { Margin = new(20, 4, 20, 0), TabStripPlacement = Dock.Left, Padding = new(12, 0, 0, 0), BorderThickness = new(0) };
    readonly RotationView rotationView;
    readonly TrendChart trendChart = new();
    readonly ComboBox trendCharacter = new() { MinWidth = 250, Margin = new(8), DisplayMemberPath = "Label" };
    readonly ComboBox trendMetric = new() { Width = 110, Margin = new(8), ItemsSource = new[] { "DPS", "HPS" }, SelectedIndex = 0 };
    readonly CheckBox pinLocal = new() { Content = "Pin my character first in the overlay", Margin = new(8), VerticalAlignment = VerticalAlignment.Center };
    readonly CheckBox customBars = new() { Content = "Use class colours", Margin = new(8), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock trendSummary = new() { Margin = new(8), TextWrapping = TextWrapping.Wrap };
    readonly TextBlock cohortSummary = new() { Margin = new(8), TextWrapping = TextWrapping.Wrap };
    readonly TextBlock differenceSummary = new() { Margin = new(8), TextWrapping = TextWrapping.Wrap };
    readonly TextBlock partySummary = new() { Margin = new(8), TextWrapping = TextWrapping.Wrap };
    readonly TextBlock cooldownSummary = new() { Margin = new(8), TextWrapping = TextWrapping.Wrap };
    readonly TextBlock setupMessage = new() { Margin = new(8), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    readonly CheckBox autoStart = new() { Content = "Resume my configured setup when Wingbeat opens", Margin = new(8) };
    readonly Dictionary<string, DataGrid> detailGrids = new();
    bool updatingTrends;
    TrendRow[] trendRows = [];
    long nextComparisonRefresh;
    bool detailsPending;
    readonly List<Fight> archive = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    readonly Button start;
    readonly CheckBox recordSample = new() { Content = "Save PCAP sample", Margin = new(8), VerticalAlignment = VerticalAlignment.Center };
    readonly CheckBox decodeEu = new() { Content = "Experimental EU decoding", IsChecked = true, Margin = new(8), VerticalAlignment = VerticalAlignment.Center };
    CombatEngine engine;
    readonly PlayerProfileCache playerProfiles;
    SkillNames skills = new();
    readonly SkillIcons skillIcons = new();
    CombatEngine? snapshotOwner;
    Fight? liveSnapshot, displayedSnapshot;
    long snapshotRevision;
    TotalRow[] displayedTotals = [];
    PvpData displayedPvp = new(0, 0, 0, [], []);
    bool archiveChanged;
    LocalCapture? capture;
    Fight? viewed;
    OverlayWindow? overlay;
    CooldownOverlayWindow? cooldownOverlay;
    readonly CooldownTracker cooldownTracker = new();
    Preferences preferences = new();
    Task<Fight[]>? autosave;
    long nextMaintenance, nextAutosave = Environment.TickCount64 + 30000;
    bool shuttingDown;
    bool npcapMissing;
    string notice = "Select an adapter, then Start. No account or internet service is used.";
    string PreferencePath => AppDataPaths.FilePath("preferences.json");
    Fight LiveSnapshot()
    {
        if (!ReferenceEquals(snapshotOwner, engine)) { liveSnapshot = null; snapshotOwner = engine; }
        return liveSnapshot = engine.SnapshotIfChanged(liveSnapshot, ref snapshotRevision);
    }
    Fight Current => viewed ?? LiveSnapshot();
    public MainWindow(bool demo)
    {
        demoMode = demo;
        Title = "Wingbeat \u2665 Aion 2 DPS Meter"; Width = 1150; Height = 870; MinWidth = 980; MinHeight = 650;
        FontFamily = new("Segoe UI"); FontSize = 13; UseLayoutRounding = true; SnapsToDevicePixels = true;
        playerProfiles = new(demo ? null : AppDataPaths.FilePath("player-profiles.json"));
        engine = new(profiles: playerProfiles);
        LocalLog.Write($"Application started; skill catalog entries {skills.Count}; " + (skills.LoadError ?? "catalog loaded"));
        LocalLog.Write($"Display metadata: {skillIcons.Count} icon families, {NpcNames.Default.Count} NPC names; " +
            (skillIcons.LoadError ?? "icons loaded") + "; " + (NpcNames.Default.LoadError ?? "NPC names loaded"));
        try { if (File.Exists(PreferencePath)) preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(PreferencePath)) ?? new(); } catch { }
        autoStart.IsChecked = preferences.AutoStartConfigured; decodeEu.IsChecked = preferences.DecodeEu;
        filter.Text = preferences.Filter; accent.Text = preferences.Accent; backAttackColor.Text = preferences.BackAttackColor; themes.SelectedItem = preferences.Theme; customBars.IsChecked = !preferences.OverlayCustomBars; pinLocal.IsChecked = preferences.OverlayPinLocal;
        character.Tag = "Filter characters…";
        var root = new DockPanel(); Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var top = new DockPanel { Margin = new(22, 16, 18, 12), LastChildFill = true }; header.Children.Add(top);
        var controls = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(controls, Dock.Right); top.Children.Add(controls);
        start = IconButton(UiIcon.Play, "Start capture", StartRequested); controls.Children.Add(start);
        start.SetResourceReference(Control.BackgroundProperty, "Accent"); start.Foreground = UiTheme.Brush("#10201C"); start.FontWeight = FontWeights.SemiBold;
        newFightButton = IconButton(UiIcon.NewFight, "New fight", NewFight); controls.Children.Add(newFightButton);
        meterButton = IconButton(UiIcon.Meter, "DPS overlay", ToggleOverlay); controls.Children.Add(meterButton);
        cooldownButton = IconButton(UiIcon.Cooldowns, "Cooldown overlay", ToggleCooldownOverlay); controls.Children.Add(cooldownButton);
        moreButton = IconButton(UiIcon.More, "More", ShowMore); controls.Children.Add(moreButton);
        settingsButton = IconButton(UiIcon.Settings, "Settings", ToggleSettings); controls.Children.Add(settingsButton);
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var mark = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 10, 0) };
        foreach (int height in new[] { 10, 19, 27 }) { var bar = new Border { Width = 4, Height = height, CornerRadius = new(2), Margin = new(0, 0, 3, 0), VerticalAlignment = VerticalAlignment.Bottom }; bar.SetResourceReference(Border.BackgroundProperty, "Accent"); mark.Children.Add(bar); }
        brand.Children.Add(mark); brand.Children.Add(new TextBlock { Text = "Wingbeat", FontSize = 21, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }); brand.Children.Add(sessionState); top.Children.Add(brand);
        var stats = new Grid { Margin = new(22, 0, 22, 10) };
        stats.ColumnDefinitions.Add(new() { Width = new GridLength(1.4, GridUnitType.Star) });
        for (int i = 0; i < 3; i++) stats.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var current = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 15, 0) };
        current.Children.Add(new TextBlock { Text = "CURRENT ENCOUNTER", FontSize = 10, Foreground = UiTheme.Brush("#93A2B8"), Margin = new(0, 0, 0, 5) }); current.Children.Add(summary);
        var fightActions = new StackPanel { Orientation = Orientation.Horizontal };
        var rename = Button("Rename", () => { renamePanel.Visibility = renamePanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; renamePanel.IsExpanded = renamePanel.Visibility == Visibility.Visible; if (renamePanel.IsExpanded) { encounter.Text = Current.Label; encounter.Focus(); encounter.SelectAll(); } });
        rename.Padding = new(8, 4, 8, 4); rename.Margin = new(0, 3, 6, 0); rename.Background = Brushes.Transparent; fightActions.Children.Add(rename);
        showLive = Button("Show live", () => { viewed = null; Refresh(); }); showLive.Padding = new(8, 4, 8, 4); showLive.Margin = new(0, 3, 0, 0); fightActions.Children.Add(showLive);
        current.Children.Add(fightActions); stats.Children.Add(current);
        AddMetric(stats, 1, "DAMAGE DONE", damageValue); AddMetric(stats, 2, "DAMAGE / SEC", dpsValue); AddMetric(stats, 3, "FIGHT TIME", durationValue); header.Children.Add(stats);
        var search = new DockPanel { Margin = new(20, 0, 20, 6) };
        clearFilter = Button("×", () => character.Clear()); clearFilter.ToolTip = "Show all players"; clearFilter.Width = 28; clearFilter.Padding = new(0); clearFilter.Visibility = Visibility.Hidden;
        DockPanel.SetDock(clearFilter, Dock.Right); search.Children.Add(clearFilter);
        DockPanel.SetDock(character, Dock.Right); character.Width = 200; search.Children.Add(character); search.Children.Add(viewTitle);
        var labelControls = new StackPanel { Orientation = Orientation.Horizontal };
        labelControls.Children.Add(encounter); labelControls.Children.Add(Button("Apply name", () => { var label = EncounterLabel(); if (viewed != null) viewed.Label = label; else engine.SetLabel(label); renamePanel.Visibility = Visibility.Collapsed; Refresh(); }));
        renamePanel.Content = labelControls; header.Children.Add(renamePanel); header.Children.Add(search);
        settingsWindow = BuildSettings();
        settingsWindow.IsVisibleChanged += (_, _) => UpdateToolbarState();
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); root.Children.Add(tabs);
        var encounterPanel = AddTab("Encounter", "Select a recorded encounter on the left. Current capture is first; saved encounters follow newest first. Participants and attributed skill damage are shown together. Select a participant to focus their skills. Adds remain in encounter totals. Each existing saved fight remains a separate entry; no inferred instance-run merging.");
        encounterPanel.Children.Remove(grids["Encounter"]);
        encounterView = new(code => skills.GetSkillName(code), skillIcons); encounterPanel.Children.Add(PageLayout.Fill(encounterView));
        encounterView.Selected += selected => { viewed = selected; encounter.Text = Current.Label; Refresh(); };
        var party = AddTab("Party", "Bars show captured party members, or named players when no roster is available. Click a bar for skills. Player names arrive when entering a zone. The full table below includes every observed actor.");
        party.Children.Remove(grids["Party"]);
        party.Children.Add(PageLayout.Fill(new SplitView(SplitView.Scroll(overview), new Expander { Header = "Full breakdown · all actors", Content = grids["Party"], Margin = new(8, 2, 8, 4) }, .6)));
        party.Children.Insert(1, CharacterDetailsHeader(partySummary));
        overview.ActorSelected += FocusActor;
        AddTab("Skills", "Damage per skill for every observed actor. Bar width and Damage % show the share of damage in the current character filter. Pet damage is attributed when its owner was discovered. Hits are damage events, not cast counts.");
        var pvp = AddTab("PvP", "Damage exchanged with you or your captured party. Opponents appear before their name arrives when their character skills identify them; these rows are marked name pending. Known NPCs and summon-only skills are excluded. Both sides use the same PvP damage window. Party membership and pet ownership must be captured to attribute their exchanges.");
        var pvpHeader = new DockPanel { LastChildFill = true };
        var lastPvp = Button("Last saved PvP fight", ShowLastPvp); DockPanel.SetDock(lastPvp, Dock.Right);
        pvpHeader.Children.Add(lastPvp); pvpHeader.Children.Add(pvpSummary);
        pvp.Children.Insert(1, pvpHeader);
        pvp.Children.Remove(grids["PvP"]);
        pvp.Children.Add(PageLayout.Fill(new SplitView(SplitView.Scroll(pvpMeter), grids["PvP"])));
        pvpMeter.ActorSelected += FocusActor;
        AddTab("Healing", "Beta: observed healing per healer, skill and recipient. Bars and Healing % show each row's share within the current character filter. Overhealing and effective healing are unavailable unless the protocol provides them.");
        var rotation = AddTab("Rotations", "Zoom with + / − or Ctrl + mouse wheel over the timeline; Fit fight shows everything. Inspect damage and healing anywhere across a player's lane. Click an event to pin details, then step with Left/Right. Zooming holds the view while capture continues; Follow latest / Escape resumes live updates. Each mark is an event, not an inferred cast.");
        rotation.Children.Remove(grids["Rotations"]);
        rotationView = new(grids["Rotations"], code => skills.GetSkillName(code), skillIcons.Find); rotation.Children.Add(PageLayout.Fill(rotationView));
        AddTab("Openers", "First 15 seconds from each character’s first recorded damage or healing event. Repeated hits are retained; this is an event sequence, not a cast-count estimate.");
        var bossPanel = AddTab("Boss analysis", "Inspect each observed boss entity in the selected encounter. Recorded cast signals, hits and defensive outcomes are separate evidence. Adds remain in the full encounter but are excluded from direct boss totals. Locations are catalogue candidates, not proof of difficulty.");
        bossPanel.Children.Remove(grids["Boss analysis"]); grids.Remove("Boss analysis");
        bossView = new(code => skills.GetSkillName(code), skillIcons); bossPanel.Children.Add(PageLayout.Fill(bossView));
        AddTab("Boss catalogue", "Offline boss and hero/legend catalogue. Search by name, NPC ID or possible instance. Family variants may span difficulties and locations; unassigned hero/legend entries are not automatically treated as bosses. Source client: " + BossCatalogue.Default.ClientVersion);
        bossView.BrowseCatalogue += id => { character.Text = id; tabs.SelectedItem = tabs.Items.Cast<TabItem>().First(t => t.Header?.ToString() == "Boss catalogue"); Refresh(); };
        AddTab("Saved fights", "Double-click a row to inspect it. Capturing continues in the background while browsing. Demo fights are marked and excluded from trends and comparisons.");
        grids["Saved fights"].MouseDoubleClick += (_, _) => { if (grids["Saved fights"].SelectedItem is SavedRow row) { viewed = archive.First(f => f.Id == row.Id); encounter.Text = viewed.Label; tabs.SelectedIndex = 0; Refresh(); } };
        var trendsPanel = AddTab("Trends", "Select an encounter to follow your DPS across attempts. Average gives each saved attempt equal weight; the chart shows the latest 100 attempts against the all-attempt average. Recognized bosses supply encounter names automatically. Character history stays together across servers. Double-click an encounter to open its latest fight, or a chart point to inspect that attempt. Switch to HPS for healing trends.");
        var trendControls = new WrapPanel(); trendControls.Children.Add(trendCharacter); trendControls.Children.Add(trendMetric);
        trendsPanel.Children.Insert(1, trendControls); trendsPanel.Children.Insert(2, trendSummary);
        trendsPanel.Children.Remove(grids["Trends"]); trendsPanel.Children.Add(PageLayout.Fill(new SplitView(trendChart, grids["Trends"], .45, 145, 90)));
        trendCharacter.ToolTip = "Character history across all saved encounters";
        trendCharacter.SelectionChanged += (_, _) => { if (!updatingTrends) Refresh(); }; trendMetric.SelectionChanged += (_, _) => { if (!updatingTrends) Refresh(); };
        grids["Trends"].SelectionMode = DataGridSelectionMode.Single;
        grids["Trends"].SetResourceReference(DataGrid.CellStyleProperty, "TrendCell");
        grids["Trends"].SelectionChanged += (_, _) => { if (!updatingTrends) RefreshTrendChart(); };
        grids["Trends"].MouseDoubleClick += (_, _) => { if (grids["Trends"].SelectedItem is EncounterTrendRow row) OpenTrendFight(row.FightId); };
        trendChart.OpenFight += OpenTrendFight;
        var reportPanel = AddTab("Report card", "Local-only comparisons: same encounter label, class, server and 1,000-CP band, with similar durations (−20%/+25%). Requires 20 observations for percentiles and 100 for top 5%. No population dataset is bundled.");
        reportPanel.Children.Insert(1, CharacterDetailsHeader(new TextBlock { Text = "Set class, CP and server to compare matching fights", Margin = new(8), TextWrapping = TextWrapping.Wrap }));
        var differences = AddTab("Top 5%", "Compare your damage, healing, criticals, event rate, opening damage and damage received with the top 5% of matching local fight observations. Requires 100 observations. Differences are descriptive, not proof that a playstyle caused better results.");
        differences.Children.Insert(1, CharacterDetailsHeader(differenceSummary));
        var cohortPanel = AddTab("Top cohort", "Top quarter needs 20 matching observations; top 5% needs 100. Match encounter, class, server, 1,000-CP band and duration (−20%/+25%). Skill shares include nonusers as zero.");
        cohortPanel.Children.Insert(1, CharacterDetailsHeader(cohortSummary));
        AddDetail(cohortPanel, "Cohort openers", "First recorded skills · damage and healing");
        AddTab("Player stats", "Latest captured stats for your local character, saved with the fight. Unknown stat IDs retain their raw values. These packets do not describe every nearby player.");
        var cooldownPanel = AddTab("Cooldowns", "Live countdowns for your tracked skills, even while browsing saved fights. Choose each skill once to include its charge tiers. Set an in-game duration for cast estimates when the server supplies no cooldown. The separate overlay shows the same countdowns.");
        var cooldownActions = new WrapPanel { Margin = new(8) };
        cooldownActions.Children.Add(IconButton(UiIcon.Settings, "Choose tracked skills", ConfigureCooldowns));
        cooldownActions.Children.Add(IconButton(UiIcon.Cooldowns, "Cooldown overlay", ToggleCooldownOverlay));
        cooldownPanel.Children.Insert(1, cooldownActions);
        cooldownPanel.Children.Insert(2, cooldownSummary);
        AddTab("Network events", "Latest decoded HP, buffs, casts, guard and dungeon events. These are stored with the fight. Unsupported records do not appear as decoded events.");
        var orderedViews = MainViews.Concat(MoreViews).Select(name => tabs.Items.Cast<TabItem>().Single(t => t.Header?.ToString() == name)).ToArray();
        tabs.Items.Clear(); foreach (var view in orderedViews) tabs.Items.Add(view);
        tabs.SelectedIndex = 0; UpdateNavigation();
        tabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.Source, tabs)) { UpdateNavigation(); Refresh(); } };
        character.TextChanged += (_, _) => Refresh();
        ApplyTheme(); ReloadArchive(); LoadAdapters();
        if (!demo) ContentRendered += ResumeConfiguredStartup;
        if (demo) LoadDemo(); else Refresh();
        timer.Tick += (_, _) => {
            try {
                if (capture != null) engine.AdvanceTime(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                FinishAutosave(wait: false);
                long now = Environment.TickCount64;
                if (capture != null && autosave == null && now >= nextMaintenance)
                {
                    bool saveCurrent = now >= nextAutosave;
                    Fight? snapshot = saveCurrent ? LiveSnapshot() : null;
                    var activeEngine = engine; var activeCapture = capture; var names = skills;
                    nextMaintenance = now + 1000;
                    if (saveCurrent) nextAutosave = now + 30000;
                    autosave = Task.Run(() => {
                        var saved = FightPersistence.Save(activeEngine, snapshot, f => SaveFightCore(f, names));
                        playerProfiles.Save();
                        if (saveCurrent) LogCapture(activeCapture, activeEngine);
                        return saved;
                    });
                }
                Refresh(skipUnchanged: true);
            } catch (Exception ex) { notice = "Local save/update failed: " + ex.Message; LocalLog.Write(notice); }
        };
        timer.Start();
        Closing += (_, e) => {
            if (shuttingDown) return;
            try { capture?.Dispose(); LogCapture(); capture = null; FinishAutosave(); engine.SaveCompleted(store); var f = LiveSnapshot(); if (f.HasRecordedData) SaveFight(f); playerProfiles.Save(); }
            catch (Exception ex) { e.Cancel = true; notice = "Could not save before closing: " + ex.Message; Refresh(); return; }
            shuttingDown = true; timer.Stop(); settingsWindow.Shutdown(); cooldownOverlay?.Close(); overlay?.Close(); LocalLog.Write("Application closed.");
        };
    }
    static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new(6) };
    static Border SettingsSection(string title, UIElement content)
    {
        var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new(8, 2, 0, 5) }); panel.Children.Add(content);
        var card = new Border { Child = panel, CornerRadius = new(10), Padding = new(14), Margin = new(0, 0, 0, 14), BorderThickness = new(1) };
        card.SetResourceReference(Border.BackgroundProperty, "Panel"); card.SetResourceReference(Border.BorderBrushProperty, "Edge"); return card;
    }
    FrameworkElement CharacterDetailsHeader(TextBlock text)
    {
        var header = new DockPanel(); var edit = Button("Party & character details", EditCharacters);
        DockPanel.SetDock(edit, Dock.Right); header.Children.Add(edit); header.Children.Add(text); return header;
    }
    void EditCharacters()
    {
        bool wasSaved = viewed != null;
        var editor = new CharacterEditor(Current, preferences) { Owner = this };
        if (editor.ShowDialog() != true || editor.Result == null) return;
        if (wasSaved) { viewed = editor.Result; SaveFight(viewed); ReloadArchive(); }
        else { engine.SetMetadata(editor.Result); var edited = LiveSnapshot(); if (edited.HasRecordedData) SaveFight(edited); ReloadArchive(); }
        displayedSnapshot = null; notice = "Character details saved locally."; Refresh();
    }
    void AddDetail(Panel panel, string key, string title)
    {
        var grid = new LiveTable { IsReadOnly = true, AutoGenerateColumns = true, CanUserAddRows = false, CanUserDeleteRows = false,
            RowHeight = 38, MinHeight = 60, EnableRowVirtualization = true, EnableColumnVirtualization = true,
            HeadersVisibility = DataGridHeadersVisibility.Column, Margin = new(8), ColumnWidth = DataGridLength.SizeToHeader };
        grid.AutoGeneratingColumn += (_, e) => { e.Column.Header = ColumnHeader(e.PropertyName); e.Column.MinWidth = 110; if (e.Column is DataGridTextColumn t && t.Binding is System.Windows.Data.Binding b && e.PropertyType == typeof(double)) b.StringFormat = "N1"; };
        detailGrids[key] = grid;
        var section = new Expander { Header = title, Content = grid, Margin = new(8) };
        if (panel is PageLayout && panel.Children.OfType<DataGrid>().LastOrDefault() is { } primary) {
            panel.Children.Remove(primary); panel.Children.Add(PageLayout.Fill(new SplitView(primary, section, .6)));
        } else panel.Children.Add(section);
    }
    void SetDetail(string key, IEnumerable rows)
    {
        var data = rows.Cast<object>().ToArray(); var grid = detailGrids[key];
        ((LiveTable)grid).SetRows(data);
    }
    sealed record TrendChoice(string Character, string Job) { public string Label => $"{Character} · {Job}"; }
    void RefreshTrends()
    {
        var all = Benchmarks.Trends(archive).Where(r => Matches(r.Character)).ToArray();
        var choices = all.Select(r => new TrendChoice(r.Character, r.Job)).DistinctBy(r => (r.Character.ToUpperInvariant(), r.Job.ToUpperInvariant())).OrderBy(r => r.Label).ToArray();
        var selected = trendCharacter.SelectedItem as TrendChoice;
        string? selectedEncounter = (grids["Trends"].SelectedItem as EncounterTrendRow)?.Encounter;
        bool healing = trendMetric.SelectedIndex == 1;
        updatingTrends = true;
        try {
            if (trendCharacter.ItemsSource == null || !trendCharacter.ItemsSource.Cast<TrendChoice>().SequenceEqual(choices)) trendCharacter.ItemsSource = choices;
            trendCharacter.SelectedItem = choices.FirstOrDefault(c => selected != null && c.Character.Equals(selected.Character, StringComparison.OrdinalIgnoreCase) && c.Job.Equals(selected.Job, StringComparison.OrdinalIgnoreCase))
                ?? choices.FirstOrDefault(c => c.Character.Equals(Current.Name(Current.LocalPlayer), StringComparison.OrdinalIgnoreCase)) ?? choices.FirstOrDefault();
            var choice = trendCharacter.SelectedItem as TrendChoice;
            trendRows = choice == null ? [] : EncounterTrends.ForCharacter(all, choice.Character, choice.Job);
            var summaries = EncounterTrends.Summarize(trendRows, healing);
            Set("Trends", summaries);
            grids["Trends"].SelectedItem = summaries.FirstOrDefault(r => r.Encounter.Equals(selectedEncounter, StringComparison.OrdinalIgnoreCase)) ?? summaries.FirstOrDefault();
            foreach (var column in grids["Trends"].Columns.Where(c => c.SortMemberPath is "Average" or "Latest" or "Best"))
                column.Header = column.SortMemberPath + (healing ? " HPS" : " DPS");
        } finally { updatingTrends = false; }
        RefreshTrendChart();
    }
    void RefreshTrendChart()
    {
        var selected = grids["Trends"].SelectedItem as EncounterTrendRow;
        bool healing = trendMetric.SelectedIndex == 1; string metric = healing ? "HPS" : "DPS";
        trendChart.Update(selected == null ? [] : EncounterTrends.ForEncounter(trendRows, selected.Encounter), healing);
        trendSummary.Text = selected == null ? "No saved encounters for this character yet." :
            $"{selected.Encounter} · {selected.Attempts:N0} attempts\nAverage {metric} {selected.Average:N1} · latest {selected.Latest:N1} · best {selected.Best:N1}";
    }
    void OpenTrendFight(string fightId)
    {
        if (archive.FirstOrDefault(f => f.Id == fightId) is not { } fight) return;
        viewed = fight; encounter.Text = fight.Label; tabs.SelectedIndex = 0; Refresh();
    }
    TotalRow? ComparisonActor(Fight f, TotalRow[] totals) => (character.Text.Length == 0 ? totals.FirstOrDefault(r => r.Id == f.LocalPlayer) : null)
        ?? totals.FirstOrDefault(r => Matches(r.Character) && f.Actors.GetValueOrDefault(r.Id)?.Player == true);
    string ComparisonStatus(Fight f, TotalRow? actor, int minimum, int count)
    {
        if (actor == null) return "Choose a player with the character filter; no identified player is available yet.";
        string missing = Benchmarks.Missing(f, actor); var identity = f.Actors[actor.Id];
        if (missing.Length > 0) return actor.Character + " · " + missing;
        return $"{actor.Character} · {identity.Job} · CP {Benchmarks.Band(actor.CP)} · {count} matching observations" +
            (count < minimum ? $" · need {minimum}; save or import matching fights" : " · locally observed, ranked by DPS") + " · use the character filter to choose another player";
    }
    static void AddMetric(Grid grid, int column, string label, TextBlock value)
    {
        var card = new Border { CornerRadius = new(8), Padding = new(16, 10, 16, 10), Margin = new(8, 0, 0, 0), BorderThickness = new(1) };
        card.SetResourceReference(Border.BorderBrushProperty, "Edge");
        card.SetResourceReference(Border.BackgroundProperty, "Panel");
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = label, FontSize = 10, Foreground = UiTheme.Brush("#93A2B8"), Margin = new(0, 0, 0, 8) });
        value.FontSize = 27; value.FontWeight = FontWeights.SemiBold; content.Children.Add(value);
        if (column == 2) value.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        card.Child = content; Grid.SetColumn(card, column); grid.Children.Add(card);
    }
    void ShowMore()
    {
        var menu = CreateMoreMenu(); menu.IsOpen = true;
    }
    ContextMenu CreateMoreMenu()
    {
        var menu = UiTheme.Menu(this, moreButton); menu.Placement = PlacementMode.Bottom;
        foreach (string title in MoreViews) {
            var item = new MenuItem { Header = title, Icon = UiIcons.Create(UiIcons.ForView(title), 16) };
            item.Click += (_, _) => { character.Clear(); tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(t => t.Header?.ToString() == title); };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var files = CreateFilesMenu(); var actions = files.Items.Cast<object>().ToArray(); files.Items.Clear();
        foreach (var action in actions) menu.Items.Add(action);
        return menu;
    }
    void UpdateNavigation()
    {
        foreach (TabItem tab in tabs.Items)
            tab.Visibility = MainViews.Contains(tab.Header?.ToString()) || ReferenceEquals(tab, tabs.SelectedItem) ? Visibility.Visible : Visibility.Collapsed;
    }
    ContextMenu CreateFilesMenu()
    {
        var menu = UiTheme.Menu(this);
        foreach (var (label, action) in new (string, Action)[] { ("Save current fight", () => { SaveFight(Current); ReloadArchive(); Refresh(); }), ("Import fights…", Import), ("Export current fight…", Export), ("Import events…", ImportEvents), ("Open data folder", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppDataPaths.Root) { UseShellExecute = true })) })
        {
            var item = new MenuItem { Header = label, Icon = UiIcons.Create(label.StartsWith("Save") || label.StartsWith("Export") ? UiIcon.Save : UiIcon.Folder, 16) };
            item.Click += (_, _) => { try { action(); } catch (Exception ex) { LocalLog.Write(label + " failed: " + ex); MessageBox.Show(this, ex.Message, "Wingbeat", MessageBoxButton.OK, MessageBoxImage.Error); } };
            menu.Items.Add(item);
        }
        return menu;
    }
    void FocusActor(int id)
        => FocusActor(id, false);
    void FocusActor(int id, bool healing)
    {
        if (overlay?.IsActive == true && capture != null) viewed = null;
        character.Text = Current.Name(id); tabs.SelectedItem = tabs.Items.Cast<TabItem>().First(t => t.Header.ToString() == (healing ? "Healing" : "Skills"));
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate(); Refresh();
    }
    static Button Button(string text, Action action)
    {
        var b = new Button { Content = text, Margin = new(4), Padding = new(12, 6, 12, 6) };
        b.Click += (_, _) => { try { action(); } catch (Exception ex) { LocalLog.Write(text + " failed: " + ex); MessageBox.Show(ex.Message, "Wingbeat Local", MessageBoxButton.OK, MessageBoxImage.Error); } };
        return b;
    }
    static Button IconButton(UiIcon icon, string label, Action action)
    {
        var button = Button(label, action); button.Padding = new(10, 8, 10, 8); button.MinHeight = 36;
        UiIcons.SetLabel(button, icon, label); return button;
    }
    void UpdateToolbarState()
    {
        void State(Button button, bool visible, string name) {
            button.SetResourceReference(Control.BorderBrushProperty, visible ? "Accent" : "Edge");
            button.SetResourceReference(Control.ForegroundProperty, visible ? "Accent" : "Text");
            button.ToolTip = (visible ? "Hide " : "Show ") + name;
            System.Windows.Automation.AutomationProperties.SetHelpText(button, visible ? "Visible" : "Hidden");
        }
        State(meterButton, overlay?.IsVisible == true, "DPS overlay");
        State(cooldownButton, cooldownOverlay?.IsVisible == true, "cooldown overlay");
        State(settingsButton, settingsWindow?.IsVisible == true, "settings");
    }
    PageLayout AddTab(string title, string help)
    {
        var panel = new PageLayout();
        panel.Children.Add(new Expander { Header = "About this view", Foreground = UiTheme.Brush("#93A2B8"), FontSize = 11, Margin = new(8, 4, 8, 8), Content = new TextBlock { Text = help, TextWrapping = TextWrapping.Wrap, Margin = new(6, 10, 6, 6) } });
        var grid = new LiveTable { IsReadOnly = true, AutoGenerateColumns = true, CanUserAddRows = false, CanUserDeleteRows = false, EnableRowVirtualization = true, EnableColumnVirtualization = true, MinHeight = 60, RowHeight = 38, HeadersVisibility = DataGridHeadersVisibility.Column, Margin = new(8), GridLinesVisibility = DataGridGridLinesVisibility.None, BorderThickness = new(0) };
        if (title == "Player stats") grid.RowHeight = 58;
        grid.ColumnWidth = new DataGridLength(1, DataGridLengthUnitType.Star);
        grid.AutoGeneratingColumn += (_, e) => {
            if (title == "Player stats") {
                if (e.PropertyName is "Stat" or "Value") e.Column = StatPresentation.Column(e.PropertyName);
                else e.Cancel = true;
                return;
            }
            e.Column.Header = ColumnHeader(e.PropertyName);
            if (e.PropertyName is "Id" or "FightId" or "ActorId") e.Cancel = true;
            if (title == "Trends" && e.PropertyName is "Character" or "Job" or "Server") e.Cancel = true;
            if (title == "Report card" && e.PropertyName == "Encounter" || title == "Top 5%" && e.PropertyName == "Tier" || title == "Top cohort" && e.PropertyName == "Code") e.Cancel = true;
            if (e.PropertyName == "SkillCode" && title is "Skills" or "Healing" or "Rotations" or "Cooldowns") e.Cancel = true;
            if (e.PropertyName == "Skill" && title is "Skills" or "Healing" or "Cooldowns" or "Openers" or "Rotations")
            {
                var row = new FrameworkElementFactory(typeof(DockPanel));
                row.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                var icon = new FrameworkElementFactory(typeof(Image));
                icon.SetValue(FrameworkElement.WidthProperty, 24d); icon.SetValue(FrameworkElement.HeightProperty, 24d);
                icon.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
                icon.SetValue(DockPanel.DockProperty, Dock.Left);
                icon.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding("SkillCode") { Converter = skillIcons });
                icon.SetBinding(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding("SkillCode") { StringFormat = "Skill ID {0}" });
                row.AppendChild(icon);
                var name = new FrameworkElementFactory(typeof(TextBlock));
                name.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
                name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Skill"));
                name.SetBinding(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding("Skill"));
                row.AppendChild(name);
                e.Column = new DataGridTemplateColumn { Header = "Skill", SortMemberPath = "Skill", MinWidth = 200,
                    Width = new DataGridLength(2, DataGridLengthUnitType.Star), CellTemplate = new DataTemplate { VisualTree = row } };
                return;
            }
            e.Column.MinWidth = 85; e.Column.Width = 110;
            if (e.PropertyName is "Character" or "Healer" or "Target" or "Encounter" or "Fields") e.Column.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
            if (e.PropertyName is "Character" or "Healer" or "Target") e.Column.MinWidth = 130;
            if (e.PropertyName is "Status" or "Source" or "Loadout" or "Metric") e.Column.MinWidth = 190;
            if (title is "Report card" or "Top 5%" or "Top cohort") { e.Column.MinWidth = Math.Max(115, e.Column.MinWidth); e.Column.Width = DataGridLength.SizeToHeader; }
            if (title == "Trends") {
                if (e.PropertyName is "Average" or "Latest" or "Best") e.Column.Header = e.PropertyName + (trendMetric.SelectedIndex == 1 ? " HPS" : " DPS");
                e.Column.Width = e.PropertyName switch { "LastFought" => 155, "Encounter" => new DataGridLength(1, DataGridLengthUnitType.Star), "Attempts" => 95, _ => 105 };
                e.Column.MinWidth = e.PropertyName == "Encounter" ? 180 : 75;
                if (e.PropertyName == "LastFought" && e.Column is DataGridTextColumn dateColumn && dateColumn.Binding is System.Windows.Data.Binding dateBinding) dateBinding.StringFormat = "g";
            }
            if (title == "Top 5%") { e.Column.MinWidth = 65; e.Column.Width = e.PropertyName switch { "Metric" => 270, "Unit" => 105, "Samples" => 70, _ => 110 }; }
            if (title == "Top cohort" && e.PropertyName == "Skill") e.Column.Width = 230;
            if (title == "Report card") { e.Column.MinWidth = 75; e.Column.Width = e.PropertyName switch { "Character" => 150, "Status" => 370, "DPS" => 75, _ => 115 }; }
            if (e.PropertyName == "HealingShare") { e.Column.Header = "Healing %"; e.Column.Width = 100; e.Column.DisplayIndex = 5; }
            if (e.PropertyName == "DamageShare") { e.Column.Header = "Damage %"; e.Column.Width = 100; }
            if (e.PropertyName == "LandedPercent") e.Column.Header = "Observed landed %";
            if (e.PropertyName == "HealerShare") e.Column.Header = "Healer share %";
            if (e.PropertyName == "LocalPercentile") e.Column.Header = "Local percentile";
            if (title is "Boss analysis" or "Boss catalogue") {
                e.Column.Header = e.PropertyName switch { "PossibleInstances" => "Possible instances", "NpcIds" => "NPC IDs", "DamageToBoss" => "To boss", "DamageFromBoss" => "From boss", "ObservedDeaths" => "Deaths", "FirstSeen" => "First seen (s)", "LastSeen" => "Last seen (s)", _ => e.Column.Header };
                if (e.PropertyName is "Boss" or "PossibleInstances") { e.Column.MinWidth = 185; e.Column.Width = new DataGridLength(1.5, DataGridLengthUnitType.Star); }
                if (e.PropertyName is "Hits" or "Entities" or "ObservedDeaths") { e.Column.MinWidth = 65; e.Column.Width = 65; }
                if (e.PropertyName is "DamageToBoss" or "DamageFromBoss") { e.Column.MinWidth = 100; e.Column.Width = 100; }
            }
            if (title == "PvP")
            {
                e.Column.Header = e.PropertyName switch { "DamageToUs" => "Enemy damage", "DamageFromUs" => "Our damage", "DPS" => "Enemy DPS", _ => e.PropertyName };
                if (e.PropertyName == "Opponent") { e.Column.MinWidth = 150; e.Column.Width = new DataGridLength(1.5, DataGridLengthUnitType.Star); }
                if (e.PropertyName == "Identity") e.Column.MinWidth = 115;
                if (e.PropertyName is "DamageToUs" or "DamageFromUs") e.Column.MinWidth = 110;
            }
            if (e.Column is DataGridTextColumn textColumn && textColumn.Binding is System.Windows.Data.Binding binding)
            {
                if (e.PropertyType == typeof(double) || e.PropertyType == typeof(double?)) binding.StringFormat = "N1";
                if (e.PropertyName is "DamageShare" or "HealingShare") binding.StringFormat = "{0:0.0}%";
                if (e.PropertyName is "Damage" or "Healing" or "Dealt" or "Received" or "DamageTaken" or "DamageToUs" or "DamageFromUs") binding.StringFormat = "N0";
            }
        };
        grids[title] = grid; panel.Children.Add(PageLayout.Fill(grid));
        double minimum = title switch { "Encounter" => 350, "Rotations" => 460, "Boss analysis" => 580, "Trends" => 380, "Party" or "PvP" or "Top cohort" => 370, _ => 260 };
        var tab = new TabItem { Header = title, HeaderTemplate = UiIcons.NavigationHeader(title), ToolTip = title,
            Tag = title switch { "Encounter" => "COMBAT", "Rotations" => "ANALYSIS", "Saved fights" => "LIBRARY", "Report card" => "INSIGHTS", "Network events" => "ADVANCED", _ => null }, Content = new ViewportHost(panel, minimum) };
        System.Windows.Automation.AutomationProperties.SetName(tab, title); tabs.Items.Add(tab);
        return panel;
    }
    static string ColumnHeader(string property) => property switch {
        "SkillCode" => "Skill ID", "CritPercent" => "Crit %",
        "CPBand" => "CP band", "LocalSamples" => "Samples", "MedianDPS" => "Median DPS", "TopQuarterDPS" => "Top 25% DPS", "TopFivePercentDPS" => "Top 5% DPS",
        "PeerMedian" => "Peer median", "TopMean" => "Top mean", "UsagePercent" => "Usage %", "MeanDamageShare" => "Top share %", "YourDamageShare" => "Your share %",
        "MeanEventsPerMinute" => "Top events/min", "YourEventsPerMinute" => "Your events/min", "KnownLoadouts" => "Known loadouts", "FirstSkill" => "First skill",
        "FirstEvent" => "First event", "YourFirstSkill" => "Your first skill", "RecordedEvents" => "Family events", "DamageTakenPerSecond" => "Damage taken/s",
        _ => System.Text.RegularExpressions.Regex.Replace(property, "(?<=[a-z0-9])(?=[A-Z])", " ")
    };
    void LoadAdapters()
    {
        if (capture != null) return;
        npcapMissing = false;
        try {
            string selected = (adapters.SelectedItem as LibPcapLiveDevice)?.Name ?? preferences.Adapter;
            var devices = LibPcapLiveDeviceList.Instance.ToArray(); adapters.ItemsSource = devices;
            adapters.SelectedItem = GameAdapterDetector.Automatic(preferences) ? null : devices.FirstOrDefault(d => string.Equals(d.Name, selected, StringComparison.OrdinalIgnoreCase));
            adapterDetection = null;
            notice = adapters.Items.Count == 0 ? "No capture adapters found. Check your network connection and Npcap installation, then refresh adapters." : GameAdapterDetector.Automatic(preferences)
                ? "Connect Aion to a server, then use Detect game adapter or Start capture." : "Choose the adapter carrying game traffic. Use Npcap loopback if the game uses a local proxy.";
        }
        catch (Exception ex) {
            adapters.ItemsSource = null;
            npcapMissing = NpcapSetup.IsLoadFailure(ex);
            notice = "Npcap unavailable: " + ex.Message + " Demo and saved fights remain available.";
        }
        adapterStatus.Text = notice; UpdateAdapterControls();
    }
    void ShowSetup(string message)
    {
        setupMessage.Text = message; setupMessage.Visibility = Visibility.Visible;
        ShowSettings("Capture");
    }
    async void ResumeConfiguredStartup(object? sender, EventArgs e)
    {
        ContentRendered -= ResumeConfiguredStartup;
        if (adapterBusy) return;
        if (preferences.AutoStartConfigured && GameAdapterDetector.Automatic(preferences) && !npcapMissing) {
            adapterBusy = true; UpdateAdapterControls();
            try { await DetectGameAdapterAsync(); }
            finally { adapterBusy = false; UpdateAdapterControls(); }
        }
        if (shuttingDown) return;
        var plan = StartupPlan.Create(preferences, adapters.Items.Cast<LibPcapLiveDevice>().Select(d => d.Name), npcapMissing, detection: adapterDetection);
        if (plan.SetupMessage != null) ShowSetup(plan.SetupMessage);
        if (plan.StartCapture) {
            adapters.SelectedItem = adapters.Items.Cast<LibPcapLiveDevice>().Single(d => string.Equals(d.Name, plan.CaptureAdapter, StringComparison.OrdinalIgnoreCase));
            try { Start(automatic: true); }
            catch (Exception ex) { LocalLog.Write("Automatic capture startup failed: " + ex); ShowSetup("Capture could not start. Check the adapter, Npcap and capture filter, then try Start capture. " + ex.Message); }
        }
        RestoreStartupOverlays(plan);
        if (npcapMissing) NpcapSetup.CreateDialog(this).ShowDialog();
        Refresh();
    }
    void RestoreStartupOverlays(StartupPlan plan)
    {
        foreach (var (enabled, open) in new (bool, Action)[] { (plan.OpenMeter, OpenOverlay), (plan.OpenCooldowns, OpenCooldownOverlay) }) {
            if (!enabled) continue;
            try { open(); }
            catch (Exception ex) { LocalLog.Write("Overlay restoration failed: " + ex); ShowSetup("An overlay could not reopen. Use its toolbar button to try again. " + ex.Message); }
        }
    }
    void Start(bool automatic = false)
    {
        if (capture != null) return;
        if (adapters.SelectedItem == null && !GameAdapterDetector.Automatic(preferences)) LoadAdapters();
        if (npcapMissing) { if (!automatic) NpcapSetup.CreateDialog(this).ShowDialog(); return; }
        if (adapters.SelectedItem is not LibPcapLiveDevice device) throw new InvalidOperationException("Select an Npcap adapter first.");
        if (string.IsNullOrWhiteSpace(filter.Text)) throw new InvalidOperationException("Enter a TCP capture filter.");
        var label = EncounterLabel();
        FinishAutosave(); engine.SaveCompleted(store); var previous = LiveSnapshot(); if (previous.HasRecordedData) SaveFight(previous);
        cooldownTracker.Reset();
        engine = new(decodeEu.IsChecked == true ? new EuGameDecoder() : null, playerProfiles); engine.NewFight(label); viewed = null;
        var newCapture = new LocalCapture(device, engine.Process);
        string? samplePath = null;
        if (!automatic && recordSample.IsChecked == true)
        {
            var dialog = new SaveFileDialog { Filter = "Packet capture|*.pcap", FileName = "eu-game-sample.pcap", Title = "Save a private packet sample (do not commit captures to GitHub)" };
            if (dialog.ShowDialog(this) != true) return;
            samplePath = dialog.FileName;
        }
        try { newCapture.Start(filter.Text, samplePath); }
        catch (Exception ex) when (NpcapSetup.IsLoadFailure(ex)) {
            npcapMissing = true;
            if (automatic) throw;
            NpcapSetup.CreateDialog(this).ShowDialog(); return;
        }
        capture = newCapture;
        nextMaintenance = Environment.TickCount64; nextAutosave = nextMaintenance + 30000;
        UiIcons.SetLabel(start, UiIcon.Stop, "Stop & save"); UpdateAdapterControls(); filter.IsEnabled = false; decodeEu.IsEnabled = false;
        preferences.AutoDetectAdapter = GameAdapterDetector.Automatic(preferences);
        preferences.Adapter = device.Name; preferences.Filter = filter.Text; preferences.DecodeEu = decodeEu.IsChecked == true; SavePreferences();
        setupMessage.Visibility = Visibility.Collapsed;
        notice = engine.DecoderStatus + (samplePath == null ? "" : " Private sample recording enabled (256 MB limit).");
        LocalLog.Write("Capture started: " + engine.DecoderStatus);
        Refresh();
    }
    void StopAndSave()
    {
        capture?.Dispose(); LogCapture(); capture = null; UiIcons.SetLabel(start, UiIcon.Play, "Start capture"); UpdateAdapterControls(); filter.IsEnabled = true; decodeEu.IsEnabled = true;
        FinishAutosave(); engine.SaveCompleted(store); var f = LiveSnapshot(); if (f.HasRecordedData) SaveFight(f); playerProfiles.Save(); ReloadArchive(); notice = "Capture stopped. Recorded events saved locally."; Refresh();
    }
    void NewFight()
    {
        var label = EncounterLabel(); FinishAutosave();
        engine.EndFight(label); engine.SaveCompleted(store);
        viewed = null; ReloadArchive(); Refresh();
    }
    string EncounterLabel() => string.IsNullOrWhiteSpace(encounter.Text) ? throw new InvalidOperationException("Enter an encounter label.") : encounter.Text.Trim();
    void SaveFight(Fight f)
    {
        FinishAutosave();
        SaveFightCore(f, skills);
    }
    void SaveFightCore(Fight f, SkillNames names)
    {
        store.Save(f);
        if (f.Demo) return;
        try { SkillAudit.Write(f, names); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LocalLog.Write("Skill diagnostics could not be saved: " + ex.Message); }
    }
    void FinishAutosave(bool wait = true)
    {
        if (autosave == null || (!wait && !autosave.IsCompleted)) return;
        try
        {
            foreach (var fight in autosave.GetAwaiter().GetResult())
            {
                archive.RemoveAll(f => f.Id == fight.Id); archive.Insert(0, fight); archiveChanged = true;
            }
        }
        finally { autosave = null; }
    }
    void LogCapture() { if (capture != null) LogCapture(capture, engine); }
    static void LogCapture(LocalCapture activeCapture, CombatEngine activeEngine)
    {
        var timing = activeCapture.Timing;
        LocalLog.Write($"Capture: packets {activeCapture.Received}, drops {activeCapture.Dropped}, errors {activeCapture.Errors}; {activeEngine.DecoderDetails}; identity events {activeEngine.IdentityEvents}; " +
            $"queued {timing.QueuedPackets} (peak {timing.PeakQueuedPackets}), driver avg {timing.DriverDelivery.AverageMs:F2} ms, queue avg/max {timing.QueueWait.AverageMs:F2}/{timing.QueueWait.MaxMs:F2} ms, " +
            $"processing avg {timing.Processing.AverageMs:F2} ms, capture-to-processed avg/max {timing.CaptureToProcessed.AverageMs:F2}/{timing.CaptureToProcessed.MaxMs:F2} ms; {activeCapture.LastError}");
    }
    void SaveCurrent() { var f = Current; if (!f.HasRecordedData) throw new InvalidOperationException("No recorded events to save."); SaveFight(f); ReloadArchive(); notice = "Saved locally: " + f.Label; Refresh(); }
    void ImportSkillNames()
    {
        var dialog = new OpenFileDialog { Filter = "Skill name catalog JSON|*.json", Title = "Load a local skill ID to name catalog" };
        if (dialog.ShowDialog(this) != true) return;
        FinishAutosave();
        skills = SkillNames.Import(dialog.FileName);
        notice = $"Loaded {skills.Count:N0} skill names locally. Saved fights also use these names.";
        LocalLog.Write(notice); Refresh();
        if (Current.HasRecordedData && !Current.Demo) SkillAudit.Write(Current, skills);
    }
    void Import()
    {
        var dialog = new OpenFileDialog { Filter = "Local fight JSON|*.json", Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;
        FinishAutosave();
        foreach (var file in dialog.FileNames) { var f = store.Load(file); store.Save(f); viewed = f; }
        ReloadArchive(); notice = "Imported local fight files."; Refresh();
    }
    void Export()
    {
        var dialog = new SaveFileDialog { Filter = "Local fight JSON|*.json", FileName = "fight.json" };
        if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
    }
    void ImportEvents()
    {
        var dialog = new OpenFileDialog { Filter = "Wingbeat normalized events|*.jsonl;*.ndjson" };
        if (dialog.ShowDialog(this) != true) return;
        var events = CombatEventFile.Read(dialog.FileName);
        FinishAutosave();
        var replay = new CombatEngine(); replay.NewFight(EncounterLabel());
        foreach (var item in events) replay.Apply(item);
        replay.SaveCompleted(store); viewed = replay.Snapshot();
        if (viewed.HasRecordedData) store.Save(viewed);
        ReloadArchive(); notice = "Imported normalized events. This is an application interchange format, not decoded game traffic."; Refresh();
    }
    void LoadDemo()
    {
        viewed = SelfTest.Demo(); viewed.Label = "Training encounter";
        string[] names = ["Aster", "Veyra", "Kaelis", "Nym", "Solis", "Thalen", "Liora", "Ren", "Seren", "Orion"];
        string[] jobs = ["Cleric", "Gladiator", "Sorcerer", "Assassin", "Chanter", "Templar", "Ranger", "Spiritmaster", "Sorcerer", "Gladiator"];
        for (int i = 0; i < names.Length; i++) if (viewed.Actors.TryGetValue(i + 1, out var actor)) viewed.Actors[i + 1] = actor with { Name = names[i], Job = jobs[i] };
        int[] demoSkills = [17010001, 11010001, 15010001, 13010001, 18010001, 12010001, 14010001, 16010001, 15020001, 11020001];
        for (int i = 0; i < viewed.Damage.Count; i++)
            if (viewed.Damage[i].ActorId is >= 1 and <= 10) viewed.Damage[i] = viewed.Damage[i] with { SkillCode = demoSkills[viewed.Damage[i].ActorId - 1] };
        for (int i = 0; i < viewed.Healing.Count; i++) viewed.Healing[i] = viewed.Healing[i] with { SkillCode = 17100001 };
        encounter.Text = viewed.Label; notice = "DEMO DATA — illustrative events, not captured game traffic."; Refresh();
    }
    void ReloadArchive()
    {
        FinishAutosave();
        archive.Clear(); archiveChanged = true; int skipped = 0;
        foreach (var path in store.Files()) { try { archive.Add(store.Load(path)); } catch { skipped++; } }
        if (skipped > 0) notice = $"Skipped {skipped} invalid fight file(s); original files retained.";
    }
    void ShowLastPvp()
    {
        var last = archive.Where(f => !f.Demo).OrderByDescending(f => f.Created).FirstOrDefault(f => PvpAnalysis.Build(f).HasOpponents);
        if (last == null) { notice = "No saved PvP fight found yet."; Refresh(); return; }
        viewed = last; character.Clear();
        tabs.SelectedItem = tabs.Items.Cast<TabItem>().First(t => t.Header?.ToString() == "PvP"); Refresh();
    }
    void ConfigureCooldowns()
    {
        var editor = new CooldownSettingsWindow(preferences, skills, cooldownTracker.ObservedSkills,
            () => { SavePreferences(); cooldownOverlay?.Refresh(); }, OpenCooldownOverlay) { Owner = settingsWindow.IsActive ? settingsWindow : this, Topmost = cooldownOverlay?.IsVisible == true };
        editor.ShowDialog();
    }
    void ToggleCooldownOverlay()
    {
        if (cooldownOverlay?.IsVisible == true) cooldownOverlay.Hide(); else OpenCooldownOverlay();
    }
    void OpenCooldownOverlay()
    {
        if (cooldownOverlay != null) { cooldownOverlay.Refresh(); cooldownOverlay.Show(); cooldownOverlay.Activate(); return; }
        var window = new CooldownOverlayWindow(preferences, cooldownTracker, skills, skillIcons, () => capture != null, ConfigureCooldowns, SavePreferences);
        cooldownOverlay = window;
        window.IsVisibleChanged += (_, _) => UpdateToolbarState();
        window.Closed += (_, _) => { if (ReferenceEquals(cooldownOverlay, window)) cooldownOverlay = null; UpdateToolbarState(); };
        try { window.Show(); } catch { cooldownOverlay = null; window.Close(); throw; }
    }
    void ToggleOverlay()
    {
        if (overlay?.IsVisible == true) overlay.Hide(); else OpenOverlay();
    }
    void OpenOverlay()
    {
        if (overlay != null) {
            var current = capture != null ? LiveSnapshot() : Current;
            bool same = ReferenceEquals(current, displayedSnapshot);
            overlay.Update(current, preferences, rows: same ? displayedTotals : null, pvpData: same ? displayedPvp : null);
            overlay.Show(); overlay.Activate(); return;
        }
        var opening = new OverlayWindow(NewFight, id => FocusActor(id, preferences.OverlayHealing), () => { customBars.IsChecked = !preferences.OverlayCustomBars; pinLocal.IsChecked = preferences.OverlayPinLocal; SavePreferences(); });
        overlay = opening;
        opening.IsVisibleChanged += (_, _) => UpdateToolbarState();
        opening.Closed += (_, _) => { if (ReferenceEquals(overlay, opening)) overlay = null; UpdateToolbarState(); };
        try {
            var fight = capture != null ? LiveSnapshot() : Current;
            bool cached = ReferenceEquals(fight, displayedSnapshot);
            opening.Update(fight, preferences, rows: cached ? displayedTotals : null, pvpData: cached ? displayedPvp : null);
            opening.Show();
        } catch {
            if (ReferenceEquals(overlay, opening)) overlay = null;
            opening.Close(); throw;
        }
    }
    void SetPinLocal()
    {
        preferences.OverlayPinLocal = pinLocal.IsChecked == true; SavePreferences(); Refresh();
    }
    void ApplyPreferences()
    {
        _ = (Color)ColorConverter.ConvertFromString(accent.Text);
        _ = (Color)ColorConverter.ConvertFromString(backAttackColor.Text);
        preferences.BackAttackColor = backAttackColor.Text;
        preferences.Theme = themes.SelectedItem?.ToString() ?? "Midnight"; preferences.Accent = accent.Text;
        preferences.OverlayCustomBars = customBars.IsChecked != true;
        SavePreferences(); ApplyTheme(); Refresh();
    }
    void SavePreferences()
    {
        try { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PreferencePath)!); File.WriteAllText(PreferencePath, JsonSerializer.Serialize(preferences)); settingsWindow?.ShowStatus("Settings saved on this PC."); }
        catch (Exception ex) { notice = "Could not save settings: " + ex.Message; LocalLog.Write(notice); settingsWindow?.ShowStatus(notice); }
    }
    void ApplyTheme()
    {
        try { _ = (Color)ColorConverter.ConvertFromString(preferences.Accent); } catch { preferences.Accent = "#55D6BE"; }
        UiTheme.Apply(this, preferences);
        settingsWindow.ApplyTheme(preferences); UpdateToolbarState();
        var accentColor = ((SolidColorBrush)Resources["Accent"]).Color;
        start.Foreground = .2126 * accentColor.R + .7152 * accentColor.G + .0722 * accentColor.B > 145 ? UiTheme.Brush("#10201C") : Brushes.White;
        tabs.Background = Background; tabs.Foreground = Foreground;
        status.Foreground = footer.Foreground = UiTheme.Brush("#93A2B8");
        foreach (var grid in grids.Values.Concat(detailGrids.Values)) {
            grid.Background = UiTheme.Brush("#121B28"); grid.Foreground = Foreground;
            grid.RowBackground = UiTheme.Brush("#182231"); grid.AlternatingRowBackground = UiTheme.Brush("#121B28");
        }
        if (grids.TryGetValue("Skills", out var skillGrid)) SkillDamageDisplay.Apply(skillGrid, preferences.Accent);
        if (grids.TryGetValue("Healing", out var healingGrid)) SkillDamageDisplay.Apply(healingGrid, preferences.Accent, nameof(HealingRow.HealingShare));
    }
    internal static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    bool Matches(string name) => name.Contains(character.Text, StringComparison.OrdinalIgnoreCase);
    void Set(string tab, IEnumerable data)
    {
        var items = data.Cast<object>().ToArray(); var grid = grids[tab];
        ((LiveTable)grid).SetRows(items);
    }
    void Refresh(bool skipUnchanged = false)
    {
        if (grids.Count == 0) return;
        if (capture != null) cooldownTracker.Observe(LiveSnapshot(), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Environment.TickCount64);
        var f = Current;
        bool changed = !ReferenceEquals(f, displayedSnapshot);
        if (changed) { displayedTotals = Analysis.Totals(f); displayedPvp = PvpAnalysis.Build(f); displayedSnapshot = f; }
        var totals = displayedTotals;
        if (!skipUnchanged || changed || archiveChanged || (tabs.SelectedItem as TabItem)?.Header?.ToString() == "Cooldowns" || (detailsPending && Environment.TickCount64 >= nextComparisonRefresh))
        {
        var meterData = MeterPresentation.Build(f, totals, limit: 50);
        summary.Text = f.Label; summary.TextTrimming = TextTrimming.CharacterEllipsis;
        summary.ToolTip = (f.Demo ? "Demo · " : viewed != null ? "Saved fight · " : "Live fight · ") + f.Label;
        damageValue.Text = MeterPresentation.Number(meterData.Amount); damageValue.ToolTip = $"{meterData.Amount:N0} damage · {meterData.Scope}";
        dpsValue.Text = MeterPresentation.Number(meterData.Rate); dpsValue.ToolTip = $"{meterData.Rate:N1} DPS · {meterData.Scope}";
        durationValue.Text = MeterPresentation.Duration(f);
        string tab = (tabs.SelectedItem as TabItem)?.Header?.ToString() ?? "Party";
        bool bossSearch = tab is "Boss analysis" or "Boss catalogue";
        character.Tag = bossSearch ? "Search bosses, IDs, instances..." : tab == "Encounter" ? "Search encounters or participants..." : tab == "Saved fights" ? "Search fights, bosses, players..." : "Filter characters...";
        character.ToolTip = bossSearch ? "Search boss names, NPC template IDs or possible instances" : "Filter details by character name; blank shows everyone";
        clearFilter.ToolTip = "Clear search";
        bool hasCharacterFilter = tab is not ("Cooldowns" or "Player stats");
        character.Visibility = hasCharacterFilter ? Visibility.Visible : Visibility.Hidden;
        viewTitle.Text = tab; clearFilter.Visibility = hasCharacterFilter && character.Text.Length > 0 ? Visibility.Visible : Visibility.Hidden;
        showLive.Visibility = viewed != null ? Visibility.Visible : Visibility.Hidden;
        bool comparison = tab is "Trends" or "Report card" or "Top cohort" or "Top 5%";
        bool updateDetails = !skipUnchanged || archiveChanged || !comparison || Environment.TickCount64 >= nextComparisonRefresh;
        detailsPending = !updateDetails;
        if (comparison && updateDetails) nextComparisonRefresh = Environment.TickCount64 + 2000;
        if (updateDetails)
        switch (tab)
        {
            case "Encounter": encounterView.Update(f, LiveSnapshot(), archive, capture != null, character.Text); break;
            case "Party":
                Set(tab, totals.Where(r => Matches(r.Character)).ToArray());
                overview.Update(meterData.Rows.Where(r => Matches(r.Name)).ToArray(), compact: true, hasEvents: f.HasEvents, emptyText: character.Text.Length > 0 ? "No matching players" : null);
                partySummary.Text = f.PartyKnown || f.Actors.Values.Any(a => a.Party) ? $"{f.Actors.Values.Count(a => a.Party || a.Id == f.LocalPlayer)} party players" : "Known players · party roster not set";
                partySummary.ToolTip = "Idle and healing-only party members stay visible. Roster: " + f.PartySource; break;
            case "Skills": Set(tab, SkillDamageDisplay.Shares(Analysis.Skills(f, skills.GetSkillName).Where(r => Matches(r.Character)))); break;
            case "PvP":
                var pvp = displayedPvp;
                Set(tab, pvp.Opponents);
                pvpMeter.Update(pvp.HasOpponents ? PvpAnalysis.Meter(f, pvp).Rows : [], compact: true, hasEvents: f.HasEvents,
                    emptyText: f.LocalPlayer == 0 && !f.Actors.Values.Any(a => a.Party) ? "Your character identity is pending\nCapture before entering a zone." : "No PvP damage recorded in this fight");
                pvpSummary.Text = pvp.HasOpponents ? $"YOU  {pvp.YourDamage:N0} damage · {pvp.YourDamage / pvp.Seconds:N1} DPS     ENEMY  {pvp.EnemyDamage:N0} damage     {MeterPresentation.Duration(pvp.Seconds)} · {pvp.Opponents.Length} opponent(s) · enemies in red" : "Rows rank by damage. Enable pinning in settings to keep your row first. Names update when captured.";
                damageValue.Text = MeterPresentation.Number(pvp.YourDamage); damageValue.ToolTip = "Your damage to PvP opponents";
                dpsValue.Text = MeterPresentation.Number(pvp.YourDamage / pvp.Seconds); dpsValue.ToolTip = "Your DPS over the shared PvP window";
                durationValue.Text = MeterPresentation.Duration(pvp.HasOpponents ? pvp.Seconds : 0); break;
            case "Healing": Set(tab, SkillDamageDisplay.Shares(DetailedAnalysis.Healing(f, skills.GetSkillName).Where(r => Matches(r.Healer)))); break;
            case "Rotations":
                DrawTimeline(f, totals); break;
            case "Openers": Set(tab, DetailedAnalysis.Openers(f, skills.GetSkillName).Where(r => Matches(r.Character))); break;
            case "Boss analysis": bossView.Update(f, character.Text, force: !skipUnchanged); break;
            case "Boss catalogue": Set(tab, BossCatalogue.Default.Rows().Where(b => Matches(b.Boss) || Matches(b.PossibleInstances) || Matches(b.NpcIds))); break;
            case "Saved fights": Set(tab, archive.Where(EncounterOverview.HasLocalDamage).Where(a => Matches(a.Label) || Matches(BossAnalysis.Summary(a)) || a.Actors.Values.Any(p => p.Player && Matches(p.Name))).Select(a => new SavedRow(a.Id, a.Created.ToLocalTime(), a.Label, a.Demo, a.Damage.Count + a.Healing.Count, Math.Round(a.Seconds, 1)) { Bosses = BossAnalysis.Summary(a) }).ToArray()); break;
            case "Trends": RefreshTrends(); break;
            case "Report card": Set(tab, Benchmarks.Report(f, archive).Where(r => Matches(r.Character)).ToArray()); break;
            case "Player stats": Set(tab, CharacterReadings.Rows(f.LocalStats)); break;
            case "Cooldowns":
                var tracked = CooldownPresentation.Rows(preferences, skills, cooldownTracker, capture != null, Environment.TickCount64);
                Set(tab, tracked);
                string trackedLabel = $"{tracked.Length} tracked skill{(tracked.Length == 1 ? "" : "s")}";
                cooldownSummary.Text = tracked.Length == 0 ? "Choose the skills you want to track, then open the cooldown overlay."
                    : capture == null ? $"{trackedLabel}. Start capture to follow cooldowns."
                    : $"{trackedLabel}. Live timers stay active while you browse saved encounters.";
                break;
            case "Network events": Set(tab, f.Network.TakeLast(500).Reverse().Select(e => new { e.Timestamp, e.Kind, e.Opcode, Fields = e.Fields.GetRawText() }).ToArray()); break;
            case "Top cohort":
                var focus = ComparisonActor(f, totals); var cohortPeers = focus == null ? [] : Benchmarks.Peers(f, focus, archive);
                cohortSummary.Text = ComparisonStatus(f, focus, 20, cohortPeers.Length);
                Set(tab, focus == null ? [] : Benchmarks.Cohort(f, focus, archive, skills.GetSkillName, cohortPeers));
                SetDetail("Cohort openers", focus == null ? [] : Benchmarks.Openers(f, focus, archive, skills.GetSkillName, cohortPeers)); break;
            case "Top 5%":
                var selected = ComparisonActor(f, totals); var differencePeers = selected == null ? [] : Benchmarks.Peers(f, selected, archive);
                differenceSummary.Text = ComparisonStatus(f, selected, 100, differencePeers.Length);
                Set(tab, selected == null ? [] : Benchmarks.Differences(f, selected, archive, differencePeers).Where(r => r.Tier == "Top 5% (local)")); break;
        }
        archiveChanged = false;
        }
        string energy = f.Energy == null ? "Loot energy: not captured" : $"Loot energy: daily {f.Energy.Daily} · bonus {f.Energy.Bonus}";
        int namedPlayers = f.Actors.Values.Count(a => a.Player);
        sessionState.Text = f.Demo ? "● DEMO" : capture != null ? "● LIVE" : "● READY";
        sessionState.Foreground = UiTheme.Brush(capture != null && !f.Demo ? preferences.Accent : "#93A2B8");
        footer.Text = (f.Demo ? "Demo preview" : viewed != null ? "Saved fight" : capture != null ? "Capturing" : "Ready to capture") + $"   ·   {namedPlayers} named players   ·   " + (f.Demo ? "Illustrative data" : "Saved locally") + (f.Energy == null ? "" : $"   ·   Loot energy {f.Energy.Daily:N0} / {f.Energy.Bonus:N0}");
        string identity = namedPlayers == 0 && f.HasEvents ? "Player names pending: capture while teleporting to refresh identities." : $"Named players: {namedPlayers}";
        status.Text = $"{notice}\n{energy}  |  {identity}" + (skills.Count == 0 ? "  |  Skill names unavailable: use Load skill names." : $"  |  {skills.Count:N0} skill names loaded") + (capture == null ? "" : $"  |  Packets {capture.Received:N0} · {engine.DecoderDetails} · queue drops {capture.Dropped:N0} · capture errors {capture.Errors:N0}" + (capture.LastError == null ? "" : " · " + capture.LastError));
        if (capture != null)
        {
            var timing = capture.Timing;
            status.Text += $"\nQueued {timing.QueuedPackets} (peak {timing.PeakQueuedPackets}) · queue avg {timing.QueueWait.AverageMs:F2} ms · capture-to-processed avg {timing.CaptureToProcessed.AverageMs:F2} ms · display refresh 200 ms";
        }
        if (overlay?.IsVisible != true) return;
        var overlayFight = capture != null ? LiveSnapshot() : f;
        string overlayEnergy = overlayFight.Energy == null ? "Loot energy: not captured" : $"Loot energy: daily {overlayFight.Energy.Daily} · bonus {overlayFight.Energy.Bonus}";
        overlay.Update(overlayFight, preferences, overlayEnergy, ReferenceEquals(overlayFight, f) ? totals : null,
            ReferenceEquals(overlayFight, f) ? displayedPvp : null);
    }
    void DrawTimeline(Fight f, TotalRow[] totals)
    {
        var actors = totals.Where(r => Matches(r.Character) && FightMetadata.InParty(f, r.Id)).ToArray();
        if (actors.Length == 0) actors = totals.Where(r => Matches(r.Character) && f.Actors.GetValueOrDefault(r.Id)?.Player == true).ToArray();
        if (actors.Length == 0) actors = totals.Where(r => Matches(r.Character)).ToArray();
        var ids = actors.OrderByDescending(r => r.Id == f.LocalPlayer).ThenBy(r => r.Character, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Id).Take(10).Select(r => r.Id).ToArray();
        rotationView.Update(f, ids, (viewed == null ? "live" : "saved:" + f.Id) + "|" + character.Text);
    }
    sealed record SavedRow(string Id, DateTime Date, string Encounter, bool Demo, int Events, double Seconds) { public string Bosses { get; init; } = ""; }
    internal void RunVisualSmokeTest()
    {
        Directory.CreateDirectory("test-artifacts");
        CheckStartupUi();
        CheckAdapterUi();
        preferences.CooldownSkills = [17100000, 17100120];
        if (viewed?.Demo == true) {
            viewed.LocalStats = new(1000, new() { [19] = 1234, [425] = 1250, [65000] = -20 });
            viewed.Network.Add(new("skill-cooldown", 1000, "47 38", System.Text.Json.JsonSerializer.SerializeToElement(new { SkillCode = 17100000, RemainingMs = 3500 })));
        }
        var setupDialog = NpcapSetup.CreateDialog(this);
        try { setupDialog.Show(); setupDialog.UpdateLayout(); Render(setupDialog, "test-artifacts/Npcap-setup.png"); }
        finally { setupDialog.Close(); }
        foreach (TabItem tab in tabs.Items)
        {
            tabs.SelectedItem = tab; UpdateLayout();
            if (tab.Header.ToString() == "Skills") LiveTableTests.Check((LiveTable)grids["Skills"], UpdateLayout);
            if (tab.Header.ToString() == "Rotations")
            {
                var items = grids["Rotations"].ItemsSource; int builds = rotationView.ModelBuilds, scenes = rotationView.Plot.SceneBuilds;
                Refresh(skipUnchanged: true);
                if (!ReferenceEquals(items, grids["Rotations"].ItemsSource) || builds != rotationView.ModelBuilds || scenes != rotationView.Plot.SceneBuilds) throw new Exception("Idle refresh rebuilt the timeline.");
            }
            if (tab.Header.ToString() == "Encounter") { encounterView.CheckPresentation(Current); UpdateLayout(); Render(this, "test-artifacts/Encounter.png"); }
            if (tab.Header.ToString() is "Skills" or "Healing")
            {
                var grid = grids[tab.Header.ToString()!];
                if (grid.Columns.SingleOrDefault(c => c.Header?.ToString() == "Skill") is not DataGridTemplateColumn skillColumn)
                    throw new Exception("Skill icon column was not generated.");
                var content = skillColumn.GetCellContent(grid.Items[0]);
                if (content == null || !VisualImages(content).Any(i => i.Source != null)) throw new Exception("Skill cell did not render its icon or fallback.");
            }
            if (tab.Header.ToString() == "Player stats" && grids["Player stats"].Items.Count != 3) throw new Exception("Player stats view did not populate.");
            if (tab.Header.ToString() == "Cooldowns" && (grids["Cooldowns"].Items.Count != 1 || grids["Cooldowns"].Items.OfType<TrackedCooldownRow>().Single().Status != "Start capture"))
                throw new Exception("Cooldown view did not group configured skills or incorrectly used saved-fight timers.");
            if (tab.Header.ToString() is "Party" or "Rotations" or "Skills" or "Healing" or "PvP" or "Player stats" or "Cooldowns") Render(this, System.IO.Path.Combine("test-artifacts", tab.Header + ".png"));
        }
        if (grids["Party"].Items.Count != 13 || grids["PvP"].Items.Count != 3) throw new Exception("Demo grids failed to populate.");
        string oldTheme = preferences.Theme;
        foreach (string theme in themes.Items) { preferences.Theme = theme; ApplyTheme(); UpdateLayout(); }
        CheckSettingsUi(); ShowSettings("Appearance"); settingsWindow.UpdateLayout();
        foreach (string theme in themes.Items)
        {
            preferences.Theme = theme; ApplyTheme(); settingsWindow.UpdateLayout(); UpdateLayout();
            themes.IsDropDownOpen = true; Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { })); themes.UpdateLayout();
            var popup = (Popup)themes.Template.FindName("PART_Popup", themes);
            if (popup.Child is not Border surface || surface.Background is not SolidColorBrush background || background.Color.R > 100)
                throw new Exception("Theme dropdown lost its dark popup surface.");
            surface.UpdateLayout();
            // Native focus changes can dismiss a popup during automation. Its
            // theme is still testable: explicitly lay out the detached surface.
            if (surface.ActualWidth <= 0 || surface.ActualHeight <= 0) {
                surface.Measure(new Size(themes.ActualWidth, themes.MaxDropDownHeight));
                surface.Arrange(new Rect(surface.DesiredSize));
            }
            Render(surface, $"test-artifacts/Dropdown-{theme}.png"); themes.IsDropDownOpen = false;
            if (backAttackColor.Template.FindName("PART_EditableTextBox", backAttackColor) is not TextBox colourInput ||
                !colourInput.IsVisible || colourInput.Text != backAttackColor.Text || string.IsNullOrWhiteSpace(colourInput.Text))
                throw new Exception("Editable back-attack colour is missing or blank.");
            CheckMenu(CreateFilesMenu(), $"Files-{theme}");
            CheckMenu(CreateMoreMenu(), $"More-{theme}");
        }
        settingsWindow.Hide();
        preferences.Theme = oldTheme; ApplyTheme(); tabs.SelectedIndex = 0;
        if (overview.RowCount != 10) throw new Exception("Overview did not display the ten party members.");
        // Keep automated layout checks independent of a user's saved display preferences.
        var cooldownDemo = new Fight { LocalPlayer = 1 }; long cooldownNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        cooldownDemo.Network.Add(new("skill-cooldown", cooldownNow, "47 38", JsonSerializer.SerializeToElement(new { SkillCode = 17100000, RemainingMs = 30000 })));
        cooldownDemo.Network.Add(new("skill-cooldown", cooldownNow, "47 38", JsonSerializer.SerializeToElement(new { SkillCode = 17010000, RemainingMs = 0 })));
        cooldownDemo.Network.Add(new("skill-use", cooldownNow, "02 38", JsonSerializer.SerializeToElement(new { ActorId = 1, SkillCode = 17150230, Sequence = 1 })));
        cooldownDemo.Network.Add(new("skill-use", cooldownNow - 2000, "02 38", JsonSerializer.SerializeToElement(new { ActorId = 1, SkillCode = 17060343, Sequence = 2 })));
        long cooldownTick = Environment.TickCount64;
        var cooldownTestTracker = new CooldownTracker(); cooldownTestTracker.Observe(cooldownDemo, cooldownNow, cooldownTick);
        var cooldownTestSettings = new Preferences { CooldownSkills = [17100000, 17010000, 17020000, 17150000, 17060343, 17060001, 17060000] };
        var cooldownTestOverlay = new CooldownOverlayWindow(cooldownTestSettings, cooldownTestTracker, skills, skillIcons, () => true, () => { }, () => { });
        try {
            cooldownTestOverlay.Show(); cooldownTestOverlay.UpdateLayout();
            cooldownTestOverlay.CheckDragging();
            if (!int.TryParse(cooldownTestOverlay.DisplayedTime(17100000), out _) || cooldownTestOverlay.DisplayedTime(17010000) != "" || cooldownTestOverlay.DisplayedTime(17020000) != "?" || !cooldownTestOverlay.Dimmed(17100000) || cooldownTestOverlay.Dimmed(17010000))
                throw new Exception("Cooldown overlay failed to distinguish active, ready and unknown skills.");
            if (!cooldownTestOverlay.DisplayedTime(17150000).StartsWith("~") || !cooldownTestOverlay.Dimmed(17150000)) throw new Exception("Cast estimate was not shown as a gray icon with estimated seconds.");
            if (cooldownTestOverlay.VisibleSkills != 5 || !cooldownTestOverlay.DisplayedTime(17060000).StartsWith("~") || !cooldownTestOverlay.Dimmed(17060000))
                throw new Exception("Old charge tier selections did not merge into one active base icon.");
            cooldownTestOverlay.RefreshAt(cooldownTick + 29250);
            if (cooldownTestOverlay.DisplayedTime(17100000) != "" || cooldownTestOverlay.Dimmed(17100000) ||
                cooldownTestOverlay.DisplayedTime(17150000) != "" || cooldownTestOverlay.Dimmed(17150000) || cooldownTestOverlay.DisplayedTime(17020000) != "?")
                throw new Exception("Early cooldown display did not make both known timers ready while preserving unknown skills.");
            cooldownTestSettings.CooldownShowReady = false; cooldownTestOverlay.RefreshAt(cooldownTick + 29250);
            if (cooldownTestOverlay.VisibleSkills != 2) throw new Exception("Hide-ready did not follow the adjusted cooldown display.");
            cooldownTestSettings.CooldownOneSecondEarly = false; cooldownTestOverlay.RefreshAt(cooldownTick + 29250);
            if (cooldownTestOverlay.DisplayedTime(17100000) != "1" || cooldownTestOverlay.DisplayedTime(17150000) != "~1" || cooldownTestOverlay.VisibleSkills != 4)
                throw new Exception("Disabling the early display did not restore the original active timers.");
            cooldownTestSettings.CooldownOneSecondEarly = true; cooldownTestSettings.CooldownShowReady = true;
            cooldownTestOverlay.RefreshAt(cooldownTick); cooldownTestOverlay.RefreshAt(cooldownTick);
            if (cooldownTestOverlay.DisplayedTime(17100000) != "29" || cooldownTestOverlay.DisplayedTime(17150000) != "~29" ||
                cooldownTestOverlay.DisplayedTime(17060000) != "~42" || cooldownTestTracker.Remaining(17100000, cooldownTick, skills) != 30)
                throw new Exception("Display adjustment accumulated, changed raw readings or lost packet-age correction.");
            Render(cooldownTestOverlay, "test-artifacts/Cooldown-overlay.png");
            CheckMenu(cooldownTestOverlay.ContextMenu, "Cooldown-menu");
            cooldownTestOverlay.Hide();
            long hiddenNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            cooldownDemo.Network.Add(new("skill-cooldown", hiddenNow, "47 38", JsonSerializer.SerializeToElement(new { SkillCode = 17100000, RemainingMs = 3500 })));
            cooldownTestTracker.Observe(cooldownDemo, hiddenNow, Environment.TickCount64);
            cooldownTestOverlay.Show();
            if (!int.TryParse(cooldownTestOverlay.DisplayedTime(17100000), out int resumedSeconds) || resumedSeconds is < 1 or > 3)
                throw new Exception("Reopened cooldown overlay did not pick up events received while hidden.");
            cooldownTestSettings.CooldownShowReady = false; cooldownTestSettings.CooldownLocked = true; cooldownTestOverlay.Refresh();
            if (cooldownTestOverlay.VisibleSkills != 4 || cooldownTestOverlay.ResizeMode != ResizeMode.NoResize) throw new Exception("Cooldown overlay settings did not apply.");
        } finally { cooldownTestOverlay.Close(); }
        var cooldownEditor = new CooldownSettingsWindow(cooldownTestSettings, skills, cooldownTestTracker.ObservedSkills, () => { }, () => { });
        try { cooldownEditor.Show(); cooldownEditor.UpdateLayout(); Render(cooldownEditor, "test-artifacts/Cooldown-settings.png"); } finally { cooldownEditor.Close(); }
        var testOverlay = new OverlayWindow(); var testPreferences = new Preferences { OverlayScope = "Party", OverlayPinLocal = true };
        testOverlay.Update(Current, testPreferences); testOverlay.Show(); testOverlay.UpdateLayout();
        testOverlay.CheckModes(); testOverlay.UpdateLayout(); Render(testOverlay, "test-artifacts/Overlay.png");
        CheckMenu(testOverlay.CreateOptions(), "Overlay-menu");
        testOverlay.Width = 310; testOverlay.Height = 300; testOverlay.UpdateLayout(); testOverlay.CheckPinnedScrolling(); Render(testOverlay, "test-artifacts/Overlay-small.png");
        testPreferences.OverlayHealing = true; testPreferences.OverlayLocked = true; testOverlay.Update(Current, testPreferences); testOverlay.UpdateLayout();
        if (testOverlay.ResizeMode != ResizeMode.NoResize || testOverlay.RowCount != 10) throw new Exception("Locked healing overlay failed.");
        Render(testOverlay, "test-artifacts/Overlay-healing.png"); testOverlay.Close();
        var pvpOverlay = new OverlayWindow(); pvpOverlay.Update(Current, new Preferences { OverlayPinLocal = true }); pvpOverlay.Show(); pvpOverlay.UpdateLayout();
        if (pvpOverlay.CurrentScope != "PvP" || pvpOverlay.RowCount != displayedPvp.Combatants.Length) throw new Exception("Automatic PvP overlay did not retain both sides.");
        pvpOverlay.CheckPinnedScrolling(); Render(pvpOverlay, "test-artifacts/Overlay-pvp.png"); pvpOverlay.Close();
        var duel = PvpTests.Duel(); var duelOverlay = new OverlayWindow();
        duelOverlay.Update(duel, new Preferences { OverlayPinLocal = true }); duelOverlay.Show(); duelOverlay.UpdateLayout();
        if (duelOverlay.CurrentScope != "PvP" || duelOverlay.RowCount != 2) throw new Exception("Unnamed PvP opponent is hidden in the overlay.");
        duelOverlay.CheckPinnedScrolling(); Render(duelOverlay, "test-artifacts/Overlay-pvp-unnamed.png");
        var backDemo = duel.Copy();
        backDemo.Damage.Add(new() { ActorId = backDemo.LocalPlayer, TargetId = 2, SkillCode = 11000001, Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Damage = 10, IsBackAttack = true });
        duelOverlay.Update(backDemo, new Preferences()); duelOverlay.CheckBackAttackBorder(true); duelOverlay.UpdateLayout();
        Render(duelOverlay, "test-artifacts/Overlay-back-attack.png");
        var frontDemo = backDemo.Copy();
        frontDemo.Damage.Add(new() { ActorId = frontDemo.LocalPlayer, TargetId = 2, SkillCode = 11000001, Timestamp = frontDemo.End + 1, Damage = 10, IsFrontAttack = true });
        duelOverlay.Update(frontDemo, new Preferences { BackAttackColor = "#7C9EF7" }); duelOverlay.CheckBackAttackBorder(true);
        duelOverlay.Update(new Fight { LocalPlayer = 1, Actors = new(duel.Actors) }, new Preferences());
        duelOverlay.CheckBackAttackBorder(false);
        if (duelOverlay.CurrentScope == "PvP") throw new Exception("Automatic PvP scope did not reset for the next fight.");
        duelOverlay.Close();
        // Exercise hide/show and close while swapping a large and small fight.
        var beforeStress = viewed;
        var beforeStressTab = tabs.SelectedItem;
        tabs.SelectedIndex = 0;
        var longFight = PvpTests.Duel();
        for (int id = 100; id < 10100; id++) longFight.Actors[id] = new(id, $"Entity {id}", false, false, 0, "Unknown", 0);
        var stressHit = longFight.Damage[0];
        for (int i = 0; i < 60000; i++) longFight.Damage.Add(stressHit with { Timestamp = stressHit.Timestamp + i * 10, SkillCode = 11000000 + i % 400 });
        bool beforeCustom = preferences.OverlayCustomBars;
        try {
            preferences.OverlayCustomBars = true;
            for (int cycle = 0; cycle < 3; cycle++) {
                viewed = longFight; Refresh(); ToggleOverlay(); UpdateLayout();
                if (overlay?.IsVisible != true) throw new Exception("Rapid overlay opening failed.");
                viewed = duel; Refresh(); overlay.UpdateLayout();
                if (overlay.RowCount != 2) throw new Exception("Overlay retained rows after switching fights.");
                ToggleOverlay();
                if (overlay?.IsVisible != false) throw new Exception("Rapid overlay hiding failed.");
                overlay.Close();
                if (overlay != null) throw new Exception("Closing the overlay retained the window.");
            }
        } finally {
            overlay?.Close(); viewed = beforeStress; preferences.OverlayCustomBars = beforeCustom; tabs.SelectedItem = beforeStressTab; Refresh();
        }

        var previousView = viewed; viewed = duel;
        tabs.SelectedItem = tabs.Items.Cast<TabItem>().First(t => t.Header?.ToString() == "PvP"); Refresh(); UpdateLayout();
        if (grids["PvP"].Items.Count != 1 || pvpMeter.RowCount != 2) throw new Exception("Unnamed opponent is hidden in the PvP tab.");
        Render(this, "test-artifacts/PvP-duel.png"); viewed = previousView; tabs.SelectedIndex = 0; Refresh();
        var beforeBossView = viewed; var beforeBossFilter = character.Text;
        var bossDemo = BossEncounterTests.Fixture();
        viewed = bossDemo; character.Text = "";
        tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(t => t.Header?.ToString() == "Boss analysis"); Refresh(); UpdateLayout();
        bossView.CheckPresentation(bossDemo); UpdateLayout();
        Render(this, "test-artifacts/Boss-analysis.png");
        Render(bossView, "test-artifacts/Boss-analysis-full.png");
        character.Text = "2310371";
        tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(t => t.Header?.ToString() == "Boss catalogue"); Refresh(); UpdateLayout();
        if (grids["Boss catalogue"].Items.Count != 1) throw new Exception("Boss catalogue variant-ID search failed.");
        Render(this, "test-artifacts/Boss-catalogue.png");
        viewed = beforeBossView; character.Text = beforeBossFilter; tabs.SelectedIndex = 0; Refresh();
        CheckFeatureViews();
        CheckResponsiveViews();
        Width = MinWidth; Height = MinHeight; UpdateLayout(); Render(this, "test-artifacts/Main-small.png");
        File.WriteAllText("smoke-test-results.txt", $"PASS: all {tabs.Items.Count} WPF tabs, immediate rotation inspection, hover hold, pinned damage/healing stepping, scrubbing/resume and retained drawing, populated CP report/top-five/cohort/trends/specs/openers, character editor, delayed comparison refresh, unnamed PvP opponents, automatic PvP overlay, skill/healing icon cells, ten ranked bars, five themes, custom bars, dark dropdowns/file menus/overlay menu, damage/healing overlay, move lock and minimum sizes rendered on .NET 10.\n" + notice);
    }
    void CheckFeatureViews()
    {
        var previousView = viewed; var savedArchive = archive.ToArray(); string previousFilter = character.Text;
        try
        {
            viewed = FeatureTests.Sample(); archive.Clear(); archive.AddRange(Enumerable.Range(1, 100).Select(FeatureTests.Sample));
            archiveChanged = true; character.Clear();
            void Show(string name) { tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(t => t.Header?.ToString() == name); Refresh(); UpdateLayout(); Render(this, "test-artifacts/Populated-" + name + ".png"); }
            Show("Report card");
            if (grids["Report card"].Items.OfType<ReportRow>().Single(r => r.Character == "Example cleric").LocalSamples != 100)
                throw new InvalidOperationException("CP report did not populate from the local archive.");
            Show("Top 5%");
            if (grids["Top 5%"].Items.Count != 6) throw new InvalidOperationException("Top-five-percent metrics are missing.");
            Show("Top cohort");
            if (grids["Top cohort"].Items.Count != 4 || detailGrids.Values.Any(g => g.ItemsSource == null))
                throw new InvalidOperationException("Cohort details are missing.");
            foreach (string detail in new[] { "Cohort openers" })
                if (detailGrids[detail].Items.Count == 0) throw new InvalidOperationException("Empty " + detail);
            Show("Trends");
            if (grids["Trends"].Items.OfType<EncounterTrendRow>().Single().Attempts != 100 || !trendChart.Children.OfType<Polyline>().Any(l => l.Points.Count == 100))
                throw new InvalidOperationException("Trend chart did not group the chosen character's observations by encounter.");
            trendMetric.SelectedIndex = 1; Refresh(); UpdateLayout(); Render(this, "test-artifacts/Populated-Trends-HPS.png");
            CheckTrendViews();
            Show("Openers");
            if (grids["Openers"].Items.Count != 4) throw new InvalidOperationException("Damage/healing opener rows are missing.");
            Show("Rotations"); RotationTests.CheckView(rotationView, viewed, name => { UpdateLayout(); Render(this, "test-artifacts/" + name + ".png"); });
            var editor = new CharacterEditor(viewed, preferences) { Owner = this };
            try { editor.Show(); editor.UpdateLayout(); editor.CheckViews(Render); } finally { editor.Close(); }
            Show("Report card"); nextComparisonRefresh = long.MaxValue;
            viewed = viewed.Copy(); viewed.Actors[1] = viewed.Actors[1] with { Cp = 25000 };
            Refresh(skipUnchanged: true);
            if (!detailsPending) throw new InvalidOperationException("Comparison update was not deferred.");
            nextComparisonRefresh = 0; Refresh(skipUnchanged: true);
            if (detailsPending || grids["Report card"].Items.OfType<ReportRow>().Single(r => r.Character == "Example cleric").LocalSamples != 0)
                throw new InvalidOperationException("A deferred comparison remained stale after activity stopped.");
            var customOverlay = new OverlayWindow();
            try { customOverlay.Update(viewed, new Preferences { OverlayScope = "Party", OverlayCustomBars = true, Accent = "#D695F2" }); customOverlay.Show(); customOverlay.UpdateLayout(); Render(customOverlay, "test-artifacts/Overlay-custom.png"); }
            finally { customOverlay.Close(); }
        }
        finally { viewed = previousView; archive.Clear(); archive.AddRange(savedArchive); archiveChanged = true; character.Text = previousFilter; trendMetric.SelectedIndex = 0; tabs.SelectedIndex = 0; Refresh(); }
    }
    void CheckStartupUi()
    {
        if (capture != null || overlay != null || cooldownOverlay != null) throw new Exception("Demo startup restored a live session.");
        tabs.SelectedIndex = 0;
        if (!tabs.Items.Cast<TabItem>().Where(t => t.Visibility == Visibility.Visible).Select(t => t.Header?.ToString()).SequenceEqual(MainViews) ||
            grids.ContainsKey("Defense") || grids.ContainsKey("Specs")) throw new Exception("Navigation retained unused views or lost the main view order.");
        var more = CreateMoreMenu();
        more.Items.OfType<MenuItem>().Single(i => i.Header?.ToString() == "Network events").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        if ((tabs.SelectedItem as TabItem) is not { Visibility: Visibility.Visible } advanced || advanced.Header?.ToString() != "Network events")
            throw new Exception("More menu did not open the selected advanced view.");
        var previous = preferences;
        try {
            double x = SystemParameters.VirtualScreenLeft + 20, y = SystemParameters.VirtualScreenTop + 20;
            preferences = new() { Adapter = "fixture-adapter", CooldownSkills = [17060000, 17060343],
                OverlayLeft = x, OverlayTop = y, OverlayWidth = 340, OverlayHeight = 220,
                CooldownLeft = x + 30, CooldownTop = y + 30, CooldownWidth = 180, CooldownHeight = 100 };
            RestoreStartupOverlays(StartupPlan.Create(preferences, ["fixture-adapter"], false));
            var meter = overlay!; var cooldown = cooldownOverlay!;
            if (capture != null || meter == null || cooldown == null || meter.Left != x || meter.Top != y || meter.Width != 340 ||
                cooldown.Left != x + 30 || cooldown.Top != y + 30 || cooldown.Width != 180) throw new Exception("Startup overlays lost saved positions or sizes.");
            OpenOverlay(); OpenCooldownOverlay();
            if (!ReferenceEquals(overlay, meter) || !ReferenceEquals(cooldownOverlay, cooldown)) throw new Exception("Startup restoration duplicated or closed an overlay.");
            meter.Left += 12; meter.Top += 10; meter.Width += 20;
            cooldown.Left += 18; cooldown.Top += 16; cooldown.Width += 10;
            // Let the same debounced placement handlers used for dragging run.
            var frame = new DispatcherFrame(); var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
            delay.Tick += (_, _) => { delay.Stop(); frame.Continue = false; }; delay.Start(); Dispatcher.PushFrame(frame);
            if (preferences.OverlayLeft != meter.Left || preferences.OverlayTop != meter.Top || preferences.OverlayWidth != meter.Width ||
                preferences.CooldownLeft != cooldown.Left || preferences.CooldownTop != cooldown.Top || preferences.CooldownWidth != cooldown.Width)
                throw new Exception("Moved overlays were not saved while still open.");
            double meterX = meter.Left, meterY = meter.Top, cooldownX = cooldown.Left, cooldownY = cooldown.Top;
            for (int i = 0; i < 3; i++) {
                meterButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); cooldownButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                if (meter.IsVisible || cooldown.IsVisible || System.Windows.Automation.AutomationProperties.GetHelpText(meterButton) != "Hidden" ||
                    System.Windows.Automation.AutomationProperties.GetHelpText(cooldownButton) != "Hidden") throw new Exception("Overlay buttons did not hide their windows or update their state.");
                meterButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); cooldownButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                if (!meter.IsVisible || !cooldown.IsVisible || !ReferenceEquals(meter, overlay) || !ReferenceEquals(cooldown, cooldownOverlay) ||
                    meter.Left != meterX || meter.Top != meterY || cooldown.Left != cooldownX || cooldown.Top != cooldownY ||
                    System.Windows.Automation.AutomationProperties.GetHelpText(meterButton) != "Visible" ||
                    System.Windows.Automation.AutomationProperties.GetHelpText(cooldownButton) != "Visible")
                    throw new Exception("Overlay toggles lost visibility, state or saved placement.");
            }
            meter.Close(); cooldown.Close(); OpenOverlay(); OpenCooldownOverlay();
            if (overlay!.Left != meterX || overlay.Top != meterY || cooldownOverlay!.Left != cooldownX || cooldownOverlay.Top != cooldownY)
                throw new Exception("Manually reopened overlays did not restore their last placement.");
            tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(t => t.Header?.ToString() == "Cooldowns"); UpdateLayout();
            if (advanced.Visibility != Visibility.Collapsed || grids["Cooldowns"].Items.OfType<TrackedCooldownRow>().Single().Skill != "Bolt")
                throw new Exception("Cooldown page did not show the configured skill or navigation failed to collapse the advanced view.");
            Render(this, "test-artifacts/Cooldowns-tracked.png");
        } finally {
            cooldownOverlay?.Close(); overlay?.Close(); preferences = previous; SavePreferences(); tabs.SelectedIndex = 0; Refresh();
        }
    }
    void CheckSettingsUi()
    {
        foreach (var button in new[] { start, newFightButton, meterButton, cooldownButton, moreButton, settingsButton })
            if (button.Content is not StackPanel label || label.Children.OfType<Viewbox>().SingleOrDefault()?.Tag is not UiIcon ||
                string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(button)))
                throw new Exception("A toolbar action lost its icon or accessible label.");
        settingsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (!settingsWindow.IsVisible) throw new Exception("Settings button did not open settings.");
        foreach (var page in new[] { "Startup", "Capture", "Overlays", "Appearance", "Local data", "Advanced" }) {
            settingsWindow.SelectPage(page); settingsWindow.UpdateLayout();
            if (settingsWindow.SelectedPage != page) throw new Exception("Settings navigation did not select a page.");
            // Capture and diagnostics can contain adapter information: never use them as sample images.
            if (page is "Startup" or "Overlays" or "Appearance") Render(settingsWindow, "test-artifacts/Settings-" + page + ".png");
        }
        settingsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (settingsWindow.IsVisible) throw new Exception("Settings button did not hide settings.");
        ShowSettings("Startup"); settingsWindow.Close();
        if (settingsWindow.IsVisible) throw new Exception("Closing settings did not hide the reusable window.");
        ShowSettings("Appearance");
        double width = settingsWindow.Width, height = settingsWindow.Height;
        settingsWindow.Width = settingsWindow.MinWidth; settingsWindow.Height = settingsWindow.MinHeight; settingsWindow.UpdateLayout();
        Render(settingsWindow, "test-artifacts/Settings-compact.png");
        settingsWindow.Width = width; settingsWindow.Height = height; settingsWindow.Hide();
        double mainWidth = Width; Width = MinWidth; UpdateLayout();
        foreach (var button in new[] { start, newFightButton, meterButton, cooldownButton, moreButton, settingsButton }) {
            var point = button.TranslatePoint(new(0, 0), this);
            if (point.X < 0 || point.X + button.ActualWidth > ActualWidth) throw new Exception("A toolbar action is clipped at minimum window width.");
        }
        Render(this, "test-artifacts/Toolbar-compact.png"); Width = mainWidth; UpdateLayout();
    }
    static IEnumerable<Image> VisualImages(DependencyObject parent)
    {
        if (parent is Image image) yield return image;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in VisualImages(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    void CheckMenu(ContextMenu menu, string name)
    {
        menu.IsOpen = true; Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { })); menu.UpdateLayout();
        var first = menu.Items.OfType<MenuItem>().First(); first.Focus(); menu.UpdateLayout();
        if (menu.Background is not SolidColorBrush background || background.Color.R > 100 || first.Template == null || first.Foreground is not SolidColorBrush text || text.Color.R < 180)
            throw new Exception("Popup menu has unreadable dark-mode colours.");
        Render(menu, $"test-artifacts/{name}.png"); menu.IsOpen = false;
    }
    static void Render(FrameworkElement window, string path)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}
