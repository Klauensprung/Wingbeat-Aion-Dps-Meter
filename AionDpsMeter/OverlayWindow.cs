using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AionDpsMeter.Local;

public sealed class OverlayWindow : Window
{
    readonly PartyHealthView partyHealth = new();
    internal Func<Dictionary<int,double>>? PartyRecovery { get; set; }
    internal Func<Fight,Dictionary<int,PartyRange>>? PartyRanges { get; set; }
    internal Func<PartyMemberDebuff[]>? PartyDebuffs { get; set; }
    internal Func<Fight, Dictionary<int, string>>? PartyDistances { get; set; }
    readonly MeterView meter = new() { Margin = new(7, 4, 7, 2), RowBackground = UiTheme.Brush("#801A2230") };
    readonly MeterView ownMeter = new() { Margin = new(7, 4, 7, 0), Visibility = Visibility.Collapsed, RowBackground = UiTheme.Brush("#801A2230") };
    readonly ScrollViewer actorScroll;
    readonly SkillBreakdownView skillBreakdown;
    Fight? skillSnapshot;
    bool skillHealing;
    PersonalSkill[] personalSkills = [];
    Fight? filterSource, filteredFight;
    TotalRow[]? filteredTotals;
    PvpData? filteredPvp;
    bool filteredBosses;
    int selectedTarget, filteredTarget;
    Actor? selectedTargetIdentity;
    Fight? healthSource;
    int engagedBoss;
    Dictionary<int, HealthReading> health = new();
    Dictionary<int, GuardDisplay> guards = new();
    readonly BackAttackIndicator backAttack = new();
    readonly DispatcherTimer backAttackTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    readonly Border backAttackBorder = new() { BorderBrush = UiTheme.Brush("#88E7CF7D"), BorderThickness = new(2),
        CornerRadius = new(4), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly ParticipantColors participantColors = new();
    readonly StackPanel header = new() { Background = UiTheme.Brush("#6010151F") };
    readonly TextBlock moveGrip = new() { Text = "⋮⋮", FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Foreground = UiTheme.Brush("#93A2B8") };
    readonly TextBlock duration = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new(4, 0, 8, 0) };
    readonly TextBlock encounter = new() { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock targetHealthText = new() { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(12, 3, 12, 6),
        Foreground = UiTheme.Brush("#E7CF7D"), Visibility = Visibility.Collapsed };
    readonly TextBlock total = new() { FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new(10, 7, 10, 4) };
    readonly StackPanel targetGuard = new() { Margin = new(12, 0, 12, 6), Visibility = Visibility.Collapsed };
    readonly TextBlock targetGuardText = new() { FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly Grid targetGuardBar = new() { Height = 3, Margin = new(0, 3, 0, 0), Background = UiTheme.Brush("#344154") };
    readonly Border targetGuardFill = new();
    readonly TextBlock energy = new() { FontSize = 10, Margin = new(10, 0, 20, 8) };
    readonly PingView pingStatus = new() { Margin = new(10, 0, 20, 4) };
    readonly Button mode, moveLock;
    readonly Action? reset;
    readonly Action? trainingAction;
    public string TrainingLabel { get; set; } = "Training…";
    public string Notification { get; set; } = "";
    readonly Action? saveSettings;
    readonly ResizeGrip resizeGrip = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Width = 14, Height = 14, Opacity = .5 };
    Preferences preferences = new();
    Fight? fight;
    TotalRow[]? totals;
    PvpData? pvp;
    string lastTheme = "", lastAccent = "";
    string? lastBackAttackColor;
    bool positioned, closed;
    public int RowCount => preferences.OverlayPartyHealth && fight?.Training == null ? partyHealth.RowCount : ownMeter.RowCount + meter.RowCount;
    internal string CurrentScope { get; private set; } = "";
    internal string MeterLabel { get; set; } = "";
    internal Action<bool>? SharedVisibilityChanged { get; set; }

    public OverlayWindow(Action? reset = null, Action<int>? actorSelected = null, Action? saveSettings = null, Func<int, string>? skillName = null, SkillIcons? skillIcons = null, Action? trainingAction = null)
    {
        this.reset = reset; this.saveSettings = saveSettings;
        this.trainingAction = trainingAction;
        skillBreakdown = new(skillName ?? (id => $"Skill {id}"), skillIcons) { Margin = new(8, 6, 8, 2) };
        Title = "Wingbeat Overlay"; Width = 390; Height = 530; MinWidth = 310; MinHeight = 185;
        Topmost = true; ShowInTaskbar = false; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip;
        AllowsTransparency = true; Background = Brushes.Transparent;
        FontFamily = new("Segoe UI"); UseLayoutRounding = true; SnapsToDevicePixels = true;
        var shell = new Border { Padding = new(1, 3, 1, 1) };
        var layers = new Grid(); layers.Children.Add(shell); layers.Children.Add(backAttackBorder); Content = layers;
        var root = new DockPanel(); shell.Child = root;
        // Tint only the controls and bars: unused space stays clear and text
        // retains full opacity. The header's tint also keeps it easy to drag.
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        // Buttons consume their own mouse-down events. Everything else in the
        // header, including the full encounter row and its padding, can drag.
        header.MouseLeftButtonDown += (_, e) => {
            if (e.ChangedButton != MouseButton.Left || preferences.OverlayLocked) return;
            e.Handled = true; DragMove();
        };
        var toolbar = new DockPanel { Margin = new(6, 4, 5, 0), LastChildFill = true, Background = Brushes.Transparent };
        mode = SmallButton("DAMAGE  ▾", "Choose damage, healing, party health and display options", ShowOptions); mode.FontWeight = FontWeights.Bold; mode.HorizontalAlignment = HorizontalAlignment.Left;
        var close = SmallButton("×", "Close overlay", Close); close.FontSize = 19;
        moveLock = SmallButton("◇", "Lock or unlock overlay position", () => { preferences.OverlayLocked = !preferences.OverlayLocked; RefreshMeter(); saveSettings?.Invoke(); });
        var clear = SmallButton("↻", "Save and start a new fight", () => this.reset?.Invoke()); clear.FontSize = 18;
        foreach (var button in new[] { close, moveLock, clear }) { DockPanel.SetDock(button, Dock.Right); toolbar.Children.Add(button); }
        DockPanel.SetDock(duration, Dock.Right); toolbar.Children.Add(duration); toolbar.Children.Add(mode);
        var moveRow = new DockPanel(); DockPanel.SetDock(moveGrip, Dock.Left);
        moveRow.Children.Add(moveGrip); moveRow.Children.Add(encounter);
        AutomationProperties.SetName(moveGrip, "Move overlay");
        AutomationProperties.SetAutomationId(moveGrip, "OverlayMoveGrip");
        header.Children.Add(toolbar);
        header.Children.Add(new Border { Child = moveRow, Background = Brushes.Transparent, MinHeight = 27, Padding = new(10, 0, 10, 6) });
        header.Children.Add(targetHealthText);
        targetGuardBar.ColumnDefinitions.Add(new() { Width = new(0, GridUnitType.Star) });
        targetGuardBar.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        targetGuardBar.Children.Add(targetGuardFill);
        targetGuard.Children.Add(targetGuardText); targetGuard.Children.Add(targetGuardBar); header.Children.Add(targetGuard);
        // Keep the local row outside the scrolling opponents/party list so it
        // stays visible even when the overlay only has room for a single row.
        DockPanel.SetDock(ownMeter, Dock.Top); root.Children.Add(ownMeter);
        var footer = new Grid { Background = UiTheme.Brush("#6010151F") }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var footerText = new StackPanel(); footerText.Children.Add(total); footerText.Children.Add(pingStatus); footerText.Children.Add(energy); footer.Children.Add(footerText);
        footer.Children.Add(resizeGrip);
        actorScroll = new ScrollViewer { Content = meter, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        root.Children.Add(actorScroll);
        meter.ActorSelected += id => actorSelected?.Invoke(id);
        ownMeter.ActorSelected += id => actorSelected?.Invoke(id);
        backAttackTimer.Tick += (_, _) => RefreshBackAttackBorder();
        IsVisibleChanged += (_, _) => { if (IsVisible && !closed) { RefreshBackAttackBorder(); backAttackTimer.Start(); } else backAttackTimer.Stop(); };
        OverlayPlacement.Track(this, SavePlacement);
        Closed += (_, _) => {
            closed = true; backAttackTimer.Stop();
            SavePlacement();
        };
    }
    void SavePlacement()
    {
        preferences.OverlayWidth = Width; preferences.OverlayHeight = Height;
        preferences.OverlayLeft = Left; preferences.OverlayTop = Top; saveSettings?.Invoke();
    }
    static Button SmallButton(string text, string tip, Action action)
    {
        var button = new Button { Content = text, ToolTip = tip, Padding = new(7, 4, 7, 4), Margin = new(1), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, FontSize = 11 };
        button.Click += (_, _) => action(); return button;
    }
    void ShowOptions()
    {
        OpenModeMenu(CreateOptions());
    }
    void OpenModeMenu(ContextMenu menu)
    {
        // Aion players hold Alt to free the cursor. WPF otherwise interprets
        // that key (including its release) as a request to leave menu mode.
        menu.PreviewKeyDown+=KeepGameCursorMenu;
        menu.PreviewKeyUp+=KeepGameCursorMenu;
        mode.ContextMenu=menu;
        menu.Closed+=(_,_)=>{if(!closed)RefreshMeter();};
        menu.IsOpen=true;
    }
    static void KeepGameCursorMenu(object sender,KeyEventArgs e)
    {
        var key=e.Key==Key.System?e.SystemKey:e.Key;
        if(key is Key.LeftAlt or Key.RightAlt)e.Handled=true;
    }
    internal ContextMenu CreateOptions()
    {
        var menu = UiTheme.Menu(this, mode); menu.Placement = PlacementMode.Bottom;
        void Item(string title, bool selected, Action action)
        {
            var item = new MenuItem { Header = title, IsCheckable = true, IsChecked = selected };
            item.Click += (_, _) => { action(); RefreshMeter(); saveSettings?.Invoke(); }; menu.Items.Add(item);
        }
        Item("Damage · DPS", !preferences.OverlayPartyHealth && !preferences.OverlayHealing, () => { preferences.OverlayPartyHealth=false; preferences.OverlayHealing=false; });
        Item("Healing · HPS", !preferences.OverlayPartyHealth && preferences.OverlayHealing, () => { preferences.OverlayPartyHealth=false; preferences.OverlayHealing=true; });
        Item("Party health · HP and debuffs", preferences.OverlayPartyHealth, () => preferences.OverlayPartyHealth=true);
        Item("Show my skills", preferences.OverlayShowSkills, () => preferences.OverlayShowSkills = !preferences.OverlayShowSkills);
        Item("Show target / boss health", preferences.OverlayShowTargetHealth, () => preferences.OverlayShowTargetHealth = !preferences.OverlayShowTargetHealth);
        menu.Items.Add(new Separator());
        string scope = preferences.OverlayAllActors ? "All" : preferences.OverlayScope;
        void Scope(string title, string value) => Item(title, scope == value, () => { preferences.OverlayScope = value; preferences.OverlayAllActors = false; });
        Scope("Automatic · party / PvP", "Auto");
        Scope("PvP · you and opponents", "PvP");
        Scope("Party / players", "Party");
        Scope("Only my character", "Self");
        Scope("All nearby players", "Players");
        Scope("All actors", "All");
        var targetChoice = new MenuItem { Header = "Targets: " + TargetLabel + "…" };
        if (trainingAction != null) {
            var trainingItem = new MenuItem { Header = TrainingLabel };
            trainingItem.Click += (_, _) => Dispatcher.BeginInvoke(trainingAction); menu.Items.Add(trainingItem);
        }
        targetChoice.Click += (_, _) => Dispatcher.BeginInvoke(new Action(ShowTargets)); menu.Items.Add(targetChoice);
        menu.Items.Add(new Separator());
        Item("Pin my character first", preferences.OverlayPinLocal, () => preferences.OverlayPinLocal = !preferences.OverlayPinLocal);
        Item("Compact rows", preferences.OverlayCompact, () => preferences.OverlayCompact = !preferences.OverlayCompact);
        Item("Class colours", !preferences.OverlayCustomBars, () => preferences.OverlayCustomBars = !preferences.OverlayCustomBars);
        Item("Lock position", preferences.OverlayLocked, () => preferences.OverlayLocked = !preferences.OverlayLocked);
        Item("Hide overlays outside Aion 2 / Wingbeat", preferences.OverlaysGameOnly, () => { preferences.OverlaysGameOnly = !preferences.OverlaysGameOnly; SharedVisibilityChanged?.Invoke(preferences.OverlaysGameOnly); });
        return menu;
    }
    public void Update(Fight value, Preferences settings, string unusedEnergy = "", TotalRow[]? rows = null, PvpData? pvpData = null)
    {
        if (closed) return;
        bool changed = !ReferenceEquals(fight, value);
        if (lastBackAttackColor != settings.BackAttackColor) {
            var brush = new SolidColorBrush(BackAttackColour(settings.BackAttackColor)); brush.Freeze();
            backAttackBorder.BorderBrush = brush; lastBackAttackColor = settings.BackAttackColor;
        }
        if (fight?.Id != value.Id) { selectedTarget = 0; selectedTargetIdentity = null; }
        if (selectedTargetIdentity is { } identity && value.Actors.GetValueOrDefault(selectedTarget) is { } current &&
            (identity.Name != current.Name || identity.NpcId != current.NpcId || identity.Server != current.Server)) {
            selectedTarget = 0; selectedTargetIdentity = null;
        }
        preferences = settings; fight = value; totals = rows ?? (changed ? Analysis.Totals(value) : totals) ?? [];
        pvp = pvpData ?? (changed ? PvpAnalysis.Build(value) : pvp) ?? PvpAnalysis.Build(value);
        if (!positioned)
        {
            Width = double.IsFinite(settings.OverlayWidth) ? Math.Clamp(settings.OverlayWidth, 310, 900) : 390;
            Height = double.IsFinite(settings.OverlayHeight) ? Math.Clamp(settings.OverlayHeight, 185, 1000) : 530;
            if (settings.OverlayLeft is double left && settings.OverlayTop is double top && double.IsFinite(left) && double.IsFinite(top))
            {
                Left = Math.Clamp(left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + Math.Max(0, SystemParameters.VirtualScreenWidth - Width));
                Top = Math.Clamp(top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + Math.Max(0, SystemParameters.VirtualScreenHeight - Height));
            }
            positioned = true;
        }
        if (lastTheme != settings.Theme || lastAccent != settings.Accent)
        {
            UiTheme.Apply(this, settings); lastTheme = settings.Theme; lastAccent = settings.Accent;
            // Shared themes set an opaque window surface for the main app.
            Background = Brushes.Transparent;
            duration.Foreground = encounter.Foreground = energy.Foreground = UiTheme.Brush("#93A2B8"); total.Foreground = UiTheme.Brush("#E7EDF7");
        }
        RefreshMeter();
    }
    void RefreshMeter()
    {
        // Retain each incoming snapshot, but leave the visual tree and popup anchor
        // untouched during selection. Closing renders the latest snapshot once.
        if (fight == null || totals == null || pvp == null || mode.ContextMenu?.IsOpen==true) return;
        bool training = fight.Training != null;
        if (preferences.OverlayFollowGameTarget && !training) selectedTarget = fight.LocalTarget is { EntityId: > 0 } target ? target.EntityId : -1;
        bool healing = !training && preferences.OverlayHealing;
        bool showHealth = !training && preferences.OverlayPartyHealth;
        bool showSkills = training || !showHealth && preferences.OverlayShowSkills;
        bool bossesOnly = !training && !preferences.OverlayFollowGameTarget && preferences.OverlayBossesOnly;
        int displayTarget = training ? 0 : selectedTarget;
        if (!ReferenceEquals(filterSource, fight) || filteredBosses != bossesOnly || filteredTarget != displayTarget) {
            filterSource = fight; filteredBosses = bossesOnly; filteredTarget = displayTarget;
            filteredFight = OverlayCombatFilter.Apply(fight, filteredBosses, displayTarget);
            filteredTotals = ReferenceEquals(filteredFight, fight) ? totals : Analysis.Totals(filteredFight);
            filteredPvp = ReferenceEquals(filteredFight, fight) ? pvp : PvpAnalysis.Build(filteredFight);
        }
        var display = filteredFight!; var displayPvp = filteredPvp!;
        if (!ReferenceEquals(healthSource, fight)) { healthSource = fight; health = CapturedHealth.Build(fight); guards = CapturedGuard.Build(fight); engagedBoss = EngagedBoss.Latest(fight); }
        backAttack.Observe(fight, Environment.TickCount64, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        RefreshBackAttackBorder();
        string scope = training ? "Self" : preferences.OverlayAllActors ? "All" : preferences.OverlayScope;
        bool showPvp = scope == "PvP" || (scope is not ("All" or "Party" or "Self" or "Players") && displayPvp.HasOpponents);
        var data = showPvp ? PvpAnalysis.Meter(display, displayPvp, healing, pinLocal: preferences.OverlayPinLocal) :
            MeterPresentation.Build(display, filteredTotals!, healing, scope == "All", pinLocal: preferences.OverlayPinLocal, actorScope: scope);
        if (!preferences.OverlayCustomBars)
            data = data with { Rows = data.Rows.Select(r => r with { Color = MeterPresentation.ClassColorFor(r.Job, r.Color) }).ToArray() };
        else data = participantColors.Apply(fight, data);
        data = data with { Rows = data.Rows.Select(r => r with { Health = health.GetValueOrDefault(r.Id)?.Label ?? "" }).ToArray() };
        CurrentScope = data.Scope;
        pingStatus.Update(fight.LocalPing, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        mode.Content = (showPvp ? "PVP · " : "") + (healing ? "HEALING" : "DAMAGE") + "  ▾";
        moveLock.Content = preferences.OverlayLocked ? "◆" : "◇";
        moveLock.ToolTip = preferences.OverlayLocked ? "Unlock overlay position" : "Lock overlay position";
        header.Cursor = preferences.OverlayLocked ? Cursors.Arrow : Cursors.SizeAll;
        header.ToolTip = preferences.OverlayLocked ? "Position locked — use the diamond button to unlock" : "Drag the header to move the overlay";
        moveGrip.Opacity = preferences.OverlayLocked ? .35 : 1;
        var resizeMode=preferences.OverlayLocked ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
        if(ResizeMode!=resizeMode)ResizeMode=resizeMode;
        resizeGrip.Visibility = preferences.OverlayLocked ? Visibility.Collapsed : Visibility.Visible;
        duration.Text = showPvp ? MeterPresentation.Duration(displayPvp.HasOpponents ? displayPvp.Seconds : 0) : MeterPresentation.Duration(fight);
        encounter.Text = (fight.Demo ? "DEMO  ·  " : "") + fight.Label + "  ·  " + data.Scope;
        var own = data.Rows.Where(r => preferences.OverlayPinLocal && r.Local).ToArray();
        var others = data.Rows.Where(r => !preferences.OverlayPinLocal || !r.Local).ToArray();
        ownMeter.Update(own, preferences.OverlayCompact, healing, fight.HasEvents, showClassSymbols: true);
        ownMeter.Visibility = own.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        actorScroll.Visibility = own.Length > 0 && others.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        string? filteredEmpty = selectedTarget == -1 ? "No in-game target captured." : bossesOnly || selectedTarget > 0 ? "No matching events for this target and player scope." : null;
        meter.Update(others, preferences.OverlayCompact, healing, fight.HasEvents, emptyText: filteredEmpty, startPosition: own.Length > 0 ? 2 : 1, showClassSymbols: true);
        total.Text = showPvp && !healing ? $"YOU   {MeterPresentation.Number(displayPvp.YourDamage)}     ·     ENEMY   {MeterPresentation.Number(displayPvp.EnemyDamage)}" :
            $"TOTAL   {MeterPresentation.Number(data.Amount)}     ·     {MeterPresentation.Number(data.Rate)} {(healing ? "HPS" : "DPS")}";
        energy.Text = fight.Energy == null ? "Wingbeat  ·  local" : $"Loot energy   {fight.Energy.Daily:N0} daily  /  {fight.Energy.Bonus:N0} bonus";
        actorScroll.Content = showSkills ? skillBreakdown : meter;
        if (showSkills) {
            if (!ReferenceEquals(skillSnapshot, display) || skillHealing != healing) {
                personalSkills = PersonalSkills.Build(display, healing); skillSnapshot = display; skillHealing = healing;
            }
            skillBreakdown.Update(personalSkills, healing, preferences.OverlayCompact, preferences.Accent, fight.LocalPlayer > 0, filteredEmpty);
            var personalData = MeterPresentation.Build(display, filteredTotals!, healing, pinLocal: true, actorScope: "Self");
            if (preferences.OverlayCustomBars) personalData = participantColors.Apply(fight, personalData);
            ownMeter.Update(personalData.Rows, preferences.OverlayCompact, healing, fight.HasEvents, showClassSymbols: true);
            ownMeter.Visibility = personalData.Rows.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            actorScroll.Visibility = Visibility.Visible;
            CurrentScope = "Your skills"; mode.Content = "MY SKILLS  ▾";
            encounter.Text = (fight.Demo ? "DEMO  ·  " : "") + fight.Name(fight.LocalPlayer) + "  ·  " + fight.Label;
            duration.Text = MeterPresentation.Duration(fight);
            long amount = personalSkills.Sum(r => r.Amount);
            total.Text = $"YOU   {MeterPresentation.Number(amount)}     ·     {MeterPresentation.Number(amount / fight.Seconds)} {(healing ? "HPS" : "DPS")}";
        } else { skillSnapshot = null; personalSkills = []; }
        if (bossesOnly || selectedTarget != 0) encounter.Text += "  ·  " + TargetLabel;
        int healthTarget = selectedTarget > 0 ? selectedTarget : preferences.OverlayFollowGameTarget ? 0 : engagedBoss;
        targetHealthText.Visibility = !training && preferences.OverlayShowTargetHealth && healthTarget > 0 ? Visibility.Visible : Visibility.Collapsed;
        var guard = guards.GetValueOrDefault(healthTarget);
        targetGuard.Visibility = targetHealthText.Visibility == Visibility.Visible && guard != null ? Visibility.Visible : Visibility.Collapsed;
        targetGuardText.Text = guard?.Label ?? ""; targetGuard.ToolTip = targetGuardText.Text;
        targetGuardBar.ColumnDefinitions[0].Width = new(guard?.Ratio ?? 0, GridUnitType.Star);
        targetGuardBar.ColumnDefinitions[1].Width = new(1 - (guard?.Ratio ?? 0), GridUnitType.Star);
        targetGuardFill.Background = UiTheme.Brush(guard?.Broken == true ? "#E7CF7D" : "#75B5F5");
        targetHealthText.Text = healthTarget > 0 ? $"{fight.Name(healthTarget)} · {health.GetValueOrDefault(healthTarget)?.Label ?? "HP not captured"}" : "";
        targetHealthText.ToolTip = (selectedTarget > 0 ? "Selected target" : "Most recently engaged recognized boss") + "\n" + targetHealthText.Text +
            "\nHealth is the last captured value. Maximum HP is unavailable; no percentage is inferred.";
        if (training) {
            mode.Content = "TRAINING  ▾";
            energy.Text = TrainingLabel.Contains("stop", StringComparison.Ordinal) ? TrainingLabel : "Saved training attempt";
            duration.Text = MeterPresentation.Duration(fight.Seconds);
        }
        if (showHealth) {
            var healthRows = PartyHealthView.Build(fight, health, PartyDebuffs?.Invoke() ?? [], PartyRecovery?.Invoke());
            var distances = PartyDistances?.Invoke(fight);
            var ranges=PartyRanges?.Invoke(fight);
            if(ranges!=null)healthRows=healthRows.Select(row=>row with {Range=row.Entity==fight.LocalPlayer?PartyRange.Nearby:ranges.GetValueOrDefault(row.Entity,PartyRange.Unknown)}).ToArray();
            if (distances != null) healthRows = healthRows.Select(row => row with { Proximity = row.Entity == fight.LocalPlayer ? "" : distances.GetValueOrDefault(row.Entity, "Distance unknown") }).ToArray();
            partyHealth.Update(healthRows, preferences.OverlayCompact);
            actorScroll.Content = partyHealth; actorScroll.Visibility = Visibility.Visible;
            ownMeter.Visibility = Visibility.Collapsed; targetHealthText.Visibility = targetGuard.Visibility = Visibility.Collapsed;
            mode.Content = "PARTY HEALTH  ▾"; CurrentScope = "Party health";
            encounter.Text = fight.PartyKnown ? "Party health · last captured HP" : PartyHealthView.HasRoster(fight)
                ? "Party health · partial roster · last captured HP" : "Party health · waiting for party roster";
            total.Text = $"{healthRows.Length} MEMBERS   ·   {healthRows.Count(r=>r.Critical)} AT OR BELOW 25%";
            energy.Text = "Green 75% · yellow 50% · red 25% · captured debuffs";
        }
        encounter.ToolTip = encounter.Text;
        if(MeterLabel.Length>0)mode.Content=MeterLabel+" · "+mode.Content;
        if (Notification.Length > 0) { energy.Text = Notification; energy.ToolTip = Notification; }
        else energy.ToolTip = null;
    }
    string TargetLabel => preferences.OverlayFollowGameTarget ? "In-game: " + (selectedTarget > 0 && fight != null ? fight.Name(selectedTarget) : "not captured") : selectedTarget > 0 && fight != null ? fight.Name(selectedTarget) : preferences.OverlayBossesOnly ? "Bosses only" : "All targets";
    void ShowTargets()
    {
        OpenModeMenu(CreateTargetsMenu());
    }
    ContextMenu CreateTargetsMenu()
    {
        var menu = UiTheme.Menu(this, mode); menu.Placement = PlacementMode.Bottom;
        void Choice(string label, bool bosses, int id) {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = !preferences.OverlayFollowGameTarget && selectedTarget == id && preferences.OverlayBossesOnly == bosses };
            item.Click += (_, _) => { preferences.OverlayFollowGameTarget = false; selectedTarget = id; selectedTargetIdentity = fight?.Actors.GetValueOrDefault(id); preferences.OverlayBossesOnly = bosses; RefreshMeter(); saveSettings?.Invoke(); };
            menu.Items.Add(item);
        }
        var follow = new MenuItem { Header = "Follow in-game target", IsCheckable = true, IsChecked = preferences.OverlayFollowGameTarget,
            ToolTip = "Follow captured target selections. Clearing your target clears the meter filter; without a selected target, no target damage is shown." };
        follow.Click += (_, _) => { preferences.OverlayFollowGameTarget = follow.IsChecked; selectedTarget = 0; selectedTargetIdentity = null; RefreshMeter(); saveSettings?.Invoke(); };
        menu.Items.Add(follow);
        Choice("All targets", false, 0); Choice("Encounter bosses only", true, 0);
        menu.Items.Add(new Separator());
        if (fight != null) foreach (int id in OverlayCombatFilter.Targets(fight)) Choice($"{fight.Name(id)} · {id}", false, id);
        return menu;
    }
    static Color BackAttackColour(string? value)
    {
        Color colour;
        try { colour = (Color)ColorConverter.ConvertFromString(value ?? "#E7CF7D"); }
        catch { colour = Color.FromRgb(231, 207, 125); }
        return Color.FromArgb(136, colour.R, colour.G, colour.B);
    }
    void RefreshBackAttackBorder() => backAttackBorder.Visibility = backAttack.Active(Environment.TickCount64) ? Visibility.Visible : Visibility.Collapsed;
    internal void CheckBackAttackBorder(bool expected)
    {
        if ((backAttackBorder.Visibility == Visibility.Visible) != expected || backAttackBorder.IsHitTestVisible ||
            backAttackBorder.BorderBrush is not SolidColorBrush brush || brush.Color.A is 0 or 255 || brush.Color != BackAttackColour(preferences.BackAttackColor))
            throw new InvalidOperationException("Back-attack border state or transparency is incorrect.");
    }
    internal bool MenuOpen=>mode.ContextMenu?.IsOpen==true;
    internal void CheckLiveDropdown(Action<Fight>? liveRefresh=null)
    {
        var original=fight!;var savedHealing=preferences.OverlayHealing;
        void Pump(int ms=40){var frame=new DispatcherFrame();var wait=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};wait.Tick+=(_,_)=>{wait.Stop();frame.Continue=false;};wait.Start();Dispatcher.PushFrame(frame);}
        try {
            foreach(bool targets in new[]{false,true}){
                if(targets)ShowTargets();else ShowOptions();Pump();
                var menu=mode.ContextMenu!;var first=menu.Items.OfType<MenuItem>().First();first.Focus();Pump();
                var menuPosition=menu.PointToScreen(new Point());
                foreach(var alt in new[]{Key.LeftAlt,Key.RightAlt})foreach(var routed in new[]{Keyboard.PreviewKeyDownEvent,Keyboard.PreviewKeyUpEvent}){
                    var key=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(menu),Environment.TickCount,alt){RoutedEvent=routed};
                    menu.RaiseEvent(key);Pump();
                    if(!key.Handled||!menu.IsOpen)throw new InvalidOperationException("Game cursor Alt key dismissed the DPS menu.");
                }
                for(int i=0;i<8;i++){
                    var incoming=SelfTest.Demo();incoming.Id=original.Id;
                    incoming.Damage.Add(incoming.Damage[0] with {Damage=100000+i,Timestamp=incoming.End+1000});
                    if(liveRefresh!=null)liveRefresh(incoming);else Update(incoming,preferences);Pump();
                    if(!menu.IsOpen||!ReferenceEquals(mode.ContextMenu,menu)||menu.PointToScreen(new Point())!=menuPosition)throw new InvalidOperationException("Live events interrupted the meter dropdown or discarded the newest snapshot.");
                }
                if(!targets){var heal=menu.Items.OfType<MenuItem>().First(i=>i.Header?.ToString()?.StartsWith("Healing")==true);heal.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));}
                if(targets){
                    var escape=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(menu),Environment.TickCount,Key.Escape){RoutedEvent=Keyboard.KeyDownEvent};
                    menu.RaiseEvent(escape);Pump(250);
                    if(menu.IsOpen)throw new InvalidOperationException("Escape no longer dismisses the target menu.");
                }else{menu.IsOpen=false;Pump(250);}
                if(!targets&&(!preferences.OverlayHealing||mode.Content?.ToString()?.Contains("HEALING")!=true))throw new InvalidOperationException("Live dropdown selection did not apply after closing.");
            }
        }finally{if(mode.ContextMenu is {} menu)menu.IsOpen=false;preferences.OverlayHealing=savedHealing;Update(original,preferences);}
    }
    internal void CheckModes()
    {
        bool previousPin = preferences.OverlayPinLocal;
        preferences.OverlayPinLocal = false; RefreshMeter();
        if (ownMeter.RowCount != 0 || meter.RowCount != 10) throw new InvalidOperationException("Default ranking still pins the local row.");
        CreateOptions().Items.OfType<MenuItem>().Single(i => i.Header?.ToString() == "Pin my character first").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        if (!preferences.OverlayPinLocal || ownMeter.RowCount != 1 || meter.RowCount != 9) throw new InvalidOperationException("Pin toggle did not move the local row.");
        preferences.OverlayPinLocal = previousPin; RefreshMeter();
        void CheckRows() { foreach (var bar in ownMeter.Children.OfType<MeterBar>().Concat(meter.Children.OfType<MeterBar>())) bar.CheckPresentation(); }
        CheckRows();
        bool previousCustom = preferences.OverlayCustomBars;
        var colourToggle = CreateOptions().Items.OfType<MenuItem>().Single(i => i.Header?.ToString() == "Class colours");
        colourToggle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        if (preferences.OverlayCustomBars == previousCustom || CreateOptions().Items.OfType<MenuItem>().Single(i => i.Header?.ToString() == "Class colours").IsChecked != !preferences.OverlayCustomBars)
            throw new InvalidOperationException("Class colour toggle did not update the overlay preferences and menu.");
        CheckRows();
        preferences.OverlayCustomBars = previousCustom; RefreshMeter();
        CheckRows();
        bool previous = preferences.OverlayHealing;
        preferences.OverlayHealing = true; RefreshMeter();
        if (RowCount != 10 || ownMeter.RowCount != 1) throw new InvalidOperationException("Healing overlay did not retain all ten party members and the local row.");
        preferences.OverlayHealing = previous; RefreshMeter();
        if (RowCount != 10) throw new InvalidOperationException("Damage overlay did not retain ten party members.");
    }
    internal void CheckPinnedScrolling()
    {
        foreach (var bar in ownMeter.Children.OfType<MeterBar>().Concat(meter.Children.OfType<MeterBar>())) bar.CheckPresentation();
        var before = ownMeter.TranslatePoint(new Point(), this);
        actorScroll.ScrollToEnd(); UpdateLayout();
        var after = ownMeter.TranslatePoint(new Point(), this);
        if (ownMeter.RowCount != 1 || before != after || after.Y < 0 || after.Y + ownMeter.ActualHeight > ActualHeight)
            throw new InvalidOperationException("Local player row moved out of view when scrolling.");
    }
    internal void CheckSkillMode(Action<FrameworkElement, string> render)
    {
        if (fight == null) throw new InvalidOperationException("Skill overlay test needs a fight.");
        var original = fight; bool previous = preferences.OverlayShowSkills;
        try {
            var sample = FeatureTests.Sample(); sample.Demo = true;
            sample.Label = "Training guardian";
            sample.Actors[1] = sample.Actors[1] with { Name = "Your character" };
            sample.Damage[0] = sample.Damage[0] with { Damage = 84000 };
            sample.Damage[1] = sample.Damage[1] with { Damage = 52000 };
            sample.Damage.Add(new() { ActorId = 1, TargetId = 100, Timestamp = 12000, Damage = 31000, SkillCode = 17030000 });
            sample.Damage.Add(new() { ActorId = 1, TargetId = 100, Timestamp = 18000, Damage = 12000, SkillCode = 17040000 });
            Update(sample, preferences);
            preferences.OverlayShowSkills = false; RefreshMeter();
            CreateOptions().Items.OfType<MenuItem>().Single(i => i.Header?.ToString() == "Show my skills").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            if (!preferences.OverlayShowSkills || actorScroll.Content != skillBreakdown || ownMeter.Visibility != Visibility.Visible || ownMeter.RowCount != 1 || skillBreakdown.RowCount != 4)
                throw new InvalidOperationException("Dropdown did not switch to the local skill breakdown.");
            var retained = skillBreakdown.Children[0]; RefreshMeter();
            if (!ReferenceEquals(retained, skillBreakdown.Children[0])) throw new InvalidOperationException("Idle skill overlay rebuilt its rows.");
            UpdateLayout(); render(this, "test-artifacts/Overlay-my-skills.png");
            CreateTargetsMenu().Items.OfType<MenuItem>().Single(i => i.Header?.ToString()?.EndsWith(" · 100", StringComparison.Ordinal) == true).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            if (selectedTarget != 100 || skillBreakdown.RowCount != 4)
                throw new InvalidOperationException("Target menu did not filter the skill view.");
            var cached = filteredFight; RefreshMeter();
            if (!ReferenceEquals(cached, filteredFight)) throw new InvalidOperationException("Idle target filter rebuilt its snapshot.");
            UpdateLayout(); render(this, "test-artifacts/Overlay-selected-target.png");
            var healthSample = sample.Copy();
            healthSample.Network.Add(new("hp", 31000, "", System.Text.Json.JsonSerializer.SerializeToElement(new { EntityId = 1, CurrentHp = 12500 })));
            preferences.OverlayShowSkills = false; Update(healthSample, preferences);
            foreach (var bar in ownMeter.Children.OfType<MeterBar>().Concat(meter.Children.OfType<MeterBar>())) bar.CheckPresentation();
            UpdateLayout(); render(this, "test-artifacts/Overlay-health.png");
            bool previousHealth = preferences.OverlayShowTargetHealth;
            try {
                preferences.OverlayShowTargetHealth = true;
                var guardSample = healthSample.Copy();
                guardSample.Network.Add(new("guard", 31000, "test", System.Text.Json.JsonSerializer.SerializeToElement(
                    new GuardReading(100, 1, 1000, 250, 0, 0, 0, false, 0, true))));
                Update(guardSample, preferences);
                if (targetGuard.Visibility != Visibility.Visible || targetGuardBar.ColumnDefinitions[0].Width.Value != .25 || !targetGuardText.Text.Contains("250"))
                    throw new InvalidOperationException("Selected target stagger bar missing.");
                UpdateLayout(); render(this, "test-artifacts/Overlay-stagger.png");
                var brokenSample = guardSample.Copy();
                brokenSample.Network.Add(new("guard", 32000, "test", System.Text.Json.JsonSerializer.SerializeToElement(
                    new GuardReading(100, 3, 1000, 0, 0, 0, 0, false, 0, true))));
                Update(brokenSample, preferences);
                if (targetGuardBar.ColumnDefinitions[0].Width.Value != 0 || !targetGuardText.Text.Contains("Broken")) throw new InvalidOperationException("Broken stagger state missing.");
                UpdateLayout(); render(this, "test-artifacts/Overlay-stagger-broken.png");
            } finally { preferences.OverlayShowTargetHealth = previousHealth; }
            var revivedSample = healthSample.Copy();
            revivedSample.Network.Add(new("entity-revived", 32000, "", System.Text.Json.JsonSerializer.SerializeToElement(new { EntityId = 1 })));
            Update(revivedSample, preferences);
            if (health.GetValueOrDefault(1)?.Label != "Revived (HP not captured)") throw new InvalidOperationException("Revival label missing from overlay health state.");
            UpdateLayout(); render(this, "test-artifacts/Overlay-revived.png");
            var trainingSample = sample.Copy();
            trainingSample.Training = new(sample.Start, sample.Start + 60000, 60, "Synthetic group", "Synthetic notes");
            bool previousHealing = preferences.OverlayHealing, previousBosses = preferences.OverlayBossesOnly;
            string previousScope = preferences.OverlayScope;
            try {
                preferences.OverlayHealing = true; preferences.OverlayBossesOnly = true; preferences.OverlayScope = "PvP";
                TrainingLabel = "Training 00:30 left · stop";
                Update(trainingSample, preferences);
                if (skillBreakdown.RowCount != 4 || duration.Text != "01:00" || actorScroll.Content != skillBreakdown ||
                    !preferences.OverlayHealing || !preferences.OverlayBossesOnly || preferences.OverlayScope != "PvP")
                    throw new InvalidOperationException("Training overlay did not show local damage without changing ordinary meter settings.");
                UpdateLayout(); render(this, "test-artifacts/Overlay-training.png");
            } finally {
                preferences.OverlayHealing = previousHealing; preferences.OverlayBossesOnly = previousBosses;
                preferences.OverlayScope = previousScope; TrainingLabel = "Training…";
            }
            preferences.OverlayShowSkills = true;
            Update(new Fight(), preferences);
            if (skillBreakdown.RowCount != 0 || selectedTarget != 0) throw new InvalidOperationException("Reset retained stale personal skills or target selection.");
        } finally { preferences.OverlayShowSkills = previous; Update(original, preferences); }
    }
}
