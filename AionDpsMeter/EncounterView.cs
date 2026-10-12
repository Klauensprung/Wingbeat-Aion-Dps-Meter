using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace AionDpsMeter.Local;

internal sealed class EncounterView : Grid
{
    sealed class ClassIconConverter : IValueConverter
    {
        readonly Dictionary<string, DrawingImage> images = new(StringComparer.OrdinalIgnoreCase);
        public object? Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is not string job || !ClassEvidence.Known(job)) return null;
            if (!images.TryGetValue(job, out var image)) {
                var pen = new Pen(UiTheme.Brush(MeterPresentation.ClassColorFor(job, "#93A2B8")), 1.5) { LineJoin = PenLineJoin.Round };
                image = new DrawingImage(new GeometryDrawing(null, pen, ClassSymbols.For(job)));
                image.Freeze(); images[job] = image;
            }
            return image;
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
    }
    sealed record Entry(string Id, string Encounter, string Detail);
    readonly LiveTable encounters = Table(), participants = Table(), skills = Table();
    readonly TextBlock heading = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock subtitle = new() { FontSize = 11, Margin = new(0, 5, 0, 10), Foreground = UiTheme.Brush("#93A2B8") };
    readonly TextBlock skillHeading = new() { Text = "Skill damage - all participants", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock empty = new() { Text = "Start capture to record an encounter, or select one from your saved history.", TextWrapping = TextWrapping.Wrap, Margin = new(8), Foreground = UiTheme.Brush("#93A2B8") };
    readonly Func<int, string> skillName;
    Dictionary<string, Fight> choices = new();
    Fight? selected;
    EncounterParticipant[] currentPeople = [];
    string liveId = "", search = "";
    int? participantId;
    bool updating;
    public event Action<Fight?>? Selected;
    internal EncounterView(Func<int, string> skillName, SkillIcons icons)
    {
        this.skillName = skillName;
        Height = 530; Margin = new(8, 0, 8, 8);
        ColumnDefinitions.Add(new() { Width = new GridLength(225) }); ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var browser = new DockPanel { Margin = new(0, 0, 14, 0) };
        var listTitle = new TextBlock { Text = "Encounters", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 4) };
        DockPanel.SetDock(listTitle, Dock.Top); browser.Children.Add(listTitle);
        var listHint = new TextBlock { Text = "Current first, then newest saved", FontSize = 11, Foreground = UiTheme.Brush("#93A2B8"), Margin = new(0, 0, 0, 10) };
        DockPanel.SetDock(listHint, Dock.Top); browser.Children.Add(listHint);
        encounters.HeadersVisibility = DataGridHeadersVisibility.None; encounters.RowHeight = 62;
        var item = new FrameworkElementFactory(typeof(StackPanel)); item.SetValue(MarginProperty, new Thickness(7, 5, 7, 5));
        var name = new FrameworkElementFactory(typeof(TextBlock)); name.SetBinding(TextBlock.TextProperty, new Binding("Encounter")); name.SetBinding(ToolTipProperty, new Binding("Encounter"));
        name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); item.AppendChild(name);
        var detail = new FrameworkElementFactory(typeof(TextBlock)); detail.SetBinding(TextBlock.TextProperty, new Binding("Detail")); detail.SetValue(TextBlock.FontSizeProperty, 10d);
        detail.SetValue(TextBlock.ForegroundProperty, UiTheme.Brush("#93A2B8")); detail.SetValue(MarginProperty, new Thickness(0, 5, 0, 0)); item.AppendChild(detail);
        encounters.Columns.Add(new DataGridTemplateColumn { CellTemplate = new DataTemplate { VisualTree = item }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        browser.Children.Add(encounters); Children.Add(browser);
        var body = new Grid(); SetColumn(body, 1); Children.Add(body);
        body.RowDefinitions.Add(new() { Height = GridLength.Auto }); body.RowDefinitions.Add(new() { Height = new GridLength(220) });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto }); body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var title = new StackPanel(); title.Children.Add(heading); title.Children.Add(subtitle); title.Children.Add(empty); body.Children.Add(title);
        var participantPanel = new DockPanel(); SetRow(participantPanel, 1); body.Children.Add(participantPanel);
        var participantTitle = new TextBlock { Text = "Players - highest DPS first", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 6) };
        DockPanel.SetDock(participantTitle, Dock.Top); participantPanel.Children.Add(participantTitle); participantPanel.Children.Add(participants);
        var player = new FrameworkElementFactory(typeof(DockPanel));
        var classIcon = new FrameworkElementFactory(typeof(Image));
        classIcon.SetValue(WidthProperty, 16d); classIcon.SetValue(HeightProperty, 16d);
        classIcon.SetValue(MarginProperty, new Thickness(0, 0, 7, 0)); classIcon.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        classIcon.SetBinding(Image.SourceProperty, new Binding(nameof(EncounterParticipant.Class)) { Converter = new ClassIconConverter() });
        classIcon.SetBinding(ToolTipProperty, new Binding(nameof(EncounterParticipant.Class)));
        var iconStyle = new Style(typeof(Image));
        var unknown = new Trigger { Property = Image.SourceProperty, Value = null };
        unknown.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed)); iconStyle.Triggers.Add(unknown);
        classIcon.SetValue(StyleProperty, iconStyle); player.AppendChild(classIcon);
        var playerName = new FrameworkElementFactory(typeof(TextBlock));
        playerName.SetBinding(TextBlock.TextProperty, new Binding(nameof(EncounterParticipant.Participant)));
        playerName.SetBinding(ToolTipProperty, new Binding(nameof(EncounterParticipant.Participant)));
        playerName.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center); playerName.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        player.AppendChild(playerName);
        participants.Columns.Add(new DataGridTemplateColumn { Header = "Participant", SortMemberPath = nameof(EncounterParticipant.Participant), CellTemplate = new DataTemplate { VisualTree = player }, Width = 140 });
        TextColumn(participants, "Class", "Class", 90);
        TextColumn(participants, "Damage", "Damage", 85, "N0"); TextColumn(participants, "DPS", "DPS", 75, "N1");
        TextColumn(participants, "Healing", "Healing", 85, "N0"); TextColumn(participants, "HPS", "HPS", 75, "N1"); TextColumn(participants, "Deaths", "Deaths", 70, "N0");
        var skillTitle = new DockPanel { Margin = new(0, 14, 0, 7) }; SetRow(skillTitle, 2); body.Children.Add(skillTitle);
        var all = new Button { Content = "All participants", Padding = new(9, 4, 9, 4), Margin = new(8, 0, 0, 0) };
        DockPanel.SetDock(all, Dock.Right); skillTitle.Children.Add(all); skillTitle.Children.Add(skillHeading);
        all.Click += (_, _) => { participantId = null; updating = true; participants.SelectedItem = null; updating = false; RefreshSkills(); };
        SetRow(skills, 3); body.Children.Add(skills);
        TextColumn(skills, "Participant", "Participant", 130);
        var skill = new FrameworkElementFactory(typeof(DockPanel));
        var image = new FrameworkElementFactory(typeof(Image)); image.SetValue(WidthProperty, 24d); image.SetValue(HeightProperty, 24d); image.SetValue(MarginProperty, new Thickness(0, 0, 7, 0));
        image.SetBinding(Image.SourceProperty, new Binding("SkillCode") { Converter = icons }); skill.AppendChild(image);
        var label = new FrameworkElementFactory(typeof(TextBlock)); label.SetBinding(TextBlock.TextProperty, new Binding("Skill")); label.SetBinding(ToolTipProperty, new Binding("Skill"));
        label.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center); label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); skill.AppendChild(label);
        skills.Columns.Add(new DataGridTemplateColumn { Header = "Skill", CellTemplate = new DataTemplate { VisualTree = skill }, Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 170 });
        TextColumn(skills, "Hits", "Hits", 50, "N0"); TextColumn(skills, "Damage", "Damage", 85, "N0"); TextColumn(skills, "DPS", "DPS", 75, "N1");
        TextColumn(skills, "DamageShare", "Share %", 80, "0.0"); TextColumn(skills, "CritPercent", "Crit %", 65, "0.0");
        SkillDamageDisplay.Apply(participants, "#55D6BE", nameof(EncounterParticipant.DamagePercentOfLeader));
        SkillDamageDisplay.Apply(skills, "#55D6BE", nameof(EncounterSkill.DamageShare));
        encounters.SelectionChanged += (_, _) => { if (!updating && encounters.SelectedItem is Entry entry && choices.TryGetValue(entry.Id, out var fight)) Selected?.Invoke(entry.Id == liveId ? null : fight); };
        participants.SelectionChanged += (_, _) => { if (!updating) { participantId = (participants.SelectedItem as EncounterParticipant)?.Id; RefreshSkills(); } };
    }
    static LiveTable Table() => new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Single,
        EnableRowVirtualization = true, EnableColumnVirtualization = true, HeadersVisibility = DataGridHeadersVisibility.Column, RowHeight = 34, GridLinesVisibility = DataGridGridLinesVisibility.None, BorderThickness = new(0) };
    static void TextColumn(DataGrid grid, string property, string header, double width, string? format = null)
    {
        var style = new Style(typeof(TextBlock)); style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        style.Setters.Add(new Setter(VerticalAlignmentProperty, VerticalAlignment.Center)); style.Setters.Add(new Setter(ToolTipProperty, new Binding(property)));
        if (format != null) style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(property) { StringFormat = format }, Width = width, ElementStyle = style });
    }
    internal void Update(Fight current, Fight live, IEnumerable<Fight> archive, bool capturing, string searchText)
    {
        updating = true;
        try {
            if (selected?.Id != current.Id) participantId = null;
            selected = current; liveId = live.Id; search = searchText;
            choices = new[] { live, current }.Concat(archive).DistinctBy(f => f.Id).ToDictionary(f => f.Id);
            var entries = choices.Values.Where(f => f.Id == current.Id || f.Id == live.Id || string.IsNullOrWhiteSpace(search) || EncounterOverview.Title(f).Contains(search, StringComparison.OrdinalIgnoreCase) || f.Actors.Values.Any(a => a.Name.Contains(search, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(f => f.Id == live.Id).ThenByDescending(f => f.Created).ThenBy(f => f.Id)
                .Select(f => new Entry(f.Id, EncounterOverview.Title(f), (f.Id == live.Id ? capturing ? "LIVE" : "CURRENT" : f.Demo ? "DEMO" : "SAVED") + "  |  " + f.Created.ToLocalTime().ToString("dd MMM HH:mm") + "  |  " + MeterPresentation.Duration(f))).ToArray();
            encounters.SetRows(entries); encounters.SelectedItem = encounters.Items.Cast<Entry>().FirstOrDefault(e => e.Id == current.Id);
            heading.Text = EncounterOverview.Title(current); heading.ToolTip = heading.Text;
            var people = currentPeople = EncounterOverview.Participants(current);
            subtitle.Text = $"{people.Length} players  |  {MeterPresentation.Duration(current)}  |  {people.Sum(p => p.Damage):N0} damage  |  {people.Sum(p => p.Healing):N0} healing";
            empty.Visibility = current.HasEvents ? Visibility.Collapsed : Visibility.Visible;
            participants.SetRows(people);
            if (participantId != null && people.All(p => p.Id != participantId)) participantId = null;
            participants.SelectedItem = participantId == null ? null : participants.Items.Cast<EncounterParticipant>().FirstOrDefault(p => p.Id == participantId);
            RefreshSkills();
        } finally { updating = false; }
    }
    void RefreshSkills()
    {
        if (selected == null) return;
        var people = currentPeople.Where(p => participantId == null || p.Id == participantId).ToArray();
        skills.SetRows(EncounterOverview.Skills(selected, people.Select(p => p.Id), skillName));
        skillHeading.Text = participantId == null ? "Skill damage - all participants" : "Skill damage - " + selected.Name(participantId.Value);
    }
    internal void CheckPresentation(Fight fight)
    {
        if (participants.Items.Count != EncounterOverview.Participants(fight).Length || skills.Items.Count == 0) throw new InvalidOperationException("Encounter overview did not render participants and skills.");
        participants.SelectedIndex = 0;
        int actor = ((EncounterParticipant)participants.SelectedItem).Id;
        if (skills.Items.Cast<EncounterSkill>().Any(s => s.ActorId != actor)) throw new InvalidOperationException("Encounter participant selection did not filter skills.");
        var live = choices[liveId]; var archive = choices.Values.ToArray();
        skills.SelectedIndex = 0;
        int skillCode = ((EncounterSkill)skills.SelectedItem).SkillCode;
        for (int tick = 1; tick <= 20; tick++) {
            var changed = fight.Copy();
            changed.Damage.Add(new() { ActorId = actor, SkillCode = skillCode, Damage = tick * 100000, Timestamp = fight.Damage.Max(h => h.Timestamp) + tick * 1000 });
            Update(changed, live.Id == changed.Id ? changed : live, archive, false, search);
            UpdateLayout();
            if (participantId != actor || participants.SelectedItem is not EncounterParticipant person || person.Id != actor ||
                skills.Items.Cast<EncounterSkill>().Any(s => s.ActorId != actor) || skills.SelectedItem is not EncounterSkill chosen || chosen.SkillCode != skillCode ||
                encounters.SelectedItem is not Entry entry || entry.Id != changed.Id)
                throw new InvalidOperationException("Changing live encounter values lost the encounter, participant or skill selection.");
        }
        Update(fight, live, archive, false, search);
        participantId = null; participants.SelectedItem = null; RefreshSkills();
        if (skills.Items.Count != EncounterOverview.Skills(fight, EncounterOverview.Participants(fight).Select(p => p.Id), skillName).Length)
            throw new InvalidOperationException("All-participant skill breakdown did not restore all rows.");
        UpdateLayout();
    }
}
