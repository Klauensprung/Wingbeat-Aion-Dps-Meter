using System.IO;
using System.Windows;
using System.Windows.Controls;
namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    Window? gatheringRoutesWindow;
    Action<bool>? refreshGatheringMapVisibility;
    sealed record GatherRouteChoice(string Name, int[] Groups, int[] Targets, int[] Objects, int Locations) {
        public string Display => $"{Name} ({Locations} locations)";
    }
    void ShowGatheringRoutes()
    {
        if(gatheringRoutesWindow!=null){gatheringRoutesWindow.Show();gatheringRoutesWindow.Activate();return;}
        trackedTargets??=new(demoMode?null:AppDataPaths.FilePath("farming-targets.json"));
        var catalogue=MapMarkers.Default;
        var window=new Window {Title="Gathering routes",Owner=this,Width=760,Height=660,MinWidth=580,MinHeight=520,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        gatheringRoutesWindow=window;UiTheme.Apply(window,preferences);
        var root=new DockPanel {Margin=new(14)};var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        header.Children.Add(new TextBlock {Text="Gathering routes",FontSize=20,Margin=new(0,0,0,8)});
        var worlds=MapWorlds.Names.Where(w=>catalogue.Groups.Any(g=>g.World==w.Key&&g.DatabaseType=="gatherable"))
            .Select(w=>new MarkerWorld(w.Key,w.Value)).OrderBy(w=>w.Name).ToArray();
        var world=new ComboBox {ItemsSource=worlds,DisplayMemberPath="Name",Margin=new(0,0,0,6),Tag="Gathering route map"};header.Children.Add(world);
        var resource=new ComboBox {DisplayMemberPath="Display",Margin=new(0,0,0,6),Tag="Gathering route resource"};header.Children.Add(resource);
        var actions=new WrapPanel {Margin=new(0,0,0,8)};header.Children.Add(actions);
        Button Add(string label){var b=new Button {Content=label,Margin=new(0,0,6,4)};actions.Children.Add(b);return b;}
        var start=Add("Start route");var track=Add("Track resource");var fit=Add("Fit route");
        GatheringMapOverlay? mapOverlay=null;
        var overlayButton=Add("Map overlay");var lockOverlay=Add("Lock overlay");lockOverlay.IsEnabled=false;
        lockOverlay.ToolTip="Lock makes the overlay click-through. Use this button again to realign.";
        var stopLimit=new ComboBox {ItemsSource=new[]{16,32,64},SelectedIndex=0,Width=60,ToolTip="Maximum stops in the next route",Margin=new(0,0,6,4)};actions.Children.Add(stopLimit);
        var predictionChoice=new ComboBox {ItemsSource=new[]{"Auto (median interval)","1 second","2 seconds","5 seconds"},SelectedIndex=0,Width=160,Margin=new(0,0,6,4),ToolTip="Auto measures the median of the last 9 position intervals. Prediction stops and shows Desynced when an update is overdue."};actions.Children.Add(predictionChoice);
        double Horizon()=>predictionChoice.SelectedIndex switch {1=>1,2=>2,3=>5,_=>0};
        predictionChoice.SelectionChanged+=(_,_)=>{if(mapOverlay!=null)mapOverlay.PredictionSeconds=Horizon();};
        var top=new CheckBox {Content="Stay on top",IsChecked=true,Margin=new(0,4,0,4)};header.Children.Add(top);window.Topmost=true;
        top.Click+=(_,_)=>window.Topmost=top.IsChecked==true;
        var feedback=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,6),Tag="Gathering route feedback"};header.Children.Add(feedback);
        var status=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new(0,4,0,8)};header.Children.Add(status);
        header.Children.Add(new TextBlock {Text="Follow the gold numbers. Suggested straight-line route; check terrain and node availability. Align the overlay center with your minimap player marker and match its zoom. Movement is predicted for the measured median update interval, then held as Desynced until a fresh position arrives.",TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,8),FontSize=11});
        var plot=new PetSpawnPlot {MinHeight=180};root.Children.Add(plot);window.Content=root;
        MapMarkerPoint[] mapPoints=[];PetSpawnPoint[] candidates=[],route=[];
        PetSpawnPoint? chosenStart=null;
        string? activeResource=null;int activeMap=0,activeLimit=16;
        int WorldId(int id)=>id switch {901=>1110,902=>1010,_=>id};
        void Update() {
            start.IsEnabled=candidates.Length>0;overlayButton.IsEnabled=route.Length>0||mapOverlay!=null;
            bool tracked=resource.SelectedItem is GatherRouteChoice choice&&choice.Targets.All(id=>trackedTargets.Snapshot().Any(t=>t.Kind=="gatherable"&&t.Id==id));
            track.IsEnabled=resource.SelectedItem!=null&&!tracked;track.Content=tracked?"Resource tracked":"Track resource";
            plot.SetPlayer(null);plot.SetRoute(route);
            if(mapOverlay!=null&&world.SelectedItem is MarkerWorld selected){mapOverlay.UpdateMap(selected.Id,route,candidates);lockOverlay.Content=mapOverlay.Locked?"Unlock overlay":"Lock overlay";}
            status.Text=route.Length==0?$"{candidates.Length} known locations. Click a marker to choose a starting point, then Start route.":$"{route.Length} route stops. The route stays visible until you hide it. Player-following uses predicted movement. Stops are not completed automatically.";
        }
        void SelectResource() {
            route=[];chosenStart=null;feedback.Text="";
            var choice=resource.SelectedItem as GatherRouteChoice;var ids=choice?.Groups.ToHashSet()??[];
            candidates=mapPoints.Where(p=>ids.Contains(p.Group)).Select(p=>new PetSpawnPoint(p.Group+1,p.X,p.Y,0)).DistinctBy(p=>(p.X,p.Y)).ToArray();
            int map=(world.SelectedItem as MarkerWorld)?.Id??0;
            plot.SetSite(new(0,LocalPositionTracker.MapKey(map)??map.ToString(),choice?.Name??"Gathering",candidates));Update();plot.FitWithPlayer(20000);
        }
        plot.Selected+=point=>{if(point!=null){chosenStart=point;feedback.Text="Starting point selected. Click Start route to plan from this marker.";}};
        resource.SelectionChanged+=(_,_)=>SelectResource();
        world.SelectionChanged+=(_,_)=>{
            if(world.SelectedItem is not MarkerWorld selected)return;
            try{mapPoints=catalogue.LoadWorld(selected.Id);}
            catch(Exception ex)when(ex is IOException or ArgumentException){mapPoints=[];resource.ItemsSource=null;SelectResource();status.Text="Map locations could not be loaded.";return;}
            var choices=catalogue.Groups.Where(g=>g.World==selected.Id&&g.DatabaseType=="gatherable").GroupBy(g=>g.Name)
                .Select(g=>new GatherRouteChoice(g.Key,g.Select(p=>p.Id).ToArray(),g.Select(p=>p.DatabaseId).Distinct().ToArray(),g.Select(p=>p.ObjectId).Distinct().ToArray(),g.Sum(p=>p.Count)))
                .OrderByDescending(c=>c.Targets.Any(id=>trackedTargets.Snapshot().Any(t=>t.Kind=="gatherable"&&t.Id==id))).ThenBy(c=>c.Name).ToArray();
            resource.ItemsSource=choices;resource.SelectedIndex=choices.Length>0?0:-1;
        };
        start.Click+=(_,_)=>{
            Update();if(!start.IsEnabled||world.SelectedItem is not MarkerWorld selected||resource.SelectedItem is not GatherRouteChoice choice)return;
            var point=chosenStart??candidates[0];
            var origin=new CapturedPosition(selected.Id,point.X,point.Y,point.Z,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            lock(engine.Gate)if(chosenStart==null&&engine.Position.Latest is {} current&&WorldId(current.MapId)==selected.Id)origin=current;
            try {trackedTargets.AddGatherables(choice.Targets);refreshTrackedTargets?.Invoke();}
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidOperationException){feedback.Text="Could not save resource: "+ex.Message;return;}
            activeResource=choice.Name;activeMap=selected.Id;activeLimit=(int)stopLimit.SelectedItem;
            route=FarmingRoute.Build(candidates,origin,activeLimit);
            feedback.Text="Route ready. Open Map overlay, align it with your minimap, then lock it.";
            plot.SetSite(new(0,LocalPositionTracker.MapKey(selected.Id)??"", "Gathering route",candidates));
            Update();plot.FitWithPlayer(20000);
        };
        fit.Click+=(_,_)=>{plot.FitWithPlayer(20000);mapOverlay?.FitRoute();};
        track.Click+=(_,_)=>{
            if(resource.SelectedItem is not GatherRouteChoice choice)return;
            try{trackedTargets.AddGatherables(choice.Targets);refreshTrackedTargets?.Invoke();Update();feedback.Text="Resource saved to tracked targets. Totals persist across fights.";}
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidOperationException){feedback.Text="Could not save resource: "+ex.Message;}
        };
        overlayButton.Click+=(_,_)=>{
            if(mapOverlay!=null){mapOverlay.Close();return;}
            RouteMovement last=new(null,false,0,false);
            RouteMovement ReadMovement(){
                if(!System.Threading.Monitor.TryEnter(engine.Gate))return last;
                try{return last=new(engine.Position.Latest,engine.Position.MovementStopped,engine.LiveSceneRevision,capture!=null,engine.Position.RelocationRevision);}
                finally{System.Threading.Monitor.Exit(engine.Gate);}
            }
            mapOverlay=new GatheringMapOverlay(demoMode?null:AppDataPaths.FilePath("gathering-map-alignment.json"),ReadMovement){PredictionSeconds=Horizon()};
            mapOverlay.Relocated+=position=>{
                if(activeResource==null)return;
                int destination=WorldId(position.MapId);
                var destinationWorld=worlds.FirstOrDefault(w=>w.Id==destination);
                if(destinationWorld==null)return;
                var groups=catalogue.Groups.Where(g=>g.World==destination&&g.DatabaseType=="gatherable"&&g.Name==activeResource).Select(g=>g.Id).ToHashSet();
                if(groups.Count==0){feedback.Text="No locations for the active resource on this map.";return;}
                MapMarkerPoint[] points;
                try{points=catalogue.LoadWorld(destination);}
                catch(Exception ex)when(ex is IOException or ArgumentException){feedback.Text="Could not refresh route locations.";return;}
                var nearby=points.Where(p=>groups.Contains(p.Group)).Select(p=>new PetSpawnPoint(p.Group+1,p.X,p.Y,0)).ToArray();
                if(nearby.Length==0)return;
                if(activeMap!=destination||world.SelectedItem is not MarkerWorld selectedWorld||selectedWorld.Id!=destination){
                    world.SelectedItem=destinationWorld;
                }
                resource.SelectedItem=resource.Items.OfType<GatherRouteChoice>().FirstOrDefault(c=>c.Name==activeResource);
                activeMap=destination;chosenStart=null;
                route=FarmingRoute.Build(nearby,position,activeLimit);
                plot.SetSite(new(0,LocalPositionTracker.MapKey(destination)??"","Gathering route",candidates));
                Update();plot.FitWithPlayer(20000);
                feedback.Text="Route refreshed to the nearest resource locations at your new position.";
            };
            refreshGatheringMapVisibility=mapOverlay.SetForegroundAllowed;
            mapOverlay.Closed+=(_,_)=>{refreshGatheringMapVisibility=null;mapOverlay=null;lockOverlay.IsEnabled=false;overlayButton.Content="Map overlay";};
            lockOverlay.IsEnabled=true;overlayButton.Content="Hide overlay";Update();
            refreshGatheringMapVisibility(!overlaysHidden&&(demoMode||GameForeground.AllowsOverlays()));
        };
        lockOverlay.Click+=(_,_)=>{if(mapOverlay!=null){mapOverlay.SetLocked(!mapOverlay.Locked);lockOverlay.Content=mapOverlay.Locked?"Unlock overlay":"Lock overlay";}};
        window.Closing+=(_,e)=>{if(mapOverlay!=null&&!shuttingDown){e.Cancel=true;window.Hide();}};
        window.Closed+=(_,_)=>{mapOverlay?.Close();gatheringRoutesWindow=null;};
        int initialMap;lock(engine.Gate)initialMap=engine.Position.Latest?.MapId??engine.Spatial.MapId;
        world.SelectedItem=worlds.FirstOrDefault(w=>w.Id==WorldId(initialMap))??worlds.FirstOrDefault(w=>w.Id==1010)??worlds.FirstOrDefault();
        window.Show();
    }
}
