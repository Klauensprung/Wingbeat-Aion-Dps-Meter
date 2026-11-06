using System.IO;
using System.Windows;
using System.Windows.Controls;
namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    Window? gatheringRoutesWindow;
    Action<bool>? refreshGatheringMapVisibility;
    sealed record GatherRouteChoice(string Name, int[] Groups, int[] Targets, int[] Objects, int Locations) {
        public string Display => Name;
        internal int ItemId {get;init;}
    }
    void ShowGatheringRoutes()
    {
        if(gatheringRoutesWindow!=null){gatheringRoutesWindow.Show();gatheringRoutesWindow.Activate();return;}
        trackedTargets??=new(demoMode?null:AppDataPaths.FilePath("farming-targets.json"));
        var catalogue=MapMarkers.Default;
        var window=new Window {Title=Localization.T("Gathering routes"),Owner=this,Width=620,Height=540,MinWidth=540,MinHeight=480,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        gatheringRoutesWindow=window;UiTheme.Apply(window,preferences);
        var root=new DockPanel {Margin=new(14)};var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        header.Children.Add(new TextBlock {Text=global::AionDpsMeter.Local.Localization.T("Gathering routes"),FontSize=20,Margin=new(0,0,0,8)});
        var worlds=MapWorlds.Names.Where(w=>catalogue.Groups.Any(g=>g.World==w.Key&&g.DatabaseType=="gatherable"))
            .Select(w=>new MarkerWorld(w.Key,w.Value)).OrderBy(w=>w.Name).ToArray();
        var world=new ComboBox {ItemsSource=worlds,DisplayMemberPath="Name",Visibility=Visibility.Collapsed,Tag="Gathering route map"};header.Children.Add(world);
        var location=new TextBlock{Text=Localization.T("Waiting for your current location…"),Margin=new(0,0,0,18),TextWrapping=TextWrapping.Wrap};location.SetResourceReference(TextBlock.ForegroundProperty,"Accent");header.Children.Add(location);
        header.Children.Add(new TextBlock{Text=Localization.T("What do you want to gather?"),FontSize=14,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,6)});
        var resource=new ComboBox {DisplayMemberPath="Display",IsEditable=true,IsTextSearchEnabled=true,Margin=new(0,0,0,14),Tag="Gathering route resource"};TextSearch.SetTextPath(resource,"Name");header.Children.Add(resource);
        var actions=new WrapPanel {Margin=new(0,0,0,8)};header.Children.Add(actions);
        Button Add(string label){var b=new Button {Content=label,Margin=new(0,0,6,4)};actions.Children.Add(b);return b;}
        var start=Add("Start route");var track=Add("Track resource");var fit=Add("Fit route");
        start.Padding=new(20,10,20,10);start.SetResourceReference(Control.BackgroundProperty,"Accent");start.Foreground=System.Windows.Media.Brushes.Black;
        var stop=Add("Stop route");stop.IsEnabled=false;
        GatheringMapOverlay? mapOverlay=null;
        var overlayButton=Add("Map overlay");var lockOverlay=Add("Lock overlay");lockOverlay.IsEnabled=false;
        lockOverlay.ToolTip=global::AionDpsMeter.Local.Localization.T("Lock makes the overlay click-through. Use this button again to realign.");
        var stopLimit=new ComboBox {ItemsSource=new[]{16,32,64},SelectedIndex=0,Width=60,ToolTip=global::AionDpsMeter.Local.Localization.T("Maximum stops in the next route"),Margin=new(0,0,6,4)};actions.Children.Add(stopLimit);
        var predictionChoice=new ComboBox {ItemsSource=new[]{"Auto (median interval)","1 second","2 seconds","5 seconds"},SelectedIndex=0,Width=160,Margin=new(0,0,6,4),ToolTip=global::AionDpsMeter.Local.Localization.T("Auto measures the median of the last 9 position intervals. Prediction stops and shows Desynced when an update is overdue.")};actions.Children.Add(predictionChoice);
        double Horizon()=>predictionChoice.SelectedIndex switch {1=>1,2=>2,3=>5,_=>0};
        predictionChoice.SelectionChanged+=(_,_)=>{if(mapOverlay!=null)mapOverlay.PredictionSeconds=Horizon();};
        var advancedPanel=new StackPanel();var advancedActions=new WrapPanel();advancedPanel.Children.Add(advancedActions);
        foreach(var control in new UIElement[]{track,fit,overlayButton,lockOverlay,stopLimit,predictionChoice}){actions.Children.Remove(control);advancedActions.Children.Add(control);}
        track.Visibility=stopLimit.Visibility=predictionChoice.Visibility=Visibility.Collapsed;
        var top=new CheckBox {Content=Localization.T("Stay on top"),IsChecked=false,Margin=new(0,4,0,4)};advancedPanel.Children.Add(top);
        top.Click+=(_,_)=>window.Topmost=top.IsChecked==true;
        var feedback=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,6),Tag="Gathering route feedback"};header.Children.Add(feedback);
        var status=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new(0,4,0,8)};header.Children.Add(status);
        advancedPanel.Children.Add(new TextBlock {Text=Localization.T("Follow the gold numbers. Align the overlay with your minimap once; its position and scale are remembered. Routes are straight-line suggestions, not terrain paths."),TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,8),FontSize=11});
        var plot=new PetSpawnPlot {Height=230};advancedPanel.Children.Add(plot);
        var advanced=new Expander{Header=Localization.T("Map preview & alignment"),Content=advancedPanel,Margin=new(0,12,0,0)};
        root.Children.Add(new ScrollViewer{Content=advanced,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});window.Content=root;
        MapMarkerPoint[] mapPoints=[];PetSpawnPoint[] candidates=[],route=[];
        string? activeResource=null;int activeMap=0,activeLimit=16;
        int activeItem=0;bool replanning=false;
        RouteMovement lastMovement=new(null,false,0,false);
        RouteMovement ReadMovement(){
            if(!System.Threading.Monitor.TryEnter(engine.Gate))return lastMovement;
            try{return lastMovement=new(engine.Position.Latest,engine.Position.MovementStopped,engine.LiveSceneRevision,capture!=null||demoMode,engine.Position.RelocationRevision);}
            finally{System.Threading.Monitor.Exit(engine.Gate);}
        }
        int WorldId(int id)=>id switch {901=>1110,902=>1010,_=>id};
        void Update() {
            var movement=ReadMovement();bool fresh=RouteRelocation.Fresh(movement,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            start.IsEnabled=candidates.Length>0&&resource.SelectedItem is GatherRouteChoice&&fresh&&world.SelectedItem is MarkerWorld w&&WorldId(movement.Position!.MapId)==w.Id;overlayButton.IsEnabled=route.Length>0||mapOverlay!=null;
            bool tracked=resource.SelectedItem is GatherRouteChoice choice&&choice.Targets.All(id=>trackedTargets.Snapshot().Any(t=>t.Kind=="gatherable"&&t.Id==id));
            track.IsEnabled=resource.SelectedItem!=null&&!tracked;track.Content=tracked?global::AionDpsMeter.Local.Localization.T("Resource tracked"):global::AionDpsMeter.Local.Localization.T("Track resource");
            plot.SetPlayer(fresh?movement.Position:null);plot.SetRoute(route);
            if(mapOverlay!=null&&world.SelectedItem is MarkerWorld selected){mapOverlay.UpdateMap(selected.Id,route,candidates);lockOverlay.Content=mapOverlay.Locked?global::AionDpsMeter.Local.Localization.T("Unlock overlay"):global::AionDpsMeter.Local.Localization.T("Lock overlay");}
            status.Text=activeItem>0?Localization.F($"Gathering {activeResource}. The route stays active until you stop it."):Localization.T("Choose a material, then start. The route keeps running until you stop it.");
            if(mapOverlay!=null)mapOverlay.ResourceLabel=activeResource??"";
        }
        void SelectResource() {
            if(!replanning&&activeItem>0){activeItem=0;activeResource=null;stop.IsEnabled=false;mapOverlay?.Close();}
            route=[];feedback.Text="";
            var choice=resource.SelectedItem as GatherRouteChoice;var ids=choice?.Groups.ToHashSet()??[];
            candidates=mapPoints.Where(p=>ids.Contains(p.Group)).Select(p=>new PetSpawnPoint(p.Group+1,p.X,p.Y,0)).DistinctBy(p=>(p.X,p.Y)).ToArray();
            int map=(world.SelectedItem as MarkerWorld)?.Id??0;
            plot.SetSite(new(0,LocalPositionTracker.MapKey(map)??map.ToString(),choice?.Name??"Gathering",candidates));Update();plot.FitWithPlayer(20000);
        }
        // The origin is always the current captured player position.
        resource.SelectionChanged+=(_,_)=>SelectResource();
        world.SelectionChanged+=(_,_)=>{
            if(world.SelectedItem is not MarkerWorld selected)return;
            try{mapPoints=catalogue.LoadWorld(selected.Id);}
            catch(Exception ex)when(ex is IOException or ArgumentException){mapPoints=[];resource.ItemsSource=null;SelectResource();status.Text=global::AionDpsMeter.Local.Localization.T("Map locations could not be loaded.");return;}
            var available=catalogue.Groups.Where(g=>g.World==selected.Id&&g.DatabaseType=="gatherable").ToArray();
            var choices=ItemFarmLinks.Default.Items.Where(i=>i.Gatherables.Length>0).Select(i=>(Item:i,Groups:available.Where(g=>i.Gatherables.Contains(g.DatabaseId)).ToArray())).Where(x=>x.Groups.Length>0)
                .Select(x=>new GatherRouteChoice(ItemNames.Default.Find(x.Item.Id),x.Groups.Select(p=>p.Id).ToArray(),x.Groups.Select(p=>p.DatabaseId).Distinct().ToArray(),x.Groups.Select(p=>p.ObjectId).Distinct().ToArray(),x.Groups.Sum(p=>p.Count)){ItemId=x.Item.Id})
                .OrderByDescending(c=>c.Targets.Any(id=>trackedTargets.Snapshot().Any(t=>t.Kind=="gatherable"&&t.Id==id))).ThenBy(c=>c.Name).ToArray();
            int selectedItem=(resource.SelectedItem as GatherRouteChoice)?.ItemId??activeItem;
            resource.ItemsSource=choices;resource.SelectedItem=choices.FirstOrDefault(c=>c.ItemId==selectedItem)??choices.FirstOrDefault();
        };
        start.Click+=(_,_)=>{
            Update();if(!start.IsEnabled||world.SelectedItem is not MarkerWorld selected||resource.SelectedItem is not GatherRouteChoice choice)return;
            var movement=ReadMovement();if(!RouteRelocation.Fresh(movement,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())||movement.Position is not {} origin||WorldId(origin.MapId)!=selected.Id)return;
            try {trackedTargets.AddGatherables(choice.Targets);refreshTrackedTargets?.Invoke();}
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidOperationException){feedback.Text=global::AionDpsMeter.Local.Localization.T("Could not save resource: ")+ex.Message;return;}
            activeResource=choice.Name;activeItem=choice.ItemId;activeMap=selected.Id;activeLimit=32;
            stop.IsEnabled=true;
            route=FarmingRoute.Build(candidates,origin,activeLimit);
            feedback.Text=Localization.T("Route started from your current position. Teleports automatically refresh the route.");
            plot.SetSite(new(0,LocalPositionTracker.MapKey(selected.Id)??"", "Gathering route",candidates));
            Update();plot.FitWithPlayer(20000);
            if(mapOverlay==null)overlayButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        };
        stop.Click+=(_,_)=>{activeItem=0;activeResource=null;route=[];mapOverlay?.Close();stop.IsEnabled=false;feedback.Text=Localization.T("Route stopped.");Update();};
        fit.Click+=(_,_)=>{plot.FitWithPlayer(20000);mapOverlay?.FitRoute();};
        track.Click+=(_,_)=>{
            if(resource.SelectedItem is not GatherRouteChoice choice)return;
            try{trackedTargets.AddGatherables(choice.Targets);refreshTrackedTargets?.Invoke();Update();feedback.Text=global::AionDpsMeter.Local.Localization.T("Resource saved to tracked targets. Totals persist across fights.");}
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidOperationException){feedback.Text=global::AionDpsMeter.Local.Localization.T("Could not save resource: ")+ex.Message;}
        };
        void Replan(CapturedPosition position){
            replanning=true;try{
                if(activeResource==null||activeItem==0)return;
                int destination=WorldId(position.MapId);
                var destinationWorld=worlds.FirstOrDefault(w=>w.Id==destination);
                if(destinationWorld==null){route=[];candidates=[];mapOverlay?.UpdateMap(destination,[],[]);return;}
                var sources=ItemFarmLinks.Default.Find(activeItem)?.Gatherables??[];
                var groups=catalogue.Groups.Where(g=>g.World==destination&&g.DatabaseType=="gatherable"&&sources.Contains(g.DatabaseId)).Select(g=>g.Id).ToHashSet();
                if(groups.Count==0){world.SelectedItem=destinationWorld;resource.SelectedItem=null;route=[];candidates=[];mapOverlay?.UpdateMap(destination,[],[]);feedback.Text=Localization.T("No locations for the active material on this map. Your material selection is kept.");return;}
                MapMarkerPoint[] points;
                try{points=catalogue.LoadWorld(destination);}
                catch(Exception ex)when(ex is IOException or ArgumentException){feedback.Text=global::AionDpsMeter.Local.Localization.T("Could not refresh route locations.");return;}
                var nearby=points.Where(p=>groups.Contains(p.Group)).Select(p=>new PetSpawnPoint(p.Group+1,p.X,p.Y,0)).ToArray();
                if(nearby.Length==0)return;
                if(activeMap!=destination||world.SelectedItem is not MarkerWorld selectedWorld||selectedWorld.Id!=destination){
                    world.SelectedItem=destinationWorld;
                }
                resource.SelectedItem=resource.Items.OfType<GatherRouteChoice>().FirstOrDefault(c=>c.ItemId==activeItem);
                activeMap=destination;
                route=FarmingRoute.Build(nearby,position,activeLimit);
                plot.SetSite(new(0,LocalPositionTracker.MapKey(destination)??"","Gathering route",candidates));
                Update();plot.FitWithPlayer(20000);
                feedback.Text=global::AionDpsMeter.Local.Localization.T("Route refreshed to the nearest resource locations at your new position.");
            }finally{replanning=false;}
        }
        overlayButton.Click+=(_,_)=>{
            if(mapOverlay!=null){mapOverlay.Close();return;}
            mapOverlay=new GatheringMapOverlay(demoMode?null:AppDataPaths.FilePath("gathering-map-alignment.json"),ReadMovement){PredictionSeconds=Horizon()};
            refreshGatheringMapVisibility=mapOverlay.SetForegroundAllowed;
            mapOverlay.Closed+=(_,_)=>{refreshGatheringMapVisibility=null;mapOverlay=null;lockOverlay.IsEnabled=false;overlayButton.Content=global::AionDpsMeter.Local.Localization.T("Map overlay");};
            lockOverlay.IsEnabled=true;overlayButton.Content=global::AionDpsMeter.Local.Localization.T("Hide overlay");Update();
            refreshGatheringMapVisibility(!overlaysHidden&&(demoMode||GameForeground.AllowsOverlays()));
        };
        lockOverlay.Click+=(_,_)=>{if(mapOverlay!=null){mapOverlay.SetLocked(!mapOverlay.Locked);lockOverlay.Content=mapOverlay.Locked?global::AionDpsMeter.Local.Localization.T("Unlock overlay"):global::AionDpsMeter.Local.Localization.T("Lock overlay");}};
        var relocation=new RouteRelocation();
        var refresh=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};
        void RefreshLocation(){
            var movement=ReadMovement();long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            bool fresh=RouteRelocation.Fresh(movement,now);
            var current=fresh?worlds.FirstOrDefault(w=>w.Id==WorldId(movement.Position!.MapId)):null;
            location.Text=!fresh?Localization.T("Waiting for your current location. Move a little after teleporting."):
                current==null?Localization.T("No gathering locations are available on this map."):Localization.F($"Current map: {current.Name}");
            if(fresh){
                if(activeItem==0&&!Equals(world.SelectedItem,current)){world.SelectedItem=current;if(current==null){resource.ItemsSource=null;mapPoints=[];SelectResource();}}
                var relocated=relocation.Observe(movement);
                if(relocated!=null)Replan(relocated);
            }
            start.IsEnabled=fresh&&current!=null&&world.SelectedItem is MarkerWorld selected&&selected.Id==current.Id&&candidates.Length>0&&resource.SelectedItem is GatherRouteChoice;
            if(mapOverlay!=null)mapOverlay.ResourceLabel=activeResource??"";
        }
        refresh.Tick+=(_,_)=>RefreshLocation();
        window.Closing+=(_,e)=>{if(mapOverlay!=null&&!shuttingDown){e.Cancel=true;window.Hide();}};
        window.Closed+=(_,_)=>{refresh.Stop();mapOverlay?.Close();gatheringRoutesWindow=null;};
        RefreshLocation();Update();refresh.Start();
        window.Show();
    }
}
