using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace AionDpsMeter.Local;

internal sealed class EncounterLogView : Grid
{
    sealed record Group(string Key,string Title,string Subtitle,EncounterLogEntry[] Entries);
    readonly TextBox search=new(){MinWidth=170,Width=230,Margin=new(0,0,12,0),Tag=Localization.T("Search bosses or encounters…")};
    readonly ComboBox character=new(){MinWidth=160,Margin=new(0,0,12,0)};
    readonly ComboBox range=new(){MinWidth=130};
    readonly ListBox groups=new(){BorderThickness=new(0),Background=Brushes.Transparent,HorizontalContentAlignment=HorizontalAlignment.Stretch};
    readonly TextBlock title=Text("Your encounter journal",28,"Text",true), subtitle=Text("",12,"Muted");
    readonly TextBlock groupTitle=Text("DAYS",11,"Muted",true), count=Text("",12,"Muted"), insight=Text("",12,"Muted");
    readonly UniformGrid statGrid=new(){Columns=4,Margin=new(0,18,0,18)};
    readonly DataGrid fights=new(){AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,CanUserDeleteRows=false,CanUserSortColumns=true,
        SelectionMode=DataGridSelectionMode.Single,HeadersVisibility=DataGridHeadersVisibility.Column,RowHeight=62,BorderThickness=new(0),
        EnableRowVirtualization=true,EnableColumnVirtualization=true,GridLinesVisibility=DataGridGridLinesVisibility.None};
    readonly TextBlock empty=Text("No encounters yet. Saved fights will appear here after your next session.",15,"Muted");
    readonly Button dateMode,bossMode;
    readonly EncounterActivityChart chart=new(){Height=128,Margin=new(0,8,0,0)};
    EncounterLogEntry[] all=[];
    bool initialized,rebuilding,byBoss;
    string? selectedKey;
    int page;
    const int PageSize=100;
    readonly Button previous,next;
    readonly TextBlock pageLabel=Text("",12,"Muted");
    internal event Action<string>? OpenFight;
    internal int VisibleFightCount=>fights.Items.Count;
    internal string SelectionTitle=>title.Text;

    internal EncounterLogView(){
        Margin=new(22,8,22,16);
        RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new());
        var toolbar=new DockPanel{Margin=new(0,0,0,18)};
        var modes=new StackPanel{Orientation=Orientation.Horizontal};
        dateMode=ActionButton("By date",()=>ChangeMode(false));bossMode=ActionButton("By boss",()=>ChangeMode(true));
        modes.Children.Add(dateMode);modes.Children.Add(bossMode);DockPanel.SetDock(modes,Dock.Left);toolbar.Children.Add(modes);
        var filters=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};filters.Children.Add(search);filters.Children.Add(character);filters.Children.Add(range);toolbar.Children.Add(filters);Children.Add(toolbar);
        character.Items.Add(Localization.T("All characters"));character.SelectedIndex=0;
        foreach(string label in new[]{"All time","Last 30 days","Last 7 days","Today"})range.Items.Add(Localization.T(label));range.SelectedIndex=0;
        search.TextChanged+=(_,_)=>{if(!rebuilding)Rebuild();};character.SelectionChanged+=(_,_)=>{if(!rebuilding)Rebuild();};range.SelectionChanged+=(_,_)=>{if(!rebuilding)Rebuild();};
        var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new(220)});body.ColumnDefinitions.Add(new(){Width=new(20)});body.ColumnDefinitions.Add(new());SetRow(body,1);Children.Add(body);
        var navigation=new DockPanel();var navHead=new StackPanel{Margin=new(12,12,12,16)};navHead.Children.Add(groupTitle);navHead.Children.Add(count);DockPanel.SetDock(navHead,Dock.Top);navigation.Children.Add(navHead);navigation.Children.Add(groups);
        body.Children.Add(Card(navigation,new(0),"Panel"));
        groups.ItemTemplate=(DataTemplate)XamlReader.Parse("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <StackPanel Margin="8,10"><TextBlock Text="{Binding Title}" FontWeight="SemiBold" TextWrapping="Wrap"/><TextBlock Text="{Binding Subtitle}" Foreground="{DynamicResource Muted}" FontSize="11" Margin="0,5,0,0" TextWrapping="Wrap"/></StackPanel>
            </DataTemplate>
            """);
        VirtualizingPanel.SetIsVirtualizing(groups,true);VirtualizingPanel.SetVirtualizationMode(groups,VirtualizationMode.Recycling);
        groups.SelectionChanged+=(_,_)=>{if(rebuilding)return;selectedKey=(groups.SelectedItem as Group)?.Key;page=0;ShowSelection();};
        var detail=new Grid();detail.RowDefinitions.Add(new(){Height=GridLength.Auto});detail.RowDefinitions.Add(new(){Height=GridLength.Auto});detail.RowDefinitions.Add(new());detail.RowDefinitions.Add(new(){Height=GridLength.Auto});SetColumn(detail,2);body.Children.Add(detail);
        var intro=new StackPanel();intro.Children.Add(Text("ENCOUNTER LOG",10,"Accent",true));intro.Children.Add(title);subtitle.Margin=new(0,6,0,0);intro.Children.Add(subtitle);intro.Children.Add(statGrid);detail.Children.Add(intro);
        var activity=new StackPanel();var activityHeading=new DockPanel();var hint=Text("Select a day to explore",11,"Muted");hint.HorizontalAlignment=HorizontalAlignment.Right;DockPanel.SetDock(hint,Dock.Right);activityHeading.Children.Add(hint);activityHeading.Children.Add(Text("Activity · last 14 days",13,"Text",true));activity.Children.Add(activityHeading);activity.Children.Add(chart);
        var activityCard=Card(activity,new(18,14,18,8),"Panel");activityCard.Margin=new(0,0,0,18);SetRow(activityCard,1);detail.Children.Add(activityCard);
        chart.DaySelected+=day=>{byBoss=false;selectedKey=day.ToString("yyyy-MM-dd");page=0;Rebuild();};
        var timeline=new Grid();timeline.RowDefinitions.Add(new(){Height=GridLength.Auto});timeline.RowDefinitions.Add(new());
        var timelineHeader=new DockPanel{Margin=new(16,12,16,10)};var open=ActionButton("Open encounter →",OpenSelected);DockPanel.SetDock(open,Dock.Right);timelineHeader.Children.Add(open);timelineHeader.Children.Add(Text("Recorded encounters",15,"Text",true));timeline.Children.Add(timelineHeader);
        SetRow(fights,1);timeline.Children.Add(fights);empty.Margin=new(24);empty.TextWrapping=TextWrapping.Wrap;empty.VerticalAlignment=VerticalAlignment.Center;SetRow(empty,1);timeline.Children.Add(empty);
        fights.Columns.Add(new DataGridTextColumn{Header=Localization.T("Time"),Binding=new Binding(nameof(EncounterLogEntry.When)),Width=64});
        var encounterTemplate=(DataTemplate)XamlReader.Parse("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <StackPanel Margin="4,7"><TextBlock Text="{Binding Title}" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/><StackPanel Orientation="Horizontal"><TextBlock Text="{Binding Character}" Foreground="{DynamicResource Muted}" FontSize="11"/><TextBlock Text=" · " Foreground="{DynamicResource Muted}"/><TextBlock Text="{Binding Outcome}" Foreground="{DynamicResource Accent}" FontSize="11"/></StackPanel></StackPanel>
            </DataTemplate>
            """);
        fights.Columns.Add(new DataGridTemplateColumn{Header=Localization.T("Encounter"),CellTemplate=encounterTemplate,Width=new(1,DataGridLengthUnitType.Star),MinWidth=165,SortMemberPath=nameof(EncounterLogEntry.Title)});
        AddColumn("Duration",nameof(EncounterLogEntry.Duration),100);AddColumn("Your DPS",nameof(EncounterLogEntry.DamageRate),88);AddColumn("Your HPS",nameof(EncounterLogEntry.HealingRate),88);
        ScrollViewer.SetHorizontalScrollBarVisibility(fights,ScrollBarVisibility.Auto);ScrollViewer.SetVerticalScrollBarVisibility(fights,ScrollBarVisibility.Auto);
        fights.SizeChanged+=(_,_)=>FitColumns();
        fights.ToolTip=Localization.T("Damage and healing are your character's totals for the whole recorded encounter, including adds. Boss views group exact NPC IDs. An unconfirmed outcome is not counted as a wipe.");
        fights.MouseDoubleClick+=(_,_)=>OpenSelected();fights.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){OpenSelected();e.Handled=true;}};
        var timelineCard=Card(timeline,new(0),"Panel");SetRow(timelineCard,2);detail.Children.Add(timelineCard);
        var bottom=new DockPanel{Margin=new(0,12,0,0)};var paging=new StackPanel{Orientation=Orientation.Horizontal};previous=ActionButton("←",()=>{page--;ShowSelection();});next=ActionButton("→",()=>{page++;ShowSelection();});pageLabel.VerticalAlignment=VerticalAlignment.Center;paging.Children.Add(previous);paging.Children.Add(pageLabel);paging.Children.Add(next);DockPanel.SetDock(paging,Dock.Right);bottom.Children.Add(paging);insight.TextWrapping=TextWrapping.Wrap;bottom.Children.Add(insight);SetRow(bottom,3);detail.Children.Add(bottom);
        SizeChanged+=(_,_)=>{body.ColumnDefinitions[0].Width=new(ActualWidth<1000?175:220);search.Width=ActualWidth<1000?170:230;statGrid.Columns=ActualWidth<850?2:4;};
        Rebuild();
    }
    void FitColumns(){if(fights.ActualWidth>0)fights.Columns[1].Width=new(Math.Max(165,fights.ActualWidth-(byBoss?110:64)-100-88-88-24));}
    void AddColumn(string label,string path,double width){
        var style=new Style(typeof(TextBlock));style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center));style.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty,HorizontalAlignment.Right));style.Setters.Add(new Setter(TextBlock.MarginProperty,new Thickness(0,0,12,0)));
        fights.Columns.Add(new DataGridTextColumn{Header=Localization.T(label),Binding=new Binding(path),Width=width,ElementStyle=style,
            SortMemberPath=path==nameof(EncounterLogEntry.DamageRate)?nameof(EncounterLogEntry.Dps):path==nameof(EncounterLogEntry.HealingRate)?nameof(EncounterLogEntry.Hps):nameof(EncounterLogEntry.Seconds)});
    }
    static TextBlock Text(string text,double size,string resource,bool bold=false){var t=new TextBlock{Text=Localization.T(text),FontSize=size,TextWrapping=TextWrapping.Wrap,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal};t.SetResourceReference(TextBlock.ForegroundProperty,resource);return t;}
    static Border Card(UIElement content,Thickness padding,string resource){var b=new Border{Child=content,Padding=padding,CornerRadius=new(12),BorderThickness=new(1)};b.SetResourceReference(Border.BackgroundProperty,resource);b.SetResourceReference(Border.BorderBrushProperty,"Edge");return b;}
    static Button ActionButton(string label,Action action){var b=new Button{Content=Localization.T(label),Padding=new(12,7,12,7),Margin=new(0,0,6,0),Cursor=Cursors.Hand};b.Click+=(_,_)=>action();return b;}
    void OpenSelected(){if(fights.SelectedItem is EncounterLogEntry row)OpenFight?.Invoke(row.Id);}
    void ChangeMode(bool boss){if(byBoss==boss)return;byBoss=boss;selectedKey=null;page=0;Rebuild();}
    internal void Update(IEnumerable<Fight> archive,bool changed){
        if(initialized&&!changed)return;initialized=true;all=EncounterLog.Build(archive);
        string? selected=character.SelectedIndex>0?character.SelectedItem as string:null;
        rebuilding=true;character.Items.Clear();character.Items.Add(Localization.T("All characters"));
        foreach(var name in all.Select(e=>e.Character).Distinct().Order())character.Items.Add(name);
        character.SelectedItem=selected!=null&&character.Items.Contains(selected)?selected:character.Items[0];rebuilding=false;Rebuild(keepPage:true);
    }
    EncounterLogEntry[] Filter(){
        DateTime first=range.SelectedIndex switch{1=>DateTime.Today.AddDays(-29),2=>DateTime.Today.AddDays(-6),3=>DateTime.Today,_=>DateTime.MinValue};
        string query=search.Text.Trim();string? who=character.SelectedIndex>0?character.SelectedItem as string:null;
        return all.Where(e=>e.Date>=first&&(who==null||e.Character==who)&&(query.Length==0||e.Title.Contains(query,StringComparison.OrdinalIgnoreCase)||e.Character.Contains(query,StringComparison.OrdinalIgnoreCase)||e.Bosses.Any(b=>b.Name.Contains(query,StringComparison.OrdinalIgnoreCase)||b.NpcId.ToString().Contains(query)))).ToArray();
    }
    void Rebuild(bool keepPage=false){
        var filtered=Filter();Group[] items;
        if(byBoss)items=filtered.SelectMany(e=>e.Bosses.DistinctBy(EncounterLog.BossKey).Select(b=>(Entry:e,Boss:b))).GroupBy(x=>EncounterLog.BossKey(x.Boss))
            .Select(g=>new Group(g.Key,g.First().Boss.Name,Localization.F($"{g.Count():N0} encounters")+" · "+(g.First().Boss.NpcId>0?"#"+g.First().Boss.NpcId:Localization.T("Captured name")),g.Select(x=>x.Entry).ToArray()))
            .OrderByDescending(g=>g.Entries.Max(e=>e.Date)).ThenBy(g=>g.Title).ToArray();
        else items=filtered.GroupBy(e=>e.Date.Date).OrderByDescending(g=>g.Key).Select(g=>new Group(g.Key.ToString("yyyy-MM-dd"),g.Key.ToString("ddd, dd MMM yyyy"),Localization.F($"{g.Count():N0} encounters")+" · "+EncounterLog.Duration(g.Sum(e=>e.Seconds)),g.ToArray())).ToArray();
        rebuilding=true;groups.ItemsSource=items;groups.SelectedItem=items.FirstOrDefault(g=>g.Key==selectedKey)??items.FirstOrDefault();selectedKey=(groups.SelectedItem as Group)?.Key;rebuilding=false;
        groupTitle.Text=Localization.T(byBoss?"BOSSES":"DAYS");count.Text=Localization.F($"{items.Length:N0} in your journal");
        dateMode.BorderBrush=byBoss?Brushes.Transparent:UiTheme.Brush("#55D6BE");bossMode.BorderBrush=byBoss?UiTheme.Brush("#55D6BE"):Brushes.Transparent;
        if(!keepPage)page=0;ShowSelection();
    }
    void ShowSelection(){
        var selected=groups.SelectedItem as Group;var rows=selected?.Entries??[];var stats=EncounterLog.Summarize(rows);
        title.Text=selected?.Title??Localization.T("Your encounter journal");
        subtitle.Text=rows.Length==0?Localization.T("No results. Try another filter or save your first fight."):
            byBoss?Localization.T("Every recorded attempt, together. Select a fight to inspect it."):
            Localization.F($"{rows.Select(e=>e.Character).Distinct().Count()} characters · {rows.SelectMany(e=>e.Bosses).DistinctBy(EncounterLog.BossKey).Count()} bosses · your day in Aion");
        statGrid.Children.Clear();Metric("Encounters",stats.Attempts.ToString("N0"),"Saved entries");
        int kills=byBoss?rows.Sum(e=>e.Bosses.Count(b=>EncounterLog.BossKey(b)==selectedKey&&b.Defeated)):stats.Kills;
        Metric("Boss defeats",kills.ToString("N0"),"Death observed");Metric("Recorded combat",EncounterLog.Duration(stats.Seconds),"Sum of fight durations");Metric("Best encounter DPS",MeterPresentation.Number(stats.BestDps),"Your recorded character");
        var filtered=Filter();var activityRows=byBoss?rows:filtered;
        DateTime end=activityRows.Length>0?activityRows.Max(e=>e.Date).Date:DateTime.Today;
        if(!byBoss&&rows.Length>0)end=rows[0].Date.Date;
        chart.Update(activityRows,end,!byBoss&&rows.Length>0?rows[0].Date.Date:null);
        page=Math.Clamp(page,0,Math.Max(0,(rows.Length-1)/PageSize));string? old=(fights.SelectedItem as EncounterLogEntry)?.Id;
        var visible=rows.OrderByDescending(e=>e.Date).Skip(page*PageSize).Take(PageSize).ToArray();fights.ItemsSource=visible;fights.SelectedItem=visible.FirstOrDefault(e=>e.Id==old)??visible.FirstOrDefault();
        ((DataGridTextColumn)fights.Columns[0]).Binding=new Binding(byBoss?nameof(EncounterLogEntry.Day):nameof(EncounterLogEntry.When));fights.Columns[0].Width=byBoss?110:64;
        fights.Columns[0].Header=Localization.T(byBoss?"Date":"Time");fights.Columns[0].SortMemberPath=nameof(EncounterLogEntry.Date);FitColumns();
        empty.Visibility=rows.Length==0?Visibility.Visible:Visibility.Collapsed;fights.Visibility=rows.Length==0?Visibility.Collapsed:Visibility.Visible;
        previous.IsEnabled=page>0;next.IsEnabled=(page+1)*PageSize<rows.Length;pageLabel.Text=rows.Length==0?"0 / 0":$"{page*PageSize+1}–{Math.Min((page+1)*PageSize,rows.Length)} / {rows.Length}";
        insight.Text=Localization.F($"Your damage {MeterPresentation.Number(stats.Damage)} · healing {MeterPresentation.Number(stats.Healing)} · deaths {stats.Deaths:N0} · loot pickups {stats.Loot:N0}");
    }
    void Metric(string name,string value,string note){var panel=new StackPanel();panel.Children.Add(Text(name,11,"Muted"));var number=Text(value,25,"Text",true);number.Margin=new(0,8,0,5);panel.Children.Add(number);panel.Children.Add(Text(note,10,"Muted"));var card=Card(panel,new(14),"Panel");card.Margin=new(0,0,8,8);statGrid.Children.Add(card);}
    internal void CheckBossMode(){ChangeMode(true);}
}

internal sealed class EncounterActivityChart : FrameworkElement
{
    (DateTime Day,int Count)[] days=[];DateTime? selected;
    internal event Action<DateTime>? DaySelected;
    internal EncounterActivityChart(){Cursor=Cursors.Hand;MouseLeftButtonUp+=(_,e)=>{int i=Index(e.GetPosition(this).X);if(i>=0&&days[i].Count>0)DaySelected?.Invoke(days[i].Day);};MouseMove+=(_,e)=>{int i=Index(e.GetPosition(this).X);if(i>=0)ToolTip=days[i].Day.ToString("D")+" · "+Localization.F($"{days[i].Count} encounters");};}
    int Index(double x)=>days.Length==0||ActualWidth<=0?-1:Math.Clamp((int)(x/ActualWidth*days.Length),0,days.Length-1);
    internal void Update(EncounterLogEntry[] entries,DateTime end,DateTime? selectedDay){var counts=entries.GroupBy(e=>e.Date.Date).ToDictionary(g=>g.Key,g=>g.Count());days=Enumerable.Range(0,14).Select(i=>(Day:end.AddDays(i-13),Count:counts.GetValueOrDefault(end.AddDays(i-13)))).ToArray();selected=selectedDay;InvalidateVisual();}
    protected override void OnRender(DrawingContext dc){
        if(days.Length==0||ActualWidth<=0)return;double step=ActualWidth/days.Length,maximum=Math.Max(1,days.Max(d=>d.Count)),baseline=ActualHeight-26;
        var muted=TryFindResource("Muted") as Brush??Brushes.Gray;var edge=TryFindResource("Edge") as Brush??Brushes.DimGray;
        for(int j=0;j<3;j++){double y=14+(baseline-14)*j/2;dc.DrawLine(new Pen(edge,.5),new(0,y),new(ActualWidth,y));}
        for(int i=0;i<days.Length;i++){
            var day=days[i];double height=day.Count==0?3:Math.Max(6,(baseline-26)*day.Count/maximum);double width=Math.Max(3,step*.52),x=step*i+(step-width)/2;
            var brush=UiTheme.Brush(day.Day==selected?"#71EDD1":day.Count>0?"#358F87":"#293443");dc.DrawRoundedRectangle(brush,null,new Rect(x,baseline-height,width,height),3,3);
            Draw(day.Day.ToString("dd"),step*(i+.5),baseline+8,muted,10);
            if(day.Count>0)Draw(day.Count.ToString(),step*(i+.5),baseline-height-17,muted,10);
        }
        void Draw(string value,double center,double y,Brush brush,double size){var text=new FormattedText(value,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip);dc.DrawText(text,new Point(center-text.Width/2,y));}
    }
}
