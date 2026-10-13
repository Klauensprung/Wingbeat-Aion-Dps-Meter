using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace AionDpsMeter.Local;

internal sealed class BossAnalysisView : Grid
{
    readonly ComboBox bosses = new() { DisplayMemberPath = nameof(BossEncounterData.Label), MinWidth = 280, MaxWidth = 520, Margin = new(0, 0, 8, 0) };
    readonly TextBlock identity = new() { TextWrapping = TextWrapping.Wrap, Foreground = BossDrawing.Muted, Margin = new(0, 5, 0, 7), FontSize = 11 };
    readonly TextBlock empty = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 20) };
    readonly PageLayout body = new();
    readonly TextBlock duration = new(), dealt = new(), received = new(), casts = new(), coverage = new() { TextWrapping = TextWrapping.Wrap, Foreground = BossDrawing.Muted, Margin = new(0, 6, 0, 6), FontSize = 11 };
    readonly LiveTable abilities = Table(), participants = Table();
    readonly LiveTable targets = Table();
    readonly DockPanel targetPanel = new();
    readonly ComboBox detailChoice = new() { ItemsSource = new[] { "Boss skills", "Skill targets", "Participants", "Damage & HP" }, SelectedIndex = 0, Width = 155, Margin = new(0, 0, 10, 5) };
    readonly TextBlock skillDetail = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8), Foreground = BossDrawing.Muted };
    readonly BossTrendPlot trend = new();
    readonly BossTimelineView timeline;
    readonly Func<int, string> skillName;
    readonly Button catalogue;
    Fight? source;
    BossEncounterData[] data = [];
    int? selectedId;
    string query = "";
    bool updating;
    internal int BossCount => data.Length;
    internal BossEncounterData? SelectedBoss => data.FirstOrDefault(b => b.EntityId == selectedId);
    internal event Action<string>? BrowseCatalogue;

    internal BossAnalysisView(Func<int, string> skillName, SkillIcons icons)
    {
        this.skillName = skillName; timeline = new(skillName); Margin = new(8); SetValue(TextBlock.ForegroundProperty, BossDrawing.Text);
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var chooser = new WrapPanel(); chooser.Children.Add(bosses);
        catalogue = Action(UiIcon.Folder, "Catalogue entry", () => { if (SelectedBoss is { NpcId: > 0 } b) BrowseCatalogue?.Invoke(b.NpcId.ToString()); }); chooser.Children.Add(catalogue);
        Children.Add(chooser); SetRow(identity, 1); Children.Add(identity); SetRow(empty, 2); Children.Add(empty); SetRow(body, 3); Children.Add(body);
        var metrics = new Grid(); for (int i = 0; i < 4; i++) metrics.ColumnDefinitions.Add(new());
        Metric(metrics, 0, "OBSERVED WINDOW", duration); Metric(metrics, 1, "DAMAGE TO BOSS", dealt);
        Metric(metrics, 2, "DAMAGE FROM BOSS", received); Metric(metrics, 3, "CAST SIGNALS", casts);
        body.Children.Add(metrics); body.Children.Add(coverage);
        var all = Action(UiIcon.Refresh, "All skills", () => { updating = true; abilities.SelectedItem = null; updating = false; timeline.Filter(null); UpdateTargets(); });
        var chart = new DockPanel();
        var chartHint = Hint("Separate damage scales; HP is the captured value, not an inferred percentage or phase.");
        DockPanel.SetDock(chartHint, Dock.Bottom); chart.Children.Add(chartHint); chart.Children.Add(trend);
        var template = new FrameworkElementFactory(typeof(DockPanel));
        var image = new FrameworkElementFactory(typeof(Image)); image.SetValue(WidthProperty, 23d); image.SetValue(HeightProperty, 23d); image.SetValue(MarginProperty, new Thickness(0, 0, 8, 0));
        image.SetBinding(Image.SourceProperty, new Binding(nameof(BossAbility.SkillCode)) { Converter = icons }); template.AppendChild(image);
        var label = new FrameworkElementFactory(typeof(TextBlock)); label.SetBinding(TextBlock.TextProperty, new Binding(nameof(BossAbility.Skill))); label.SetBinding(ToolTipProperty, new Binding(nameof(BossAbility.Skill)));
        label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); label.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center); template.AppendChild(label);
        abilities.Columns.Add(new DataGridTemplateColumn { Header = "Skill", CellTemplate = new DataTemplate { VisualTree = template }, Width = 205, SortMemberPath = nameof(BossAbility.Skill) });
        Column(abilities, "SkillCode", "Skill ID", 85); Column(abilities, "Casts", "Casts", 60, "N0"); Column(abilities, "Hits", "Hits", 60, "N0");
        Column(abilities, "Damage", "Damage", 100, "N0"); Column(abilities, "DamageShare", "Share %", 75, "0.0");
        Column(abilities, "CastGap", "Cast gap (s)", 95, "0.0"); Column(abilities, "Targets", "Targets", 65, "N0"); Column(abilities, "BiggestHit", "Largest hit", 90, "N0"); Column(abilities, "AverageHit", "Average hit", 95, "N1");
        Column(abilities, "Blocks", "Blocks", 70, "N0"); Column(abilities, "Parries", "Parries", 70, "N0"); Column(abilities, "Avoids", "Avoids", 70, "N0");
        SkillDamageDisplay.Apply(abilities, "#F18B91", nameof(BossAbility.DamageShare));
        DockPanel.SetDock(skillDetail, Dock.Top); targetPanel.Children.Add(skillDetail); targetPanel.Children.Add(targets);
        Column(targets, "Target", "Target", 190); Column(targets, "EntityId", "Entity ID", 90); Column(targets, "Hits", "Hits", 70, "N0");
        Column(targets, "Damage", "Damage taken", 115, "N0"); Column(targets, "Share", "Skill share %", 95, "0.0"); Column(targets, "BiggestHit", "Largest hit", 100, "N0");
        SkillDamageDisplay.Apply(targets, "#F18B91", nameof(BossAbilityTarget.Share));
        var player = new FrameworkElementFactory(typeof(DockPanel)); var classImage = new FrameworkElementFactory(typeof(Image));
        classImage.SetValue(WidthProperty, 16d); classImage.SetValue(HeightProperty, 16d); classImage.SetValue(MarginProperty, new Thickness(0, 0, 7, 0));
        classImage.SetBinding(Image.SourceProperty, new Binding(nameof(BossParticipant.Class)) { Converter = new EncounterView.ClassIconConverter() }); player.AppendChild(classImage);
        var name = new FrameworkElementFactory(typeof(TextBlock)); name.SetBinding(TextBlock.TextProperty, new Binding(nameof(BossParticipant.Player))); name.SetBinding(ToolTipProperty, new Binding(nameof(BossParticipant.Player)));
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); name.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center); player.AppendChild(name);
        participants.Columns.Add(new DataGridTemplateColumn { Header = "Participant", CellTemplate = new DataTemplate { VisualTree = player }, Width = 180, SortMemberPath = nameof(BossParticipant.Player) });
        Column(participants, "Id", "Entity ID", 85); Column(participants, "Damage", "To boss", 100, "N0"); Column(participants, "DPS", "Boss DPS", 95, "N1");
        Column(participants, "Share", "Share %", 80, "0.0"); Column(participants, "Taken", "Taken", 100, "N0"); Column(participants, "HitsTaken", "Hits taken", 80, "N0"); Column(participants, "Deaths", "Deaths in window", 125, "N0");
        SkillDamageDisplay.Apply(participants, "#55D6BE", nameof(BossParticipant.Share));
        var lower = new PageLayout(); var detailBar = new WrapPanel { Margin = new(0, 6, 0, 0) }; detailBar.Children.Add(detailChoice);
        detailBar.Children.Add(all);
        detailBar.Children.Add(Action(UiIcon.Meter, "Selected skill targets", () => detailChoice.SelectedIndex = 1)); lower.Children.Add(detailBar);
        var detailHint = Hint("Select a skill to focus its timeline. Cast gap is an observed interval, not a cooldown."); lower.Children.Add(detailHint);
        var detailContent = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, Content = abilities };
        lower.Children.Add(PageLayout.Fill(detailContent));
        var targetViewport = new ViewportHost(targetPanel, 145);
        var chartViewport = new ViewportHost(chart, 195);
        detailChoice.SelectionChanged += (_, _) => {
            detailContent.Content = detailChoice.SelectedIndex switch { 1 => targetViewport, 2 => participants, 3 => chartViewport, _ => abilities };
            detailHint.Text = detailChoice.SelectedIndex switch {
                1 => "Damage from the selected boss skill, broken down by target.",
                2 => "Boss-only DPS. Deaths occurred within the boss window; they are not confirmed boss kills.",
                3 => "Hover the chart for exact time-bin totals and the latest earlier HP reading.",
                _ => "Select a skill to focus its timeline. Cast gap is an observed interval, not a cooldown." };
        };
        body.Children.Add(PageLayout.Fill(new SplitView(timeline, lower, .55, 210, 145)));
        bosses.SelectionChanged += (_, _) => { if (!updating) { selectedId = (bosses.SelectedItem as BossEncounterData)?.EntityId; ShowBoss(); } };
        abilities.SelectionChanged += (_, _) => { if (!updating) { timeline.Filter((abilities.SelectedItem as BossAbility)?.SkillCode); UpdateTargets(); } };
    }
    static TextBlock Hint(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = BossDrawing.Muted, FontSize = 11, Margin = new(0, 0, 0, 10) };
    static Button Action(UiIcon icon, string text, Action action) { var b = new Button { Padding = new(10, 7, 10, 7), Margin = new(0, 0, 6, 0) }; UiIcons.SetLabel(b, icon, text); b.Click += (_, _) => action(); return b; }
    static LiveTable Table() => new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false,
        SelectionMode = DataGridSelectionMode.Single, EnableRowVirtualization = true, EnableColumnVirtualization = true, RowHeight = 35,
        MinHeight = 60, Margin = new(0, 0, 0, 4), HeadersVisibility = DataGridHeadersVisibility.Column };
    static void Column(DataGrid grid, string property, string header, double width, string? format = null)
    {
        var style = new Style(typeof(TextBlock)); style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        style.Setters.Add(new Setter(VerticalAlignmentProperty, VerticalAlignment.Center)); style.Setters.Add(new Setter(ToolTipProperty, new Binding(property)));
        if (format != null) style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(property) { StringFormat = format, TargetNullValue = "—" }, Width = width, ElementStyle = style });
    }
    static void Metric(Grid grid, int index, string label, TextBlock value)
    {
        var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = label, Foreground = BossDrawing.Muted, FontSize = 10 });
        value.FontSize = 19; value.FontWeight = FontWeights.SemiBold; value.Margin = new(0, 3, 0, 0); panel.Children.Add(value);
        var card = new Border { Child = panel, Background = BossDrawing.Alternate, CornerRadius = new(7), Padding = new(10, 7, 10, 7), Margin = new(0, 0, 7, 0) };
        Grid.SetColumn(card, index); grid.Children.Add(card);
    }
    internal void Update(Fight fight, string search, bool force = false)
    {
        if (!force && ReferenceEquals(source, fight) && query == search) return;
        bool changed = source?.Id != fight.Id;
        if (force || !ReferenceEquals(source, fight)) data = BossEncounterAnalysis.Build(fight, skillName);
        source = fight; query = search;
        var choices = data.Where(b => search.Length == 0 || new[] { b.Name, b.DisplayEntityId.ToString(), b.NpcId.ToString(), b.Location }.Any(s => s.Contains(search, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (changed || choices.All(b => b.EntityId != selectedId)) selectedId = choices.FirstOrDefault()?.EntityId;
        updating = true; bosses.ItemsSource = choices; bosses.SelectedItem = choices.FirstOrDefault(b => b.EntityId == selectedId); updating = false;
        empty.Text = data.Length == 0 ? "No boss identified in this encounter yet. Select a saved boss fight, or capture an encounter with an NPC template or boss-cast signal." : "No bosses match this search.";
        empty.Visibility = choices.Length == 0 ? Visibility.Visible : Visibility.Collapsed; body.Visibility = choices.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowBoss();
    }
    void ShowBoss()
    {
        var boss = SelectedBoss; catalogue.IsEnabled = boss?.NpcId > 0; if (boss == null || source == null) { identity.Text = ""; return; }
        identity.Text = $"Entity {boss.DisplayEntityId}{(boss.OriginalEntityId > 0 ? " (earlier identity)" : "")} / NPC {(boss.NpcId > 0 ? boss.NpcId.ToString() : "unknown")} / {(boss.DeathObserved ? "Death observed" : "No boss death recorded")}\n{boss.Identification}. {boss.Location}";
        duration.Text = BossDrawing.Time(boss.Seconds); dealt.Text = MeterPresentation.Number(boss.DamageToBoss); received.Text = MeterPresentation.Number(boss.DamageFromBoss); casts.Text = boss.Casts.ToString("N0");
        coverage.Text = $"{boss.Abilities.Length} observed skills / {boss.Participants.Length} participants / {boss.Hp.Length} HP readings. " +
            (boss.AmbiguousNetworkIdentity ? "Entity ID was reused: ambiguous cast, HP and enrage packets are omitted; attributed hits remain available." :
             boss.Casts == 0 ? "No cast signals captured; the rotation below shows impacts and outcomes only." : "Rotation shows captured signals and impacts; missing actions and unconfirmed casts remain unknown.");
        bool switched = shownEntity != boss.EntityId || shownFight != source.Id; shownEntity = boss.EntityId; shownFight = source.Id;
        updating = true; if (switched) abilities.SelectedItem = null; abilities.SetRows(boss.Abilities); participants.SetRows(boss.Participants); updating = false;
        timeline.Update(boss, source.Name); trend.Update(boss); UpdateTargets();
    }
    void UpdateTargets()
    {
        if (abilities.SelectedItem is not BossAbility ability || SelectedBoss is not { } boss) { skillDetail.Text = "Select an ability in Boss skills to inspect its targets."; targets.SetRows(Array.Empty<BossAbilityTarget>()); return; }
        skillDetail.Text = $"{ability.Skill} [{ability.SkillCode}] / {ability.Casts} cast signals / {ability.Hits} hits / {ability.BiggestHit:N0} largest hit\n" +
            (ability.CastGap is double gap ? $"Median observed cast gap: {gap:N1}s. " : "Not enough cast signals for an interval. ") +
            $"{ability.Blocks} blocks / {ability.Parries} parries / {ability.Avoids} avoids.";
        targets.SetRows(boss.Targets.Where(t => t.SkillCode == ability.SkillCode));
    }
    int? shownEntity;
    string? shownFight;
    internal void CheckResponsiveDetails(Action layout)
    {
        abilities.SelectedIndex = 0;
        var split = body.Children.OfType<SplitView>().Single();
        var content = ((PageLayout)split.Lower).Children.OfType<ContentControl>().Single();
        for (int i = 0; i < detailChoice.Items.Count; i++) {
            detailChoice.SelectedIndex = i; layout();
            var shown = (FrameworkElement)content.Content;
            if (!shown.IsVisible || shown.ActualHeight < 60 || Math.Abs(shown.ActualHeight + shown.Margin.Top + shown.Margin.Bottom - content.ActualHeight) > 2)
                throw new Exception($"Boss detail {i} does not fill its resized panel.");
        }
        detailChoice.SelectedIndex = 0; layout();
    }
    internal void CheckPresentation(Fight fixture)
    {
        Update(fixture, ""); UpdateLayout();
        if (BossCount != 2 || SelectedBoss?.EntityId != 100 || abilities.Items.Count < 3 || participants.Items.Count != 2) throw new Exception("Boss detail sections did not populate.");
        abilities.SelectedIndex = 0; var chosen = (BossAbility)abilities.SelectedItem;
        if (targets.Items.Count != 2) throw new Exception("Selecting a boss skill did not show its affected targets.");
        timeline.Step(1); if (timeline.Selected == null) throw new Exception("Boss timeline cannot step to a recorded action.");
        void PaintTimeline() { UpdateLayout(); var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)timeline.Plot.ActualWidth, (int)timeline.Plot.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(timeline.Plot); }
        PaintTimeline(); int scenes = timeline.Plot.SceneBuilds;
        timeline.Step(1); PaintTimeline();
        if (scenes == 0 || timeline.Plot.SceneBuilds != scenes) throw new Exception("Stepping boss events rebuilt the retained timeline drawing.");
        if (timeline.Plot.At(new(10, 40)) != null || timeline.Plot.At(new(BossTimelinePlot.LabelWidth + 1, 40)) == null)
            throw new Exception("Boss timeline hit testing requires a precise marker hit or includes the label column.");
        timeline.Zoom(.1, .5); if (timeline.Span >= SelectedBoss.Seconds) throw new Exception("Boss timeline did not zoom.");
        var copy = fixture.Copy(); copy.Damage.Add(new() { ActorId = 1, TargetId = 100, Timestamp = 130000, SkillCode = 17010000, Damage = 1 });
        Update(copy, ""); UpdateLayout();
        if (abilities.SelectedItem is not BossAbility selected || selected.SkillCode != chosen.SkillCode || SelectedBoss?.EntityId != 100 || timeline.Selected == null)
            throw new Exception("Boss refresh lost a selected skill, boss or pinned event.");
        bosses.SelectedIndex = 1; if (SelectedBoss?.EntityId != 101 || timeline.Selected != null) throw new Exception("Switching boss did not clear the previous rotation selection.");
        Update(fixture, "no matching boss"); if (body.Visibility != Visibility.Collapsed) throw new Exception("Empty boss search retained stale details.");
        Update(fixture, ""); timeline.Filter(null); timeline.Resume();
        if (timeline.Span != SelectedBoss!.Seconds) throw new Exception("Changing bosses did not restore whole-window zoom.");
    }
}
