using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace AionDpsMeter.Local;

internal sealed class EncounterView : Grid
{
    internal sealed class ClassIconConverter : IValueConverter
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
    readonly LiveTable participants = Table(), skills = Table();
    readonly ComboBox encounters=new(){MinWidth=280,MaxWidth=480,HorizontalAlignment=HorizontalAlignment.Left,Tag="Choose encounter"};
    Entry[] lastEntries=[];
    readonly TextBlock historyCount = new() { FontSize = 11, Foreground = UiTheme.Brush("#93A2B8"), Margin = new(0, 0, 0, 10) };
    readonly TextBlock heading = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock subtitle = new() { FontSize = 11, Margin = new(0, 5, 0, 10), Foreground = UiTheme.Brush("#93A2B8") };
    readonly TextBlock skillHeading = new() { Text = global::AionDpsMeter.Local.Localization.T("Skill contribution"), FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock empty = new() { Text = global::AionDpsMeter.Local.Localization.T("Start capture to record an encounter, or select one from your saved history."), TextWrapping = TextWrapping.Wrap, Margin = new(8), Foreground = UiTheme.Brush("#93A2B8") };
    readonly EncounterTimelinePlot timeline = new();
    readonly TextBlock timelineLabel = new() { Text = global::AionDpsMeter.Local.Localization.T("DAMAGE OVER TIME"), FontSize = 10, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 8) };
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
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        Margin = new(8, 0, 8, 8);
        var browser = new DockPanel{Margin=new(0,0,0,14)};
        var listTitle=new TextBlock{Text=Localization.T("Browse fights"),FontSize=12,VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,12,0)};
        DockPanel.SetDock(listTitle,Dock.Left);browser.Children.Add(listTitle);
        historyCount.VerticalAlignment=VerticalAlignment.Center;historyCount.Margin=new(12,0,0,0);DockPanel.SetDock(historyCount,Dock.Right);browser.Children.Add(historyCount);
        browser.Children.Add(encounters);
        var item = new FrameworkElementFactory(typeof(StackPanel)); item.SetValue(MarginProperty, new Thickness(7, 2, 7, 2));
        var name = new FrameworkElementFactory(typeof(TextBlock)); name.SetBinding(TextBlock.TextProperty, new Binding("Encounter")); name.SetBinding(ToolTipProperty, new Binding("Encounter"));
        name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); item.AppendChild(name);
        var detail = new FrameworkElementFactory(typeof(TextBlock)); detail.SetBinding(TextBlock.TextProperty, new Binding("Detail")); detail.SetValue(TextBlock.FontSizeProperty, 10d);
        detail.SetBinding(ToolTipProperty, new Binding("Detail")); detail.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        detail.SetValue(TextBlock.ForegroundProperty, UiTheme.Brush("#93A2B8")); detail.SetValue(MarginProperty, new Thickness(0, 2, 0, 0)); item.AppendChild(detail);
        encounters.ItemTemplate=new DataTemplate{VisualTree=item};
        var body=new Grid();Children.Add(body);
        body.RowDefinitions.Add(new(){Height=GridLength.Auto});body.RowDefinitions.Add(new(){Height=GridLength.Auto});body.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var title=new StackPanel();title.Children.Add(browser);title.Children.Add(empty);body.Children.Add(title);
        var participantPanel = new DockPanel();
        var participantTitle = new TextBlock { Text = global::AionDpsMeter.Local.Localization.T("Party performance"), ToolTip = global::AionDpsMeter.Local.Localization.T("Ranked by DPS. Select a player to inspect their skills and timeline."), FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 10) };
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
        participants.Columns.Add(new DataGridTemplateColumn { Header = "Participant", SortMemberPath = nameof(EncounterParticipant.Participant), CellTemplate = new DataTemplate { VisualTree = player }, Width = new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=135 });

        TextColumn(participants, "Damage", "Damage", 75, "N0"); TextColumn(participants, "DPS", "DPS", 70, "N1");
        participants.RowHeight=42;
        participants.ToolTip=Localization.T("Select a player to focus their skills. Healing and other statistics are available in Party and Healing.");
        var skillTitle = new DockPanel { Margin = new(0, 4, 0, 7) };
        var all = new Button { Content = global::AionDpsMeter.Local.Localization.T("All participants"), Padding = new(9, 4, 9, 4), Margin = new(8, 0, 0, 0) };
        DockPanel.SetDock(all, Dock.Right); skillTitle.Children.Add(all); skillTitle.Children.Add(skillHeading);
        all.Click += (_, _) => { participantId = null; updating = true; participants.SelectedItem = null; updating = false; RefreshSkills(); };
        var skillPanel = new DockPanel(); DockPanel.SetDock(skillTitle, Dock.Top); skillPanel.Children.Add(skillTitle); skillPanel.Children.Add(skills);
        timeline.Height=64;
        var graph = new StackPanel(); graph.Children.Add(timelineLabel); graph.Children.Add(timeline);
        var graphCard = UiTheme.Card(graph, new Thickness(10)); graphCard.Margin = new(0, 0, 0, 10); SetRow(graphCard, 1); body.Children.Add(graphCard);
        SizeChanged+=(_,_)=>graphCard.Visibility=ActualHeight<400?Visibility.Collapsed:Visibility.Visible;
        var split=new Grid();split.ColumnDefinitions.Add(new(){Width=new GridLength(0.42,GridUnitType.Star),MinWidth=330});split.ColumnDefinitions.Add(new(){Width=new GridLength(0.58,GridUnitType.Star),MinWidth=330});
        var partyCard=UiTheme.Card(participantPanel,new Thickness(16));partyCard.Margin=new(0,0,12,0);split.Children.Add(partyCard);
        var skillCard=UiTheme.Card(skillPanel,new Thickness(16));SetColumn(skillCard,1);split.Children.Add(skillCard);SetRow(split,2);body.Children.Add(split);

        var skill = new FrameworkElementFactory(typeof(DockPanel));
        var image = new FrameworkElementFactory(typeof(Image)); image.SetValue(WidthProperty, 28d); image.SetValue(HeightProperty, 28d); image.SetValue(MarginProperty, new Thickness(0, 0, 7, 0));
        image.SetBinding(Image.SourceProperty, new Binding("SkillCode") { Converter = icons }); skill.AppendChild(image);
        var label = new FrameworkElementFactory(typeof(TextBlock)); label.SetBinding(TextBlock.TextProperty, new Binding("Skill")); label.SetBinding(ToolTipProperty, new Binding("Skill"));
        label.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center); label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); skill.AppendChild(label);
        skills.Columns.Add(new DataGridTemplateColumn { Header = "Skill", CellTemplate = new DataTemplate { VisualTree = skill }, Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 125 });
        TextColumn(skills,"Damage","Damage",85,"N0");TextColumn(skills,"DamageShare","Share",68,"0.0");
        skills.RowHeight=42;
        var rowDetails=new Style(typeof(DataGridCell));rowDetails.Setters.Add(new Setter(ToolTipProperty,new Binding{Converter=new SkillDetailConverter()}));skills.CellStyle=rowDetails;
        SkillDamageDisplay.Apply(participants, "#55D6BE", nameof(EncounterParticipant.DamagePercentOfLeader));
        SkillDamageDisplay.Apply(skills, "#55D6BE", nameof(EncounterSkill.DamageShare));
        encounters.SelectionChanged += (_, _) => { if (!updating && encounters.SelectedItem is Entry entry && choices.TryGetValue(entry.Id, out var fight)) Selected?.Invoke(entry.Id == liveId ? null : fight); };
        participants.SelectionChanged += (_, _) => { if (!updating) { participantId = (participants.SelectedItem as EncounterParticipant)?.Id; RefreshSkills(); } };
    }
    sealed class SkillDetailConverter:IValueConverter{
        public object Convert(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)=>value is EncounterSkill s?Localization.F($"{s.Participant} ? {s.Skill}\n{s.Hits:N0} hits ? {s.DPS:N1} DPS ? {s.CritPercent:0.0}% critical"):"";
        public object ConvertBack(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)=>Binding.DoNothing;
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
            var history = EncounterOverview.History(current, live, archive, search);
            historyCount.Text = $"{history.Length:N0} {(history.Length == 1 ? "encounter" : "encounters")}";
            var entries = history
                .Select(f => new Entry(f.Id, EncounterOverview.Title(f), (f.Id == live.Id ? capturing ? "LIVE" : "CURRENT" : f.Demo ? "DEMO" : "SAVED") + "  |  " + f.Created.ToLocalTime().ToString("dd MMM HH:mm") + "  |  " + MeterPresentation.Duration(f))).ToArray();
            if(!encounters.IsDropDownOpen){
                if(!lastEntries.SequenceEqual(entries)){lastEntries=entries;encounters.ItemsSource=entries;}
                encounters.SelectedItem=encounters.Items.Cast<Entry>().FirstOrDefault(e=>e.Id==current.Id);
            }
            heading.Text = EncounterOverview.Title(current); heading.ToolTip = heading.Text;
            var people = currentPeople = EncounterOverview.Participants(current);
            subtitle.Text = global::AionDpsMeter.Local.Localization.F($"{people.Length} players  |  {MeterPresentation.Duration(current)}  |  {people.Sum(p => p.Damage):N0} damage  |  {people.Sum(p => p.Healing):N0} healing");
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
        skillHeading.Text = participantId == null ? global::AionDpsMeter.Local.Localization.T("Skill contribution") : "" + selected.Name(participantId.Value);
        timelineLabel.Text = participantId == null ? global::AionDpsMeter.Local.Localization.T("PARTY DAMAGE OVER TIME") : selected.Name(participantId.Value).ToUpperInvariant() + global::AionDpsMeter.Local.Localization.T(" · DAMAGE OVER TIME");
        timeline.Update(selected, people.Select(p => p.Id));
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
        int builds = timeline.Builds; RefreshSkills();
        if (timeline.Builds != builds || timeline.BucketCount is < 1 or > 120)
            throw new InvalidOperationException("Encounter timeline rebuilt unchanged data or exceeded its rendering budget.");
        participantId = null; participants.SelectedItem = null; RefreshSkills();
        if (skills.Items.Count != EncounterOverview.Skills(fight, EncounterOverview.Participants(fight).Select(p => p.Id), skillName).Length)
            throw new InvalidOperationException("All-participant skill breakdown did not restore all rows.");
        UpdateLayout();
    }
}
