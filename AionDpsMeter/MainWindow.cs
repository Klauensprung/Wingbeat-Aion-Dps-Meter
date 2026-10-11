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
    public string Theme { get; set; } = "Midnight";
    public string Accent { get; set; } = "#55D6BE";
    public string Filter { get; set; } = "tcp and not port 443 and not port 80 and not port 53 and not port 22";
    public string Adapter { get; set; } = "";
    public bool OverlayHealing { get; set; }
    public bool OverlayAllActors { get; set; }
    public string OverlayScope { get; set; } = "Auto";
    public bool OverlayCustomBars { get; set; }
    public bool OverlayCompact { get; set; } = true;
    public bool OverlayLocked { get; set; }
    public double OverlayWidth { get; set; } = 390;
    public double OverlayHeight { get; set; } = 530;
    public double? OverlayLeft { get; set; }
    public double? OverlayTop { get; set; }
}

public sealed class MainWindow : Window
{
    readonly FightStore store = new(System.IO.Path.Combine(AppContext.BaseDirectory, "local-data", "fights"));
    readonly Dictionary<string, DataGrid> grids = new();
    readonly TextBlock status = new() { Margin = new(10), TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    readonly TextBlock summary = new() { FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 4) };
    readonly TextBlock sessionState = new() { FontSize = 11, Margin = new(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock footer = new() { FontSize = 11, Margin = new(22, 10, 22, 12) };
    readonly TextBlock damageValue = new(), dpsValue = new(), durationValue = new();
    readonly TextBlock viewTitle = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new(178, 0, 0, 0) };
    readonly Expander renamePanel = new() { Header = "Encounter name", Visibility = Visibility.Collapsed, Margin = new(24, 0, 24, 10) };
    readonly Button showLive, clearFilter;
    readonly MeterView overview = new() { Margin = new(8, 8, 8, 12) };
    readonly MeterView pvpMeter = new() { Margin = new(8, 8, 8, 12) };
    readonly TextBlock pvpSummary = new() { Margin = new(8, 6, 8, 10), FontSize = 12, TextWrapping = TextWrapping.Wrap };
    readonly Expander settingsPanel = new() { Header = "Settings & diagnostics", IsExpanded = false, Margin = new(20, 0, 20, 12), Visibility = Visibility.Collapsed };
    readonly ComboBox adapters = new() { Width = 330, DisplayMemberPath = "Description", Margin = new(4) };
    readonly ComboBox themes = new() { Width = 110, Margin = new(4), ItemsSource = new[] { "Midnight", "Slate", "Forest", "Ember", "Violet" } };
    readonly TextBox accent = new() { Width = 90, Margin = new(4) };
    readonly TextBox filter = new() { Width = 455, Margin = new(4) };
    readonly TextBox encounter = new() { Text = "Local fight", Width = 180, Margin = new(4) };
    readonly TextBox character = new() { Width = 150, Margin = new(4), ToolTip = "Filter details by character name; blank shows everyone" };
    readonly TabControl tabs = new() { Margin = new(20, 4, 20, 0), TabStripPlacement = Dock.Left, Padding = new(12, 0, 0, 0), BorderThickness = new(0) };
    readonly RotationView rotationView;
    readonly TrendChart trendChart = new();
    readonly ComboBox trendCharacter = new() { MinWidth = 250, Margin = new(8), DisplayMemberPath = "Label" };
    readonly ComboBox trendMetric = new() { Width = 110, Margin = new(8), ItemsSource = new[] { "DPS", "HPS" }, SelectedIndex = 0 };
    readonly CheckBox customBars = new() { Content = "Accent-coloured overlay bars", Margin = new(8), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock trendSummary = new() { Margin = new(8), TextWrapping = TextWrapping.Wrap };
    readonly TextBlock cohortSummary = new() { Margin = new(8), TextWrapping = TextWrapping.Wrap };
    readonly TextBlock differenceSummary = new() { Margin = new(8), TextWrapping = TextWrapping.Wrap };
    readonly TextBlock partySummary = new() { Margin = new(8), TextWrapping = TextWrapping.Wrap };
    readonly Dictionary<string, DataGrid> detailGrids = new();
    bool updatingTrends;
    long nextComparisonRefresh;
    bool detailsPending;
    readonly List<Fight> archive = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    readonly Button start;
    readonly CheckBox recordSample = new() { Content = "Save PCAP sample", Margin = new(8), VerticalAlignment = VerticalAlignment.Center };
    readonly CheckBox decodeEu = new() { Content = "Experimental EU decoding", IsChecked = true, Margin = new(8), VerticalAlignment = VerticalAlignment.Center };
    CombatEngine engine;
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
    Preferences preferences = new();
    Task<Fight[]>? autosave;
    long nextMaintenance, nextAutosave = Environment.TickCount64 + 30000;
    bool shuttingDown;
    bool npcapMissing;
    string notice = "Select an adapter, then Start. No account or internet service is used.";
    string PreferencePath => System.IO.Path.Combine(AppContext.BaseDirectory, "local-data", "preferences.json");
    Fight LiveSnapshot()
    {
        if (!ReferenceEquals(snapshotOwner, engine)) { liveSnapshot = null; snapshotOwner = engine; }
        return liveSnapshot = engine.SnapshotIfChanged(liveSnapshot, ref snapshotRevision);
    }
    Fight Current => viewed ?? LiveSnapshot();
    public MainWindow(bool demo)
    {
        Title = "Wingbeat \u2665 Aion 2 DPS Meter"; Width = 1150; Height = 870; MinWidth = 980; MinHeight = 650;
        FontFamily = new("Segoe UI"); FontSize = 13; UseLayoutRounding = true; SnapsToDevicePixels = true;
        engine = new();
        LocalLog.Write($"Application started; skill catalog entries {skills.Count}; " + (skills.LoadError ?? "catalog loaded"));
        LocalLog.Write($"Display metadata: {skillIcons.Count} icon families, {NpcNames.Default.Count} NPC names; " +
            (skillIcons.LoadError ?? "icons loaded") + "; " + (NpcNames.Default.LoadError ?? "NPC names loaded"));
        try { if (File.Exists(PreferencePath)) preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(PreferencePath)) ?? new(); } catch { }
        filter.Text = preferences.Filter; accent.Text = preferences.Accent; themes.SelectedItem = preferences.Theme; customBars.IsChecked = preferences.OverlayCustomBars;
        character.Tag = "Filter characters…";
        var root = new DockPanel(); Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var top = new DockPanel { Margin = new(22, 16, 18, 12), LastChildFill = true }; header.Children.Add(top);
        var controls = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(controls, Dock.Right); top.Children.Add(controls);
        start = Button("Start capture", () => { if (capture == null) Start(); else StopAndSave(); }); controls.Children.Add(start);
        start.SetResourceReference(Control.BackgroundProperty, "Accent"); start.Foreground = UiTheme.Brush("#10201C"); start.FontWeight = FontWeights.SemiBold;
        controls.Children.Add(Button("New fight", NewFight));
        var overlayButton = Button("Open overlay", ToggleOverlay); overlayButton.SetResourceReference(Control.BorderBrushProperty, "Accent"); controls.Children.Add(overlayButton);
        controls.Children.Add(Button("Files", ShowFiles));
        controls.Children.Add(Button("Settings", () => { settingsPanel.Visibility = settingsPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; settingsPanel.IsExpanded = settingsPanel.Visibility == Visibility.Visible; }));
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var mark = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 10, 0) };
        foreach (int height in new[] { 10, 19, 27 }) { var bar = new Border { Width = 4, Height = height, CornerRadius = new(2), Margin = new(0, 0, 3, 0), VerticalAlignment = VerticalAlignment.Bottom }; bar.SetResourceReference(Border.BackgroundProperty, "Accent"); mark.Children.Add(bar); }
        brand.Children.Add(mark); brand.Children.Add(new TextBlock { Text = "Wingbeat", FontSize = 21, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }); brand.Children.Add(sessionState); top.Children.Add(brand);
        var stats = new Grid { Margin = new(22, 0, 22, 10) };
        stats.ColumnDefinitions.Add(new() { Width = new GridLength(1.4, GridUnitType.Star) });
        for (int i = 0; i < 3; i++) stats.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var current = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 15, 0) };
        current.Children.Add(new TextBlock { Text = "CURRENT FIGHT", FontSize = 10, Foreground = UiTheme.Brush("#93A2B8"), Margin = new(0, 0, 0, 5) }); current.Children.Add(summary);
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
        var settings = new StackPanel();
        var captureSettings = new WrapPanel { Margin = new(4) }; captureSettings.Children.Add(Label("Adapter")); captureSettings.Children.Add(adapters); captureSettings.Children.Add(Button("Refresh adapters", RefreshAdapters)); captureSettings.Children.Add(Button("Install Npcap", () => NpcapSetup.CreateDialog(this).ShowDialog())); settings.Children.Add(SettingsSection("Capture", captureSettings));
        var appearance = new WrapPanel { Margin = new(4) }; appearance.Children.Add(Label("Theme")); appearance.Children.Add(themes); appearance.Children.Add(Label("Accent")); appearance.Children.Add(accent); appearance.Children.Add(Button("Apply", ApplyPreferences)); settings.Children.Add(SettingsSection("Appearance", appearance));
        appearance.Children.Add(customBars);
        var localTools = new WrapPanel { Margin = new(4) }; localTools.Children.Add(Button("Load skill names", ImportSkillNames)); localTools.Children.Add(Button("Preview demo", LoadDemo)); settings.Children.Add(SettingsSection("Local data", localTools));
        var advanced = new WrapPanel { Margin = new(4) }; advanced.Children.Add(Label("Capture filter")); advanced.Children.Add(filter); advanced.Children.Add(recordSample); advanced.Children.Add(decodeEu);
        var diagnostics = new StackPanel(); diagnostics.Children.Add(advanced); diagnostics.Children.Add(status);
        settings.Children.Add(new Expander { Header = "Advanced capture & diagnostics", Content = diagnostics, Margin = new(10, 6, 10, 10) });
        settingsPanel.Content = new ScrollViewer { Content = settings, MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; header.Children.Add(settingsPanel);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); root.Children.Add(tabs);
        var party = AddTab("Party", "Bars show captured party members, or named players when no roster is available. Click a bar for skills. Player names arrive when entering a zone. The full table below includes every observed actor.");
        party.Children.Remove(grids["Party"]); party.Children.Add(overview);
        party.Children.Add(new Expander { Header = "Full breakdown · all actors", Content = grids["Party"], Margin = new(8, 2, 8, 12) });
        party.Children.Insert(1, CharacterDetailsHeader(partySummary));
        overview.ActorSelected += FocusActor;
        AddTab("Skills", "Damage per skill for every observed actor. Bar width and Damage % show the share of damage in the current character filter. Pet damage is attributed when its owner was discovered. Hits are damage events, not cast counts.");
        var pvp = AddTab("PvP", "Damage exchanged with you or your captured party. Opponents appear before their name arrives when their character skills identify them; these rows are marked name pending. Known NPCs and summon-only skills are excluded. Both sides use the same PvP damage window. Party membership and pet ownership must be captured to attribute their exchanges.");
        var pvpHeader = new DockPanel { LastChildFill = true };
        var lastPvp = Button("Last saved PvP fight", ShowLastPvp); DockPanel.SetDock(lastPvp, Dock.Right);
        pvpHeader.Children.Add(lastPvp); pvpHeader.Children.Add(pvpSummary);
        pvp.Children.Insert(1, pvpHeader);
        pvp.Children.Insert(2, new ScrollViewer { Content = pvpMeter, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        pvpMeter.ActorSelected += FocusActor;
        AddTab("Defense", "Observed hits and avoided outcomes. Landed % is an event-based estimate, not a game accuracy stat; block/parry events are shown separately.");
        AddTab("Healing", "Beta: observed healing per healer, skill and recipient. Bars and Healing % show each row's share within the current character filter. Overhealing and effective healing are unavailable unless the protocol provides them.");
        var rotation = AddTab("Rotations", "Zoom with + / − or Ctrl + mouse wheel over the timeline; Fit fight shows everything. Inspect damage and healing anywhere across a player's lane. Click an event to pin details, then step with Left/Right. Zooming holds the view while capture continues; Follow latest / Escape resumes live updates. Each mark is an event, not an inferred cast.");
        rotation.Children.Remove(grids["Rotations"]);
        rotationView = new(grids["Rotations"], code => skills.GetSkillName(code), skillIcons.Find); rotation.Children.Add(rotationView);
        AddTab("Openers", "First 15 seconds from each character’s first recorded damage or healing event. Repeated hits are retained; this is an event sequence, not a cast-count estimate.");
        var specsPanel = AddTab("Specs", "Per-character skill levels, choices and equipment from imported or locally entered loadouts. Observed skills without a supplied loadout have unknown levels. Complete means the supplier marked the loadout complete.");
        specsPanel.Children.Insert(1, CharacterDetailsHeader(new TextBlock { Text = "Skill loadouts and equipment for every player", Margin = new(8) }));
        AddDetail(specsPanel, "Equipment", "Equipment · supplied loadouts");
        AddTab("Saved fights", "Double-click a row to inspect it. Capturing continues in the background while browsing. Demo fights are marked and excluded from trends and comparisons.");
        grids["Saved fights"].MouseDoubleClick += (_, _) => { if (grids["Saved fights"].SelectedItem is SavedRow row) { viewed = archive.First(f => f.Id == row.Id); encounter.Text = viewed.Label; tabs.SelectedIndex = 0; Refresh(); } };
        var trendsPanel = AddTab("Trends", "Choose a character and DPS or HPS. Different servers and classes stay separate. The chart shows the last 100 observations; the table retains all matching saved fights. Double-click a row to inspect its fight.");
        var trendControls = new WrapPanel(); trendControls.Children.Add(trendCharacter); trendControls.Children.Add(trendMetric);
        trendsPanel.Children.Insert(1, trendControls); trendsPanel.Children.Insert(2, trendSummary); trendsPanel.Children.Insert(3, trendChart);
        trendCharacter.SelectionChanged += (_, _) => { if (!updatingTrends) Refresh(); }; trendMetric.SelectionChanged += (_, _) => Refresh();
        grids["Trends"].MouseDoubleClick += (_, _) => { if (grids["Trends"].SelectedItem is TrendRow row) { viewed = archive.First(f => f.Id == row.FightId); encounter.Text = viewed.Label; tabs.SelectedIndex = 0; Refresh(); } };
        var reportPanel = AddTab("Report card", "Local-only comparisons: same encounter label, class, server and 1,000-CP band, with similar durations (−20%/+25%). Requires 20 observations for percentiles and 100 for top 5%. No population dataset is bundled.");
        reportPanel.Children.Insert(1, CharacterDetailsHeader(new TextBlock { Text = "Set class, CP and server to compare matching fights", Margin = new(8), TextWrapping = TextWrapping.Wrap }));
        var differences = AddTab("Top 5%", "Compare your damage, healing, criticals, event rate, opening damage and damage received with the top 5% of matching local fight observations. Requires 100 observations. Differences are descriptive, not proof that a playstyle caused better results.");
        differences.Children.Insert(1, CharacterDetailsHeader(differenceSummary));
        var cohortPanel = AddTab("Top cohort", "Top quarter needs 20 matching observations; top 5% needs 100. Match encounter, class, server, 1,000-CP band and duration (−20%/+25%). Skill shares include nonusers as zero. Spec rates require loadouts marked complete; equipment rates use known entries for that slot. Missing equipment and specs stay unknown.");
        cohortPanel.Children.Insert(1, CharacterDetailsHeader(cohortSummary));
        AddDetail(cohortPanel, "Cohort specs", "Skill levels and choices · complete supplied loadouts");
        AddDetail(cohortPanel, "Cohort equipment", "Equipment · supplied loadouts");
        AddDetail(cohortPanel, "Cohort openers", "First recorded skills · damage and healing");
        AddTab("Player stats", "Latest captured stats for your local character, saved with the fight. Unknown stat IDs retain their raw values. These packets do not describe every nearby player.");
        AddTab("Cooldowns", "Last reported skill cooldowns for your local character. Remaining time is the value when the packet arrived, not a running countdown.");
        AddTab("Network events", "Latest decoded HP, buffs, casts, guard and dungeon events. These are stored with the fight. Unsupported records do not appear as decoded events.");
        tabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.Source, tabs)) Refresh(); };
        character.TextChanged += (_, _) => Refresh();
        ApplyTheme(); ReloadArchive(); RefreshAdapters();
        if (!demo) ContentRendered += PromptForMissingNpcap;
        if (demo) LoadDemo(); else Refresh();
        timer.Tick += (_, _) => {
            try {
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
            try { capture?.Dispose(); LogCapture(); capture = null; FinishAutosave(); engine.SaveCompleted(store); var f = LiveSnapshot(); if (f.HasRecordedData) SaveFight(f); }
            catch (Exception ex) { e.Cancel = true; notice = "Could not save before closing: " + ex.Message; Refresh(); return; }
            shuttingDown = true; timer.Stop(); overlay?.Close(); LocalLog.Write("Application closed.");
        };
    }
    static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new(6) };
    static Border SettingsSection(string title, UIElement content)
    {
        var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new(8, 2, 0, 5) }); panel.Children.Add(content);
        var card = new Border { Child = panel, CornerRadius = new(7), Padding = new(8), Margin = new(4, 4, 4, 8) }; card.SetResourceReference(Border.BackgroundProperty, "Panel"); return card;
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
            RowHeight = 38, MaxHeight = 350, MinHeight = 80, EnableRowVirtualization = true, EnableColumnVirtualization = true,
            HeadersVisibility = DataGridHeadersVisibility.Column, Margin = new(8), ColumnWidth = DataGridLength.SizeToHeader };
        grid.AutoGeneratingColumn += (_, e) => { e.Column.Header = ColumnHeader(e.PropertyName); e.Column.MinWidth = 110; if (e.Column is DataGridTextColumn t && t.Binding is System.Windows.Data.Binding b && e.PropertyType == typeof(double)) b.StringFormat = "N1"; };
        detailGrids[key] = grid; panel.Children.Add(new Expander { Header = title, Content = grid, Margin = new(8) });
    }
    void SetDetail(string key, IEnumerable rows)
    {
        var data = rows.Cast<object>().ToArray(); var grid = detailGrids[key];
        ((LiveTable)grid).SetRows(data);
    }
    sealed record TrendChoice(string Character, string Job, int Server) { public string Label => $"{Character} · {Job} · server {Server}"; }
    void RefreshTrends()
    {
        var all = Benchmarks.Trends(archive).Where(r => Matches(r.Character)).ToArray();
        var choices = all.Select(r => new TrendChoice(r.Character, r.Job, r.Server)).Distinct().OrderBy(r => r.Label).ToArray();
        var selected = trendCharacter.SelectedItem as TrendChoice;
        updatingTrends = true;
        try {
            if (trendCharacter.ItemsSource == null || !trendCharacter.ItemsSource.Cast<TrendChoice>().SequenceEqual(choices)) trendCharacter.ItemsSource = choices;
            trendCharacter.SelectedItem = choices.FirstOrDefault(c => c == selected) ?? choices.FirstOrDefault(c => c.Character == Current.Name(Current.LocalPlayer)) ?? choices.FirstOrDefault();
        } finally { updatingTrends = false; }
        var choice = trendCharacter.SelectedItem as TrendChoice;
        var rows = all.Where(r => choice != null && r.Character == choice.Character && r.Job == choice.Job && r.Server == choice.Server).ToArray();
        bool healing = trendMetric.SelectedIndex == 1; var rates = rows.Select(r => healing ? r.HPS : r.DPS).ToArray();
        trendChart.Update(rows, healing); Set("Trends", rows.OrderByDescending(r => r.Date));
        trendSummary.Text = rates.Length == 0 ? "No saved observations for this character yet." :
            $"{rows.Length} observations · latest {rates[^1]:N1} · median {Benchmarks.Median(rates):N1} · best {rates.Max():N1} {(healing ? "HPS" : "DPS")} · compare matching encounters for context";
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
    void ShowFiles()
    {
        var menu = CreateFilesMenu(); menu.IsOpen = true;
    }
    ContextMenu CreateFilesMenu()
    {
        var menu = UiTheme.Menu(this);
        foreach (var (label, action) in new (string, Action)[] { ("Save current fight", () => { SaveFight(Current); ReloadArchive(); Refresh(); }), ("Import fights…", Import), ("Export current fight…", Export), ("Import events…", ImportEvents) })
        {
            var item = new MenuItem { Header = label };
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
    StackPanel AddTab(string title, string help)
    {
        var panel = new StackPanel();
        panel.Children.Add(new Expander { Header = "About this view", Foreground = UiTheme.Brush("#93A2B8"), FontSize = 11, Margin = new(8, 4, 8, 8), Content = new TextBlock { Text = help, TextWrapping = TextWrapping.Wrap, Margin = new(6, 10, 6, 6) } });
        var grid = new LiveTable { IsReadOnly = true, AutoGenerateColumns = true, CanUserAddRows = false, CanUserDeleteRows = false, EnableRowVirtualization = true, EnableColumnVirtualization = true, MaxHeight = 550, MinHeight = 120, RowHeight = 38, HeadersVisibility = DataGridHeadersVisibility.Column, Margin = new(8), GridLinesVisibility = DataGridGridLinesVisibility.None, BorderThickness = new(0) };
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
            if (e.PropertyName == "SkillCode" && title is "Skills" or "Healing" or "Rotations") e.Cancel = true;
            if (e.PropertyName == "Skill" && title is "Skills" or "Healing" or "Specs" or "Openers" or "Rotations")
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
            if (title == "Trends") e.Column.Width = e.PropertyName switch { "Date" => 170, "Encounter" => 250, _ => 90 };
            if (title == "Top 5%") { e.Column.MinWidth = 65; e.Column.Width = e.PropertyName switch { "Metric" => 270, "Unit" => 105, "Samples" => 70, _ => 110 }; }
            if (title == "Top cohort" && e.PropertyName == "Skill") e.Column.Width = 230;
            if (title == "Report card") { e.Column.MinWidth = 75; e.Column.Width = e.PropertyName switch { "Character" => 150, "Status" => 370, "DPS" => 75, _ => 115 }; }
            if (e.PropertyName == "HealingShare") { e.Column.Header = "Healing %"; e.Column.Width = 100; e.Column.DisplayIndex = 5; }
            if (e.PropertyName == "DamageShare") { e.Column.Header = "Damage %"; e.Column.Width = 100; }
            if (e.PropertyName == "LandedPercent") e.Column.Header = "Observed landed %";
            if (e.PropertyName == "HealerShare") e.Column.Header = "Healer share %";
            if (e.PropertyName == "LocalPercentile") e.Column.Header = "Local percentile";
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
        grids[title] = grid; panel.Children.Add(grid);
        tabs.Items.Add(new TabItem { Header = title, Tag = title switch { "Party" => "COMBAT", "Rotations" => "ANALYSIS", "Saved fights" => "LIBRARY", "Report card" => "INSIGHTS", "Network events" => "ADVANCED", _ => null }, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Visible, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } });
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
    void RefreshAdapters()
    {
        if (capture != null) return;
        npcapMissing = false;
        try {
            string selected = (adapters.SelectedItem as LibPcapLiveDevice)?.Name ?? preferences.Adapter;
            var devices = LibPcapLiveDeviceList.Instance.ToArray(); adapters.ItemsSource = devices;
            adapters.SelectedItem = devices.FirstOrDefault(d => d.Name == selected) ?? devices.FirstOrDefault();
            notice = adapters.Items.Count == 0 ? "No capture adapters found. Check your network connection and Npcap installation, then refresh adapters." : "Choose the adapter carrying game traffic. Use Npcap loopback if the game uses a local proxy.";
        }
        catch (Exception ex) {
            adapters.ItemsSource = null;
            npcapMissing = NpcapSetup.IsLoadFailure(ex);
            notice = "Npcap unavailable: " + ex.Message + " Demo and saved fights remain available.";
        }
    }
    void PromptForMissingNpcap(object? sender, EventArgs e)
    {
        ContentRendered -= PromptForMissingNpcap;
        if (npcapMissing) NpcapSetup.CreateDialog(this).ShowDialog();
    }
    void Start()
    {
        if (capture != null) return;
        if (adapters.SelectedItem == null) RefreshAdapters();
        if (npcapMissing) { NpcapSetup.CreateDialog(this).ShowDialog(); return; }
        if (adapters.SelectedItem is not LibPcapLiveDevice device) throw new InvalidOperationException("Select an Npcap adapter first.");
        if (string.IsNullOrWhiteSpace(filter.Text)) throw new InvalidOperationException("Enter a TCP capture filter.");
        var label = EncounterLabel();
        FinishAutosave(); engine.SaveCompleted(store); var previous = LiveSnapshot(); if (previous.HasRecordedData) SaveFight(previous);
        engine = new(decodeEu.IsChecked == true ? new EuGameDecoder() : null); engine.NewFight(label); viewed = null;
        var newCapture = new LocalCapture(device, engine.Process);
        string? samplePath = null;
        if (recordSample.IsChecked == true)
        {
            var dialog = new SaveFileDialog { Filter = "Packet capture|*.pcap", FileName = "eu-game-sample.pcap", Title = "Save a private packet sample (do not commit captures to GitHub)" };
            if (dialog.ShowDialog(this) != true) return;
            samplePath = dialog.FileName;
        }
        try { newCapture.Start(filter.Text, samplePath); }
        catch (Exception ex) when (NpcapSetup.IsLoadFailure(ex)) {
            npcapMissing = true; NpcapSetup.CreateDialog(this).ShowDialog(); return;
        }
        capture = newCapture;
        nextMaintenance = Environment.TickCount64; nextAutosave = nextMaintenance + 30000;
        start.Content = "Stop & save"; adapters.IsEnabled = false; filter.IsEnabled = false; decodeEu.IsEnabled = false;
        preferences.Adapter = device.Name; preferences.Filter = filter.Text; SavePreferences();
        notice = engine.DecoderStatus + (samplePath == null ? "" : " Private sample recording enabled (256 MB limit).");
        LocalLog.Write("Capture started: " + engine.DecoderStatus);
        Refresh();
    }
    void StopAndSave()
    {
        capture?.Dispose(); LogCapture(); capture = null; start.Content = "Start capture"; adapters.IsEnabled = true; filter.IsEnabled = true; decodeEu.IsEnabled = true;
        FinishAutosave(); engine.SaveCompleted(store); var f = LiveSnapshot(); if (f.HasRecordedData) SaveFight(f); ReloadArchive(); notice = "Capture stopped. Recorded events saved locally."; Refresh();
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
    void ToggleOverlay()
    {
        if (overlay != null) { overlay.Close(); overlay = null; return; }
        overlay = new OverlayWindow(NewFight, id => FocusActor(id, preferences.OverlayHealing), () => { customBars.IsChecked = preferences.OverlayCustomBars; SavePreferences(); }); overlay.Closed += (_, _) => overlay = null;
        var fight = capture != null ? LiveSnapshot() : Current;
        overlay.Update(fight, preferences, rows: ReferenceEquals(fight, displayedSnapshot) ? displayedTotals : null); overlay.Show();
    }
    void ApplyPreferences()
    {
        _ = (Color)ColorConverter.ConvertFromString(accent.Text);
        preferences.Theme = themes.SelectedItem?.ToString() ?? "Midnight"; preferences.Accent = accent.Text; preferences.Filter = filter.Text;
        preferences.OverlayCustomBars = customBars.IsChecked == true;
        preferences.Adapter = (adapters.SelectedItem as LibPcapLiveDevice)?.Name ?? preferences.Adapter;
        SavePreferences(); ApplyTheme(); Refresh();
    }
    void SavePreferences()
    {
        try { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PreferencePath)!); File.WriteAllText(PreferencePath, JsonSerializer.Serialize(preferences)); }
        catch (Exception ex) { notice = "Could not save settings: " + ex.Message; LocalLog.Write(notice); }
    }
    void ApplyTheme()
    {
        try { _ = (Color)ColorConverter.ConvertFromString(preferences.Accent); } catch { preferences.Accent = "#55D6BE"; }
        UiTheme.Apply(this, preferences);
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
        var f = Current;
        bool changed = !ReferenceEquals(f, displayedSnapshot);
        if (changed) { displayedTotals = Analysis.Totals(f); displayedPvp = PvpAnalysis.Build(f); displayedSnapshot = f; }
        var totals = displayedTotals;
        if (!skipUnchanged || changed || archiveChanged || (detailsPending && Environment.TickCount64 >= nextComparisonRefresh))
        {
        var meterData = MeterPresentation.Build(f, totals, limit: 50);
        summary.Text = f.Label; summary.TextTrimming = TextTrimming.CharacterEllipsis;
        summary.ToolTip = (f.Demo ? "Demo · " : viewed != null ? "Saved fight · " : "Live fight · ") + f.Label;
        damageValue.Text = MeterPresentation.Number(meterData.Amount); damageValue.ToolTip = $"{meterData.Amount:N0} damage · {meterData.Scope}";
        dpsValue.Text = MeterPresentation.Number(meterData.Rate); dpsValue.ToolTip = $"{meterData.Rate:N1} DPS · {meterData.Scope}";
        durationValue.Text = MeterPresentation.Duration(f);
        string tab = (tabs.SelectedItem as TabItem)?.Header?.ToString() ?? "Party";
        viewTitle.Text = tab; clearFilter.Visibility = character.Text.Length > 0 ? Visibility.Visible : Visibility.Hidden;
        showLive.Visibility = viewed != null ? Visibility.Visible : Visibility.Hidden;
        bool comparison = tab is "Trends" or "Report card" or "Top cohort" or "Top 5%";
        bool updateDetails = !skipUnchanged || archiveChanged || !comparison || Environment.TickCount64 >= nextComparisonRefresh;
        detailsPending = !updateDetails;
        if (comparison && updateDetails) nextComparisonRefresh = Environment.TickCount64 + 2000;
        if (updateDetails)
        switch (tab)
        {
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
                pvpSummary.Text = pvp.HasOpponents ? $"YOU  {pvp.YourDamage:N0} damage · {pvp.YourDamage / pvp.Seconds:N1} DPS     ENEMY  {pvp.EnemyDamage:N0} damage     {MeterPresentation.Duration(pvp.Seconds)} · {pvp.Opponents.Length} opponent(s) · enemies in red" : "Your row stays first. Names update when their identity packets arrive.";
                damageValue.Text = MeterPresentation.Number(pvp.YourDamage); damageValue.ToolTip = "Your damage to PvP opponents";
                dpsValue.Text = MeterPresentation.Number(pvp.YourDamage / pvp.Seconds); dpsValue.ToolTip = "Your DPS over the shared PvP window";
                durationValue.Text = MeterPresentation.Duration(pvp.HasOpponents ? pvp.Seconds : 0); break;
            case "Defense": Set(tab, Analysis.Defense(f).Where(r => Matches(r.Character)).ToArray()); break;
            case "Healing": Set(tab, SkillDamageDisplay.Shares(DetailedAnalysis.Healing(f, skills.GetSkillName).Where(r => Matches(r.Healer)))); break;
            case "Rotations":
                DrawTimeline(f, totals); break;
            case "Openers": Set(tab, DetailedAnalysis.Openers(f, skills.GetSkillName).Where(r => Matches(r.Character))); break;
            case "Specs": Set(tab, DetailedAnalysis.Specs(f, skills.GetSkillName).Where(r => Matches(r.Character))); SetDetail("Equipment", DetailedAnalysis.Equipment(f).Where(r => Matches(r.Character))); break;
            case "Saved fights": Set(tab, archive.Where(a => Matches(a.Label) || a.Actors.Values.Any(p => p.Player && Matches(p.Name))).Select(a => new SavedRow(a.Id, a.Created.ToLocalTime(), a.Label, a.Demo, a.Damage.Count + a.Healing.Count, Math.Round(a.Seconds, 1))).ToArray()); break;
            case "Trends": RefreshTrends(); break;
            case "Report card": Set(tab, Benchmarks.Report(f, archive).Where(r => Matches(r.Character)).ToArray()); break;
            case "Player stats": Set(tab, CharacterReadings.Rows(f.LocalStats)); break;
            case "Cooldowns": Set(tab, CharacterReadings.Cooldowns(f.Network, skills)); break;
            case "Network events": Set(tab, f.Network.TakeLast(500).Reverse().Select(e => new { e.Timestamp, e.Kind, e.Opcode, Fields = e.Fields.GetRawText() }).ToArray()); break;
            case "Top cohort":
                var focus = ComparisonActor(f, totals); var cohortPeers = focus == null ? [] : Benchmarks.Peers(f, focus, archive);
                cohortSummary.Text = ComparisonStatus(f, focus, 20, cohortPeers.Length);
                Set(tab, focus == null ? [] : Benchmarks.Cohort(f, focus, archive, skills.GetSkillName, cohortPeers));
                SetDetail("Cohort specs", focus == null ? [] : Benchmarks.Specs(f, focus, archive, skills.GetSkillName, cohortPeers));
                SetDetail("Cohort equipment", focus == null ? [] : Benchmarks.Equipment(f, focus, archive, cohortPeers));
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
        if (overlay == null) return;
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
    sealed record SavedRow(string Id, DateTime Date, string Encounter, bool Demo, int Events, double Seconds);
    internal void RunVisualSmokeTest()
    {
        Directory.CreateDirectory("test-artifacts");
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
            if (tab.Header.ToString() is "Skills" or "Healing")
            {
                var grid = grids[tab.Header.ToString()!];
                if (grid.Columns.SingleOrDefault(c => c.Header?.ToString() == "Skill") is not DataGridTemplateColumn skillColumn)
                    throw new Exception("Skill icon column was not generated.");
                var content = skillColumn.GetCellContent(grid.Items[0]);
                if (content == null || !VisualImages(content).Any(i => i.Source != null)) throw new Exception("Skill cell did not render its icon or fallback.");
            }
            if (tab.Header.ToString() == "Player stats" && grids["Player stats"].Items.Count != 3) throw new Exception("Player stats view did not populate.");
            if (tab.Header.ToString() == "Cooldowns" && grids["Cooldowns"].Items.Count != 1) throw new Exception("Cooldown view did not populate.");
            if (tab.Header.ToString() is "Party" or "Rotations" or "Skills" or "Healing" or "PvP" or "Player stats" or "Cooldowns") Render(this, System.IO.Path.Combine("test-artifacts", tab.Header + ".png"));
        }
        if (grids["Party"].Items.Count != 13 || grids["PvP"].Items.Count != 3) throw new Exception("Demo grids failed to populate.");
        string oldTheme = preferences.Theme;
        foreach (string theme in themes.Items) { preferences.Theme = theme; ApplyTheme(); UpdateLayout(); }
        settingsPanel.Visibility = Visibility.Visible; settingsPanel.IsExpanded = true; UpdateLayout();
        foreach (string theme in themes.Items)
        {
            preferences.Theme = theme; ApplyTheme(); UpdateLayout();
            themes.IsDropDownOpen = true; Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { })); themes.UpdateLayout();
            var popup = (Popup)themes.Template.FindName("PART_Popup", themes);
            if (popup.Child is not Border surface || surface.Background is not SolidColorBrush background || background.Color.R > 100)
                throw new Exception("Theme dropdown lost its dark popup surface.");
            surface.UpdateLayout(); Render(surface, $"test-artifacts/Dropdown-{theme}.png"); themes.IsDropDownOpen = false;
            CheckMenu(CreateFilesMenu(), $"Files-{theme}");
        }
        settingsPanel.IsExpanded = false; settingsPanel.Visibility = Visibility.Collapsed;
        preferences.Theme = oldTheme; ApplyTheme(); tabs.SelectedIndex = 0;
        if (overview.RowCount != 10) throw new Exception("Overview did not display the ten party members.");
        // Keep automated layout checks independent of a user's saved display preferences.
        var testOverlay = new OverlayWindow(); var testPreferences = new Preferences { OverlayScope = "Party" };
        testOverlay.Update(Current, testPreferences); testOverlay.Show(); testOverlay.UpdateLayout();
        testOverlay.CheckModes(); testOverlay.UpdateLayout(); Render(testOverlay, "test-artifacts/Overlay.png");
        CheckMenu(testOverlay.CreateOptions(), "Overlay-menu");
        testOverlay.Width = 310; testOverlay.Height = 300; testOverlay.UpdateLayout(); testOverlay.CheckPinnedScrolling(); Render(testOverlay, "test-artifacts/Overlay-small.png");
        testPreferences.OverlayHealing = true; testPreferences.OverlayLocked = true; testOverlay.Update(Current, testPreferences); testOverlay.UpdateLayout();
        if (testOverlay.ResizeMode != ResizeMode.NoResize || testOverlay.RowCount != 10) throw new Exception("Locked healing overlay failed.");
        Render(testOverlay, "test-artifacts/Overlay-healing.png"); testOverlay.Close();
        var pvpOverlay = new OverlayWindow(); pvpOverlay.Update(Current, new Preferences()); pvpOverlay.Show(); pvpOverlay.UpdateLayout();
        if (pvpOverlay.CurrentScope != "PvP" || pvpOverlay.RowCount != displayedPvp.Combatants.Length) throw new Exception("Automatic PvP overlay did not retain both sides.");
        pvpOverlay.CheckPinnedScrolling(); Render(pvpOverlay, "test-artifacts/Overlay-pvp.png"); pvpOverlay.Close();
        var duel = PvpTests.Duel(); var duelOverlay = new OverlayWindow();
        duelOverlay.Update(duel, new Preferences()); duelOverlay.Show(); duelOverlay.UpdateLayout();
        if (duelOverlay.CurrentScope != "PvP" || duelOverlay.RowCount != 2) throw new Exception("Unnamed PvP opponent is hidden in the overlay.");
        duelOverlay.CheckPinnedScrolling(); Render(duelOverlay, "test-artifacts/Overlay-pvp-unnamed.png");
        duelOverlay.Update(new Fight { LocalPlayer = 1, Actors = new(duel.Actors) }, new Preferences());
        if (duelOverlay.CurrentScope == "PvP") throw new Exception("Automatic PvP scope did not reset for the next fight.");
        duelOverlay.Close();
        var previousView = viewed; viewed = duel;
        tabs.SelectedItem = tabs.Items.Cast<TabItem>().First(t => t.Header?.ToString() == "PvP"); Refresh(); UpdateLayout();
        if (grids["PvP"].Items.Count != 1 || pvpMeter.RowCount != 2) throw new Exception("Unnamed opponent is hidden in the PvP tab.");
        Render(this, "test-artifacts/PvP-duel.png"); viewed = previousView; tabs.SelectedIndex = 0; Refresh();
        CheckFeatureViews();
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
            if (grids["Top cohort"].Items.Count != 4 || detailGrids.Values.Any(g => g.ItemsSource == null && g != detailGrids["Equipment"]))
                throw new InvalidOperationException("Cohort details are missing.");
            foreach (string detail in new[] { "Cohort specs", "Cohort equipment", "Cohort openers" })
                if (detailGrids[detail].Items.Count == 0) throw new InvalidOperationException("Empty " + detail);
            Show("Trends");
            if (grids["Trends"].Items.Count != 100 || !trendChart.Children.OfType<Polyline>().Any(l => l.Points.Count == 100))
                throw new InvalidOperationException("Trend chart did not draw the chosen character's observations.");
            trendMetric.SelectedIndex = 1; Refresh(); UpdateLayout(); Render(this, "test-artifacts/Populated-Trends-HPS.png");
            Show("Specs");
            if (detailGrids["Equipment"].Items.Count != 1 || !grids["Specs"].Items.OfType<SpecRow>().Any(r => r.SkillCode == 17030000 && r.Level == 7 && r.RecordedEvents == 0))
                throw new InvalidOperationException("Unused loadout skills or equipment were lost in the UI.");
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
