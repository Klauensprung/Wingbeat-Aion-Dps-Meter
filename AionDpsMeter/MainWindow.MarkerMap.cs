using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    Window? markerMapWindow;
    Action<MapTarget?>? focusMapTarget;
    void ShowTargetMap(MapTarget target) { ShowMarkerMap(); focusMapTarget?.Invoke(target); }
    MapPins? mapPins;
    MapCategories? mapCategories;
    sealed record MarkerWorld(int Id, string Name);
    sealed record MarkerRow(int Id, string Name, string Type, string Category, int Count, string Pinned);
    void ShowMarkerMap()
    {
        if(markerMapWindow!=null){markerMapWindow.Activate();return;}
        var catalogue=MapMarkers.Default;
        trackedTargets??=new(demoMode?null:AppDataPaths.FilePath("farming-targets.json"));var targetStore=trackedTargets;
        mapPins??=new(demoMode?null:AppDataPaths.FilePath("map-pins.json"));
        var pins=mapPins;
        mapCategories??=new(demoMode?null:AppDataPaths.FilePath("map-categories.json"));var filters=mapCategories;
        var window=new Window {Title=global::AionDpsMeter.Local.Localization.T("World map"),Owner=this,Width=1040,Height=690,MinWidth=780,MinHeight=560,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        markerMapWindow=window;window.Closed+=(_,_)=>{markerMapWindow=null;focusMapTarget=null;};UiTheme.Apply(window,preferences);
        var layout=new DockPanel {Margin=new(18)};var heading=new StackPanel();DockPanel.SetDock(heading,Dock.Top);layout.Children.Add(heading);
        heading.Children.Add(UiSections.Heading("World map", "Pin places you want to revisit",
            "Pins are saved for each map and stay visible through search and category filters. Search finds names and categories across maps from three characters. Open a result to visit its map. These are reference locations, not live creature availability. Scroll to zoom and drag to pan; click a marker for its name. Refresh position to show the latest captured position. Terrain is available for supported maps; other maps show coordinates on a grid."));
        var controls=new WrapPanel {Margin=new(0,0,0,8)};
        var worlds=MapWorlds.Names.Select(p=>new MarkerWorld(p.Key,$"{p.Value} ({p.Key})")).OrderBy(w=>w.Name).ToArray();
        var searchIndex=new MapSearch(catalogue.Groups,MapWorlds.Names);
        var targetIndex=new MapTargetIndex(catalogue.Groups);MapTarget? focusedTarget=null;HashSet<int>? focusedGroups=null;
        MapSearchResult searchResult=new([],0,false);int searchLimit=50;
        var world=new ComboBox {ItemsSource=worlds,DisplayMemberPath="Name",Width=270,Margin=new(0,0,8,0),Tag="Marker world"};
        var category=new Button {Content=global::AionDpsMeter.Local.Localization.T("Categories"),Width=160,Margin=new(0,0,8,0),Tag="Marker categories"};
        var categoryMenu=new ContextMenu();category.ContextMenu=categoryMenu;category.Click+=(_,_)=>{categoryMenu.PlacementTarget=category;categoryMenu.IsOpen=true;};
        var refresh=new Button {Content=global::AionDpsMeter.Local.Localization.T("Refresh position"),Margin=new(0,0,8,0)};
        var fit=new Button {Content=global::AionDpsMeter.Local.Localization.T("Fit map")};
        var routeToggle=new CheckBox {Content=global::AionDpsMeter.Local.Localization.T("Farming route"),Margin=new(12,0,8,0),VerticalAlignment=VerticalAlignment.Center};
        var onTop=new CheckBox {Content=global::AionDpsMeter.Local.Localization.T("Stay on top"),Margin=new(8,0,0,0),VerticalAlignment=VerticalAlignment.Center};
        onTop.Click+=(_,_)=>window.Topmost=onTop.IsChecked==true;
        var nextStop=new Button {Content=global::AionDpsMeter.Local.Localization.T("Next stop"),Margin=new(8,0,0,0)};controls.Children.Add(nextStop);
        var restartRoute=new Button {Content=global::AionDpsMeter.Local.Localization.T("Restart route"),Margin=new(8,0,0,0)};controls.Children.Add(restartRoute);
        var routeStatus=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,6)};
        heading.Children.Add(routeStatus);controls.Children.Add(routeToggle);controls.Children.Add(onTop);
        controls.Children.Add(world);controls.Children.Add(category);controls.Children.Add(refresh);controls.Children.Add(fit);heading.Children.Add(controls);
        var search=new TextBox {Tag="Search all maps...",ToolTip=global::AionDpsMeter.Local.Localization.T("Search names and categories across maps (at least 3 characters)"),MaxLength=300,Margin=new(0,0,0,8)};heading.Children.Add(search);
        var resultsRow=new WrapPanel {Margin=new(0,0,0,8)};
        var results=new ComboBox {Width=360,DisplayMemberPath="Display",Tag="Map search results",Margin=new(0,0,8,0)};
        var openResult=new Button {Content=global::AionDpsMeter.Local.Localization.T("Open result"),IsEnabled=false,Margin=new(0,0,8,0)};
        var more=new Button {Content=global::AionDpsMeter.Local.Localization.T("Show 50 more"),IsEnabled=false};
        resultsRow.Children.Add(results);resultsRow.Children.Add(openResult);resultsRow.Children.Add(more);heading.Children.Add(resultsRow);
        var actions=new WrapPanel {Margin=new(0,0,0,8)};var pin=new Button {Content=global::AionDpsMeter.Local.Localization.T("Pin selected"),IsEnabled=false,Margin=new(0,0,8,0)};
        var clear=new Button {Content=global::AionDpsMeter.Local.Localization.T("Clear map pins")};actions.Children.Add(pin);actions.Children.Add(clear);
        var clearTarget=new Button {Content=global::AionDpsMeter.Local.Localization.T("Back to all markers"),Visibility=Visibility.Collapsed,Margin=new(8,0,0,0)};actions.Children.Add(clearTarget);heading.Children.Add(actions);
        var trackingRow=new WrapPanel {Margin=new(0,0,0,8)};
        var trackChoice=new ComboBox {Width=270,DisplayMemberPath="Display",Tag="Map tracking identity",ToolTip=global::AionDpsMeter.Local.Localization.T("Choose an entity ID when multiple creatures share a name"),Margin=new(0,0,8,0)};
        var track=new Button {Content=global::AionDpsMeter.Local.Localization.T("Track selected"),IsEnabled=false,Margin=new(0,0,8,0)};
        var showTargets=new Button {Content=global::AionDpsMeter.Local.Localization.T("Tracked targets")};showTargets.Click+=(_,_)=>ShowTrackedTargets();
        trackingRow.Children.Add(trackChoice);trackingRow.Children.Add(track);trackingRow.Children.Add(showTargets);heading.Children.Add(trackingRow);
        var status=new TextBlock {Tag="Map marker status",TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,8)};heading.Children.Add(status);
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star),MinWidth=380});grid.ColumnDefinitions.Add(new(){Width=new GridLength(1.2,GridUnitType.Star)});
        var table=new LiveTable {IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,RowHeight=32,EnableRowVirtualization=true,Margin=new(0,0,10,0)};
        var nameStyle=new Style(typeof(TextBlock));nameStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis));nameStyle.Setters.Add(new Setter(TextBlock.ToolTipProperty,new Binding("Name")));
        table.Columns.Add(new DataGridTextColumn {Header="Place",Binding=new Binding("Name"),ElementStyle=nameStyle,Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=200});
        table.Columns.Add(new DataGridTextColumn {Header="Count",Binding=new Binding("Count"),Width=80});
        table.Columns.Add(new DataGridTextColumn {Header="Pinned",Binding=new Binding("Pinned"),Width=80});
        var plot=new PetSpawnPlot {MinHeight=100};Grid.SetColumn(plot,1);grid.Children.Add(table);grid.Children.Add(plot);layout.Children.Add(grid);
        MapMarkerPoint[] points=[];MapMarkerGroup[] groups=[];MapMarkerGroup[] visibleGroups=[];int currentWorld=0;bool loading=false;CapturedPosition? position=null;
        void Position() {lock(engine.Gate)position=MapPositionPresentation.Select(engine.Position.Latest,
            engine.PositionPrediction.Current,preferences.PetMapPredictPosition,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),capture!=null).Position;}
        HashSet<(double,double)> visitedStops=new();PetSpawnPoint[] activeRoute=[];int routeWorld=0;
        void RefreshRoute() {
            if(routeWorld!=currentWorld){visitedStops.Clear();routeWorld=currentWorld;}
            if(routeToggle.IsChecked!=true){plot.SetRoute([]);routeStatus.Text="";return;}
            Position();
            var local=position!=null && WorldId(position)==currentWorld?position:null;
            plot.SetPlayer(local);
            if(local==null){plot.SetRoute([]);routeStatus.Text=global::AionDpsMeter.Local.Localization.T("Route: waiting for your position in this map.");return;}
            var targets=focusedTarget==null?targetStore.Snapshot():new[]{focusedTarget};
            var ids=targets.SelectMany(t=>MapTargetLocations.Resolve(t,targetIndex,PetFarmLinks.Default))
                .Where(g=>g.World==currentWorld).Select(g=>g.Id).ToHashSet();
            var candidates=points.Where(p=>ids.Contains(p.Group)).Select(p=>new PetSpawnPoint(p.Group+1,p.X,p.Y,0));
            var route=FarmingRoute.Build(candidates.Where(p=>!visitedStops.Contains((p.X,p.Y))),local);activeRoute=route;plot.SetRoute(route);nextStop.IsEnabled=route.Length>0;
            routeStatus.Text=route.Length==0?global::AionDpsMeter.Local.Localization.T("No tracked farming locations in this map. Track a resource first."):
                global::AionDpsMeter.Local.Localization.F($"Suggested route: {route.Length} stops (nearest 64 maximum). Follow the gold numbers. Straight lines; check terrain. Position {Math.Max(0,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-local.Timestamp)/1000:N0}s old.");
        }
        routeToggle.Click+=(_,_)=>RefreshRoute();
        nextStop.Click+=(_,_)=>{if(activeRoute.FirstOrDefault() is {} stop)visitedStops.Add((stop.X,stop.Y));RefreshRoute();};
        restartRoute.Click+=(_,_)=>{visitedStops.Clear();routeToggle.IsChecked=true;RefreshRoute();};
        var routeTimer=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromSeconds(2)};
        routeTimer.Tick+=(_,_)=>RefreshRoute();
        window.Closed+=(_,_)=>routeTimer.Stop();routeTimer.Start();
        int WorldId(CapturedPosition p)=>p.MapId switch {901=>1110,902=>1010,_=>p.MapId};
        TraceDiscoverySnapshot CurrentTraces() {
            var context=engine.CurrentTraceContext();
            return TraceDiscovery.Build(catalogue.Groups,MapWorlds.Groups,context.Achievements,context.Race);
        }
        void RefreshRows() {
            if(loading)return;
            string? selectedName=(table.SelectedItem as MarkerRow)?.Name;
            var traces=focusedTarget?.Kind=="collectable"?CurrentTraces():null;
            if(traces!=null)focusedGroups=MapTargetLocations.RemainingTraces(focusedTarget!,catalogue.Groups,traces).Select(g=>g.Id).ToHashSet();
            var saved=pins.ForWorld(currentWorld);
            var matching=focusedGroups ?? (searchResult.TotalGroups>0 ? searchResult.Groups.SelectMany(g=>g.GroupIds).ToHashSet() : null);
            var visible=MapMarkers.Visible(groups,saved,"",null,matching,g=>filters.Enabled(currentWorld,g.Type,g.Category));
            visibleGroups=visible;UpdateFilterMenu();
            table.SetRows(visible.GroupBy(g=>g.Name).Select(g=>new MarkerRow(g.First().Id,g.Key,g.First().Type,g.First().Category,g.Sum(p=>p.Count),saved.Contains(g.Key)?"Yes":"")).OrderByDescending(g=>g.Pinned.Length).ThenBy(g=>g.Name).ToArray());
            table.SelectedItem=table.Items.Cast<MarkerRow>().FirstOrDefault(r=>r.Name==selectedName);UpdateTrackingChoices();
            var ids=visible.Select(g=>g.Id).ToHashSet();var shown=points.Where(p=>ids.Contains(p.Group)).Select(p=>new PetSpawnPoint(p.Group+1,p.X,p.Y,0)).ToArray();
            plot.SetSite(new(0,LocalPositionTracker.MapKey(currentWorld)??currentWorld.ToString(),"World map",shown));
            plot.SetHighlights(visible.Where(g=>saved.Contains(g.Name)).Select(g=>g.Id+1));
            plot.SetPlayer(position!=null && WorldId(position)==currentWorld?position:null);plot.FitWithPlayer(20000);
            status.Text=global::AionDpsMeter.Local.Localization.F($"{shown.Length:N0} reference markers; {saved.Count:N0} pinned names in this map (gold).");
            if(focusedTarget!=null) {
                string targetName=focusedTarget.Kind=="collectable"?"Remaining traces":focusedTarget.Kind=="pet"?PetFarmLinks.Default.Name(focusedTarget.Id):targetIndex.Groups(focusedTarget).FirstOrDefault()?.Name??focusedTarget.Kind;
                status.Text+=global::AionDpsMeter.Local.Localization.F($" Target: {targetName} ({focusedTarget.Id}). Choose a map above to see its locations.");
            }
            if(traces!=null)status.Text+=traces.HasFullSnapshot?global::AionDpsMeter.Local.Localization.T(" Based on the full achievement list; pinned places remain visible."):global::AionDpsMeter.Local.Localization.T(" Achievement list incomplete: locations are not yet confirmed collected. Refresh after new captures; pinned places remain visible.");
            if(search.Text.Trim().Length>0)status.Text+=global::AionDpsMeter.Local.Localization.F($" Search: {searchResult.Groups.Length:N0} of {searchResult.TotalGroups:N0} groups across maps.");
            if(search.Text.Trim().Length is >0 and <3)status.Text+=global::AionDpsMeter.Local.Localization.T(" Enter at least 3 characters.");
            if(searchResult.IsLimited && searchResult.Groups.Length<searchLimit)status.Text+=global::AionDpsMeter.Local.Localization.T(" Refine your search to show more locations within the marker limit.");
            if(position!=null && WorldId(position)==currentWorld)status.Text+=global::AionDpsMeter.Local.Localization.F($" Position captured {Math.Max(0,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-position.Timestamp)/1000:N0}s ago.");
            if(filters.LoadError!=null)status.Text+=" "+filters.LoadError;
            RefreshRoute();
            if(catalogue.LoadError!=null)status.Text+=" "+catalogue.LoadError;if(pins.LoadError!=null)status.Text+=" "+pins.LoadError;
        }
        void UpdateFilterMenu() {
            foreach(var type in categoryMenu.Items.OfType<MenuItem>())foreach(var item in type.Items.OfType<MenuItem>())
                if(item.Tag is ValueTuple<string,string> key)item.IsChecked=filters.Enabled(currentWorld,key.Item1,key.Item2);
            int active=groups.Select(g=>(g.Type,g.Category)).Distinct().Count(g=>filters.Enabled(currentWorld,g.Type,g.Category));
            category.Content=global::AionDpsMeter.Local.Localization.F($"Categories ({active})");
        }
        void ChangeFilter(Action action) {
            try {action();RefreshRows();}
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidOperationException){UpdateFilterMenu();status.Text=global::AionDpsMeter.Local.Localization.T("Categories were not saved: ")+ex.Message;}
        }
        void BuildFilterMenu() {
            categoryMenu.Items.Clear();
            var all=new MenuItem {Header="All categories"};all.Click+=(_,_)=>ChangeFilter(()=>filters.SetAll(currentWorld,groups,true));categoryMenu.Items.Add(all);
            var none=new MenuItem {Header="No categories"};none.Click+=(_,_)=>ChangeFilter(()=>filters.SetAll(currentWorld,groups,false));categoryMenu.Items.Add(none);categoryMenu.Items.Add(new Separator());
            foreach(var type in groups.GroupBy(g=>g.Type).OrderBy(g=>g.Key)) {
                var parent=new MenuItem {Header=type.Key};var toggle=new MenuItem {Header="Toggle all in this type",StaysOpenOnClick=true};
                string kind=type.Key;toggle.Click+=(_,_)=>ChangeFilter(()=>filters.ToggleType(currentWorld,kind,groups));parent.Items.Add(toggle);parent.Items.Add(new Separator());
                foreach(var entry in type.GroupBy(g=>g.Category).OrderBy(g=>g.Key)) {
                    string name=entry.Key;var item=new MenuItem {Header=$"{name} ({entry.Sum(g=>g.Count):N0})",Tag=(kind,name),IsCheckable=true,StaysOpenOnClick=true};
                    item.Click+=(_,_)=>ChangeFilter(()=>filters.Toggle(currentWorld,kind,name,groups));parent.Items.Add(item);
                }
                categoryMenu.Items.Add(parent);
            }
            UpdateFilterMenu();
        }
        world.SelectionChanged+=(_,_)=>{
            if(loading)return;
            if(world.SelectedItem is not MarkerWorld selected)return;
            loading=true;currentWorld=selected.Id;groups=catalogue.Groups.Where(g=>g.World==currentWorld).ToArray();
            try {points=catalogue.LoadWorld(currentWorld);}
            catch(Exception ex) when(ex is IOException or ArgumentException){points=[];table.SetRows(Array.Empty<MarkerRow>());plot.SetSite(null);plot.SetPlayer(null);status.Text=global::AionDpsMeter.Local.Localization.T("Map coordinates could not be loaded.");loading=false;return;}
            filters.Reconcile(currentWorld,groups);BuildFilterMenu();loading=false;RefreshRows();
        };
        void FocusTarget(MapTarget? target,bool clearSearch=true) {
            var resolved=target==null?[]:target.Kind=="collectable"?MapTargetLocations.RemainingTraces(target,catalogue.Groups,CurrentTraces()):MapTargetLocations.Resolve(target,targetIndex,PetFarmLinks.Default);
            var locations=MapTargetLocations.Worlds(resolved,MapWorlds.Names);
            int? selected=target==null?currentWorld:MapTargetLocations.SelectWorld(locations,currentWorld);
            if(target?.Kind=="collectable"&&selected==null)selected=target.Id!=1&&MapWorlds.Names.ContainsKey(target.Id)?target.Id:currentWorld;
            if(target!=null&&selected==null){status.Text=global::AionDpsMeter.Local.Localization.T("No supported map locations are available for this target.");return;}
            visitedStops.Clear();routeToggle.IsChecked=target!=null;
            loading=true;focusedTarget=target;focusedGroups=target==null?null:resolved.Select(g=>g.Id).ToHashSet();
            clearTarget.Visibility=target==null?Visibility.Collapsed:Visibility.Visible;
            if(clearSearch){search.Text="";searchResult=new([],0,false);results.ItemsSource=searchResult.Groups;more.IsEnabled=false;}
            var choices=target==null?worlds:locations.Where(l=>l.CanOpen).Select(l=>new MarkerWorld(l.World,$"{l.Name} ({l.Spawns:N0} locations)")).ToArray();
            if(choices.Length==0&&target?.Kind=="collectable")choices=[new(selected!.Value,$"{MapWorlds.Names.GetValueOrDefault(selected.Value)} (0 remaining locations)")];
            world.ItemsSource=choices;loading=false;world.SelectedItem=choices.FirstOrDefault(w=>w.Id==selected)??choices.FirstOrDefault();RefreshRows();
        }
        focusMapTarget=target=>FocusTarget(target);clearTarget.Click+=(_,_)=>FocusTarget(null);
        void Search() {
            searchResult=searchIndex.Find(search.Text,searchLimit);results.ItemsSource=searchResult.Groups;
            results.SelectedIndex=searchResult.Groups.Length>0?0:-1;more.IsEnabled=searchResult.IsLimited && searchResult.Groups.Length==searchLimit;
            RefreshRows();
        }
        search.TextChanged+=(_,_)=>{if(loading)return;if(focusedTarget!=null)FocusTarget(null,false);searchLimit=50;Search();};
        more.Click+=(_,_)=>{searchLimit+=50;Search();};results.SelectionChanged+=(_,_)=>openResult.IsEnabled=results.SelectedItem is MapSearchGroup;
        openResult.Click+=(_,_)=>{if(results.SelectedItem is MapSearchGroup selected){world.SelectedItem=worlds.FirstOrDefault(w=>w.Id==selected.World);table.SelectedItem=table.Items.Cast<MarkerRow>().FirstOrDefault(r=>r.Name==selected.Name);if(table.SelectedItem!=null)table.ScrollIntoView(table.SelectedItem);}};
        void UpdateTrackButton() {
            var choice=trackChoice.SelectedItem as TargetChoice;track.IsEnabled=choice!=null;
            track.Content=choice!=null&&targetStore.Snapshot().Any(t=>t.Kind==choice.Kind&&t.Id==choice.Id)?global::AionDpsMeter.Local.Localization.T("Stop tracking"):global::AionDpsMeter.Local.Localization.T("Track selected");
        }
        void UpdateTrackingChoices() {
            var previous=trackChoice.SelectedItem as TargetChoice;
            var selected=table.SelectedItem as MarkerRow;
            var choices=selected==null?[]:MapTargetIndex.Choices(visibleGroups,selected.Name).Select(t=>new TargetChoice(t.Kind,t.Id,t.Kind=="collectable"?"Traces in current map":$"{selected.Name} - {t.Kind}")).ToArray();
            trackChoice.ItemsSource=choices;
            trackChoice.SelectedItem=choices.FirstOrDefault(c=>c.Kind==previous?.Kind&&c.Id==previous.Id)??(choices.Length==1?choices[0]:null);
            UpdateTrackButton();
        }
        trackChoice.SelectionChanged+=(_,_)=>UpdateTrackButton();
        track.Click+=(_,_)=>{
            if(trackChoice.SelectedItem is not TargetChoice choice)return;
            try {targetStore.Toggle(choice.Kind,choice.Id);UpdateTrackButton();refreshTrackedTargets?.Invoke();}
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidOperationException){status.Text=global::AionDpsMeter.Local.Localization.T("Target change could not be saved: ")+ex.Message;}
        };
        window.Activated+=(_,_)=>UpdateTrackButton();
        table.SelectionChanged+=(_,_)=>{UpdateTrackingChoices();pin.IsEnabled=table.SelectedItem is MarkerRow;pin.Content=table.SelectedItem is MarkerRow r && pins.ForWorld(currentWorld).Contains(r.Name)?global::AionDpsMeter.Local.Localization.T("Unpin selected"):global::AionDpsMeter.Local.Localization.T("Pin selected");};
        void SavePins(Action change){try{change();RefreshRows();}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidOperationException){status.Text=global::AionDpsMeter.Local.Localization.T("Pins were not changed: ")+ex.Message;}}
        pin.Click+=(_,_)=>{if(table.SelectedItem is MarkerRow row)SavePins(()=>pins.Toggle(currentWorld,row.Name));};
        clear.Click+=(_,_)=>SavePins(()=>pins.Clear(currentWorld));refresh.Click+=(_,_)=>{Position();if(focusedTarget?.Kind=="collectable")FocusTarget(focusedTarget);else RefreshRows();};fit.Click+=(_,_)=>plot.FitWithPlayer(20000);
        plot.Selected+=point=>{if(point!=null){int id=point.NpcId-1;table.SelectedItem=table.Items.Cast<MarkerRow>().FirstOrDefault(r=>r.Name==catalogue.Groups[id].Name);var identity=MapTargetIndex.Identity(catalogue.Groups[id]);trackChoice.SelectedItem=trackChoice.Items.Cast<TargetChoice>().FirstOrDefault(c=>c.Kind==identity?.Kind&&c.Id==identity.Id);if(table.SelectedItem!=null)table.ScrollIntoView(table.SelectedItem);}};
        Position();world.SelectedItem=worlds.FirstOrDefault(w=>w.Id==(position==null?1010:WorldId(position)))??worlds.FirstOrDefault();
        window.Content=layout;window.Show();
    }
}
