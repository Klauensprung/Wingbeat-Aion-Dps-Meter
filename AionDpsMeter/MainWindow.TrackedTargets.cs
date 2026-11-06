using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
namespace AionDpsMeter.Local;
public sealed partial class MainWindow
{
    MapTargets? trackedTargets;
    Window? trackedTargetsWindow;
    Action? refreshTrackedTargets;
    DispatcherTimer? trackedTargetsTimer;
    sealed record TargetChoice(string Kind,int Id,string Name) { public string Display=>$"{Name} ({Id})"; }
    sealed record TargetRow(string Kind,int Id,string Name,string Count,string Goal,string Souls,string Status,string Nearby);
    void ShowTrackedTargets()
    {
        if(trackedTargetsWindow!=null){trackedTargetsWindow.Activate();return;}
        trackedTargets??=new(demoMode?null:AppDataPaths.FilePath("farming-targets.json"));var store=trackedTargets;
        var index=new MapTargetIndex(MapMarkers.Default.Groups);var pets=PetFarmLinks.Default;
        var window=new Window {Title=global::AionDpsMeter.Local.Localization.T("Tracked farming targets"),Owner=this,Width=940,Height=600,MinWidth=780,MinHeight=540,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        trackedTargetsWindow=window;window.Closed+=(_,_)=>{trackedTargetsWindow=null;refreshTrackedTargets=null;};UiTheme.Apply(window,preferences);
        var layout=new DockPanel {Margin=new(18)};var heading=new StackPanel();DockPanel.SetDock(heading,Dock.Top);layout.Children.Add(heading);
        heading.Children.Add(UiSections.Heading("Tracked farming targets","Saved on this PC",
            "Choose NPCs, gathering resources, pets or items to follow. Counts use your Farming totals since reset, including earlier activity. Pet targets count kills of linked NPCs and explicit soul gains; completion uses the latest captured pet level. Pets without verified NPC links show unknown kill and nearby counts. Item targets count loot quantities and show known NPC and gathering source locations. Trace counts use captured achievements for the current character. A count prefixed with >= is a lower bound until a full achievement list arrives. Nearby counts use captured spawn/removal state, not distance. This window refreshes every second while open and keeps the latest eight notices. Closing it stops refreshes and clears notices. Removing a target keeps your totals."));
        var addRow=new WrapPanel {Margin=new(0,0,0,8)};
        var kind=new ComboBox {ItemsSource=new[]{"NPC","Gathering","Pet","Item","Traces"},SelectedIndex=0,Width=120,Tag="Target kind",Margin=new(0,0,8,0)};
        var search=new TextBox {Width=240,Tag="Find target by name or ID...",MaxLength=100,Margin=new(0,0,8,0)};
        var choices=new ComboBox {Width=300,DisplayMemberPath="Display",Tag="Target choices",Margin=new(0,0,8,0)};
        var add=new Button {Content=global::AionDpsMeter.Local.Localization.T("Track"),IsEnabled=false};addRow.Children.Add(kind);addRow.Children.Add(search);addRow.Children.Add(choices);addRow.Children.Add(add);heading.Children.Add(addRow);
        var actions=new WrapPanel {Margin=new(0,0,0,8)};
        var refresh=new Button {Content=global::AionDpsMeter.Local.Localization.T("Refresh"),Margin=new(0,0,8,0)};
        var phase=new ComboBox {ItemsSource=new[]{"Pet level 1","Pet level 2","Pet level 3"},SelectedIndex=Math.Clamp(preferences.FarmingTargetGoal,1,3)-1,Width=125,Margin=new(0,0,8,0),Tag="Tracked pet phase"};
        var goal=new TextBox {Width=90,Tag="Goal",ToolTip=global::AionDpsMeter.Local.Localization.T("Numeric target; leave empty to clear"),Margin=new(0,0,8,0)};
        var setGoal=new Button {Content=global::AionDpsMeter.Local.Localization.T("Set goal"),IsEnabled=false,Margin=new(0,0,8,0)};
        var remove=new Button {Content=global::AionDpsMeter.Local.Localization.T("Remove target"),IsEnabled=false,Margin=new(0,0,8,0)};
        var map=new Button {Content=global::AionDpsMeter.Local.Localization.T("Show on map"),IsEnabled=false,ToolTip=global::AionDpsMeter.Local.Localization.T("Open known NPC, gathering, pet or item source locations. Trace maps show locations not captured as collected.")};
        foreach(var control in new Control[]{refresh,phase,goal,setGoal,remove,map})actions.Children.Add(control);heading.Children.Add(actions);
        var status=new TextBlock {Tag="Farming session summary",TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,8)};heading.Children.Add(status);
        var table=new LiveTable {IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,EnableRowVirtualization=true,RowHeight=34};
        void Column(string name,string property,int width) {
            var style=new Style(typeof(TextBlock));style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis));style.Setters.Add(new Setter(TextBlock.ToolTipProperty,new Binding(property)));
            table.Columns.Add(new DataGridTextColumn {Header=name,Binding=new Binding(property),ElementStyle=style,Width=width});
        }
        Column("Target","Name",190);Column("Count","Count",80);Column("Nearby","Nearby",85);Column("Goal","Goal",75);Column("Souls","Souls",65);Column("Progress","Status",210);
        var monitor=new FarmingTargetView();var noticeRows=new List<string>();
        var noticesPanel=new StackPanel {Margin=new(0,8,0,0),Visibility=Visibility.Collapsed};DockPanel.SetDock(noticesPanel,Dock.Bottom);layout.Children.Add(noticesPanel);
        var noticeHeader=new DockPanel();var clearNotices=new Button {Content=global::AionDpsMeter.Local.Localization.T("Clear notices"),HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(clearNotices,Dock.Right);noticeHeader.Children.Add(clearNotices);
        noticeHeader.Children.Add(new TextBlock {Text=global::AionDpsMeter.Local.Localization.T("Recent farming notices"),VerticalAlignment=VerticalAlignment.Center});noticesPanel.Children.Add(noticeHeader);
        var noticeList=new ListBox {Tag="Farming notices",MaxHeight=84,Margin=new(0,4,0,0)};noticesPanel.Children.Add(noticeList);
        clearNotices.Click+=(_,_)=>{noticeRows.Clear();noticeList.ItemsSource=Array.Empty<string>();noticesPanel.Visibility=Visibility.Collapsed;};
        string Kind()=>kind.SelectedIndex switch {1=>"gatherable",2=>"pet",3=>"item",4=>"collectable",_=>"npc"};
        void RefreshRows() {
            var selected=table.SelectedItem as TargetRow;string goalInput=goal.Text;
            var view=monitor.Update(engine,store.Snapshot(),phase.SelectedIndex+1,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var session=view.Session;if(view.SceneChanged)noticeRows.Clear();
            var rows=view.Rows.Select(entry=>{
                var target=entry.Target;var p=entry.Progress;var around=entry.Nearby;
                string state=p.Done==true?"Complete":p.Done==null?"Progress unknown":"In progress";
                if(target.Kind=="pet"&&p.Pets.FirstOrDefault() is {} pet&&pet.KillsPerSoul is decimal ratio)state+=$" / {ratio:N0} kills per soul";
                return new TargetRow(target.Kind,target.Id,entry.Name+$" ({target.Id})",(p.CountIsPartial?">=":"")+(p.Count?.ToString("N0")??"?"),target.Kind=="pet"?$"Level {phase.SelectedIndex+1}":p.Goal?.ToString("N0")??"-",p.Pets.Length>0?p.Pets.Sum(p=>(decimal)p.SoulsGained).ToString("N0"):"-",state,target.Kind=="collectable"?"-":around?.ToString("N0")??"?");
            }).ToArray();table.SetRows(rows);table.SelectedItem=rows.FirstOrDefault(r=>r.Kind==selected?.Kind&&r.Id==selected.Id);
            if(selected!=null&&table.SelectedItem is TargetRow restored&&restored.Kind==selected.Kind&&restored.Id==selected.Id)goal.Text=goalInput;
            foreach(var notice in view.Notices){noticeRows.Insert(0,FarmingTargetView.Notice(notice));if(noticeRows.Count>8)noticeRows.RemoveAt(8);}
            noticeList.ItemsSource=noticeRows.ToArray();noticesPanel.Visibility=noticeRows.Count>0?Visibility.Visible:Visibility.Collapsed;
            string Since(long? at)=>at.HasValue?DateTimeOffset.FromUnixTimeMilliseconds(at.Value).ToLocalTime().ToString("g"):"unknown";
            status.DataContext=session;
            status.Text=global::AionDpsMeter.Local.Localization.F($"{rows.Length} targets. Kills: {session.Kills:N0} ({session.KillsPerHour:N0}/h). Souls: {session.Souls:N0}. Auto-refresh: 1s.\nKills since {Since(session.KillsSince)}; souls since {Since(session.SoulsSince)}.");
            status.ToolTip=global::AionDpsMeter.Local.Localization.T("Totals include all farming activity since each reset, not just selected targets. Kills/hour uses elapsed time with a one-minute minimum.");
            if(session.HasOmissions)status.Text+=global::AionDpsMeter.Local.Localization.T(" Some quantities were omitted.");
            if(store.LoadError!=null)status.Text+=" "+store.LoadError;if(pets.LoadError!=null)status.Text+=" "+pets.LoadError;
            if(view.Rows.Any(r=>r.Target.Kind=="item")&&ItemFarmLinks.Default.LoadError is {} itemSourceError)status.Text+=" "+itemSourceError;
            UpdateAdd();
        }
        void UpdateAdd()=>add.IsEnabled=choices.SelectedItem is TargetChoice choice&&!store.Snapshot().Any(t=>t.Kind==choice.Kind&&t.Id==choice.Id);
        void Find() {
            string query=search.Text.Trim(),type=Kind();IEnumerable<TargetChoice> available;
            if(type=="collectable")available=new[]{new TargetChoice(type,1,"Traces in current map")}.Concat(MapMarkers.Default.Groups.Where(g=>g.Category=="monolith"&&MapWorlds.Names.ContainsKey(g.World)).Select(g=>g.World).Distinct().Select(id=>new TargetChoice(type,id,$"Traces: {MapWorlds.Names[id]}")));
            else if(type=="pet")available=pets.Targets.Select(p=>new TargetChoice(type,p.Id,p.Name));
            else if(type=="item")available=query.Length>=3?ItemNames.Default.Search(query).Select(p=>new TargetChoice(type,p.Key,p.Value)):[];
            else available=MapMarkers.Default.Groups.Where(g=>MapTargetIndex.Identity(g)?.Kind==type).GroupBy(g=>g.DatabaseId).Select(g=>new TargetChoice(type,g.Key,g.First().Name));
            choices.ItemsSource=available.Where(p=>p.Name.Contains(query,StringComparison.OrdinalIgnoreCase)||p.Id.ToString()==query).OrderBy(p=>p.Name).Take(50).ToArray();choices.SelectedIndex=choices.Items.Count>0?0:-1;UpdateAdd();
        }
        void Change(Action action){try{action();RefreshRows();}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidOperationException){status.Text=global::AionDpsMeter.Local.Localization.T("Target change could not be saved: ")+ex.Message;}}
        kind.SelectionChanged+=(_,_)=>Find();search.TextChanged+=(_,_)=>Find();choices.SelectionChanged+=(_,_)=>UpdateAdd();
        add.Click+=(_,_)=>{if(choices.SelectedItem is TargetChoice c&&add.IsEnabled)Change(()=>store.Toggle(c.Kind,c.Id));};
        remove.Click+=(_,_)=>{if(table.SelectedItem is TargetRow row)Change(()=>store.Remove(row.Kind,row.Id));};
        table.SelectionChanged+=(_,_)=>{var row=table.SelectedItem as TargetRow;remove.IsEnabled=row!=null;map.IsEnabled=row!=null&&(row.Kind=="collectable"||MapTargetLocations.Worlds(MapTargetLocations.Resolve(new(row.Kind,row.Id),index,pets),MapWorlds.Names).Any(w=>w.CanOpen));setGoal.IsEnabled=row!=null&&row.Kind is not ("pet" or "collectable");goal.Text=row==null?"":store.Snapshot().FirstOrDefault(t=>t.Kind==row.Kind&&t.Id==row.Id)?.Goal?.ToString()??"";};
        setGoal.Click+=(_,_)=>{if(table.SelectedItem is not TargetRow row)return;long? value=null;if(goal.Text.Trim().Length>0){if(!long.TryParse(goal.Text,out long n)||n<0||n>1000000000){status.Text=global::AionDpsMeter.Local.Localization.T("Enter a goal from 0 to 1,000,000,000, or leave it empty.");return;}value=n;}Change(()=>store.SetGoal(row.Kind,row.Id,value));};
        map.Click+=(_,_)=>{if(table.SelectedItem is TargetRow row)ShowTargetMap(new(row.Kind,row.Id));};
        var timer=new DispatcherTimer(DispatcherPriority.Background) {Interval=TimeSpan.FromSeconds(1)};trackedTargetsTimer=timer;
        void Tick(object? sender,EventArgs args)=>RefreshRows();timer.Tick+=Tick;
        window.Closed+=(_,_)=>{timer.Stop();timer.Tick-=Tick;trackedTargetsTimer=null;};
        refreshTrackedTargets=RefreshRows;refresh.Click+=(_,_)=>RefreshRows();phase.SelectionChanged+=(_,_)=>{preferences.FarmingTargetGoal=phase.SelectedIndex+1;SavePreferences();RefreshRows();};Find();RefreshRows();layout.Children.Add(table);window.Content=layout;window.Show();timer.Start();
    }
}
