using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
namespace AionDpsMeter.Local;

internal sealed class GatheringMapOverlay : Window
{
    sealed record Alignment(double Left,double Top,double Width,double Height,MapViewportState View,bool Locked=false);
    readonly System.Windows.Threading.DispatcherTimer saveTimer=new(){Interval=TimeSpan.FromMilliseconds(400)};
    bool restoring;
    void QueueSave(){if(restoring||closed||map<=0)return;saveTimer.Stop();saveTimer.Start();}
    readonly PetSpawnPlot plot=new(){OverlayOnly=true,AlignmentGuide=true,FollowPlayer=true,ShowPointsOnOverlay=true};
    readonly TextBlock heading=new(){Text=global::AionDpsMeter.Local.Localization.T("Align the center with your player | Wheel to match scale | Resize corner"),Foreground=Brushes.White,Background=UiTheme.Brush("#D0101823"),Padding=new(8)};
    readonly Thumb resize=new(){Width=24,Height=24,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Cursor=System.Windows.Input.Cursors.SizeNWSE,Background=Brushes.Gold};
    readonly string? path;
    readonly Dictionary<int,Alignment> saved=new();
    int map;
    readonly Func<RouteMovement>? readMovement;
    readonly RouteDeadReckoning prediction=new();
    readonly RouteRelocation relocation=new();
    internal event Action<CapturedPosition>? Relocated;
    readonly TextBlock movementStatus=new(){Foreground=Brushes.White,Background=UiTheme.Brush("#B0101823"),Padding=new(4),FontSize=10,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom,IsHitTestVisible=false};
    RouteMovement sample=new(null,false,0,false);
    long sampledAt;
    bool rendering,closed;
    internal void SetForegroundAllowed(bool allowed){
        if(closed)return;
        allowed &= OverlayHideControls.Allows(this);
        if(allowed&&!IsVisible)Show();
        else if(!allowed&&IsVisible)Hide();
    }
    internal double PredictionSeconds {get;set;}
    internal string ResourceLabel {get;set;}="";
    void RenderMovement(object? sender,EventArgs e){
        if(readMovement==null)return;
        long tick=Environment.TickCount64;
        if(tick-sampledAt>=32){sample=readMovement();sampledAt=tick;
            if(relocation.Observe(sample) is {} position){prediction.Clear();Relocated?.Invoke(position);}}
        plot.SetPlayer(prediction.Frame(sample,map,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),PredictionSeconds));
        movementStatus.Text=(ResourceLabel.Length>0?ResourceLabel+" · ":"")+prediction.Status;
    }
    void SetRendering(bool enabled){if(rendering==enabled)return;rendering=enabled;if(enabled)CompositionTarget.Rendering+=RenderMovement;else CompositionTarget.Rendering-=RenderMovement;}
    internal bool RenderingActive=>rendering;
    PetSpawnPoint[] currentRoute=[];
    PetSpawnPoint[] selectedPoints=[];
    internal void FitRoute(){plot.SetSite(new(0,LocalPositionTracker.MapKey(map)??"", "Route alignment",currentRoute));plot.FitWithPlayer(20000);plot.SetPoints(selectedPoints);QueueSave();}
    internal bool Locked {get;private set;}
    internal bool NativeVisible=>IsWindowVisible(new WindowInteropHelper(this).Handle);
    internal bool RouteVisible=>plot.IsVisible&&plot.RouteCount>0;
    internal int StopCount=>plot.RouteCount;
    internal int SecondStopCount=>plot.SecondRouteCount;
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] static extern bool IsWindowVisible(IntPtr handle);
    internal bool ClickThrough => (GetStyle(new WindowInteropHelper(this).Handle,-20)&0x20)!=0;
    internal MapViewportState AlignmentView=>plot.Viewport;
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int GetStyle(IntPtr h,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongW")] static extern int SetStyle(IntPtr h,int index,int value);
    internal GatheringMapOverlay(string? path,Func<RouteMovement>? readMovement=null) {
        this.readMovement=readMovement;
        this.path=path;
        Title=global::AionDpsMeter.Local.Localization.T("Gathering map alignment");Width=700;Height=550;MinWidth=220;MinHeight=180;
        WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=false;ShowActivated=false;
        var grid=new Grid();grid.Children.Add(plot);heading.VerticalAlignment=VerticalAlignment.Top;grid.Children.Add(heading);grid.Children.Add(resize);grid.Children.Add(movementStatus);Content=grid;
        heading.MouseLeftButtonDown+=(_,_)=>{if(!Locked)DragMove();};
        resize.DragDelta+=(_,e)=>{Width=Math.Clamp(ActualWidth+e.HorizontalChange,MinWidth,4000);Height=Math.Clamp(ActualHeight+e.VerticalChange,MinHeight,4000);};
        try{if(path!=null&&File.Exists(path)&&new FileInfo(path).Length<65536){var data=JsonSerializer.Deserialize<Dictionary<int,Alignment>>(File.ReadAllText(path));if(data?.Count<=32)foreach(var p in data){
            if(p.Value==null||p.Value.View==null)continue;
            var validator=new PetSpawnViewport();validator.Restore(p.Value.View);
            if(p.Key>=0&&new[]{p.Value.Left,p.Value.Top,p.Value.Width,p.Value.Height}.All(double.IsFinite)&&p.Value.Width is >=220 and <=4000&&p.Value.Height is >=180 and <=4000)saved[p.Key]=p.Value;
        }}}catch(Exception ex)when(ex is IOException or JsonException or ArgumentException or UnauthorizedAccessException){saved.Clear();LocalLog.Write("Map alignment unavailable: "+ex.Message);}
        OverlayHideControls.Attach(this);
        saveTimer.Tick+=(_,_)=>{saveTimer.Stop();Save();};
        LocationChanged+=(_,_)=>QueueSave();SizeChanged+=(_,_)=>QueueSave();
        PreviewMouseWheel+=(_,_)=>QueueSave();
        SourceInitialized+=(_,_)=>SetLocked(Locked);
        IsVisibleChanged+=(_,_)=>{SetRendering(IsVisible);if(!IsVisible)Save();};
        Closed+=(_,_)=>{closed=true;saveTimer.Stop();SetRendering(false);Save();};
    }
    internal void UpdateMap(int world,PetSpawnPoint[] route,PetSpawnPoint[]? gatherables=null,PetSpawnPoint[]? secondRoute=null) {
        if(map!=world){Save();restoring=true;try{prediction.Clear();map=world;plot.SetSite(new(0,LocalPositionTracker.MapKey(world)??"", "Route alignment",route));plot.FitWithPlayer(20000);
            if(saved.TryGetValue(world,out var a)||saved.TryGetValue(0,out a)){Width=a.Width;Height=a.Height;
                Left=Math.Clamp(a.Left,SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+Math.Max(0,SystemParameters.VirtualScreenWidth-Width));
                Top=Math.Clamp(a.Top,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+Math.Max(0,SystemParameters.VirtualScreenHeight-Height));plot.RestoreViewport(a.View);SetLocked(a.Locked);}
            else SetLocked(false);
        }finally{restoring=false;}}
        currentRoute=[..route,..secondRoute??[]];selectedPoints=gatherables??currentRoute;plot.SetPoints(selectedPoints);plot.SetRoute(route);plot.SetSecondRoute(secondRoute??[]);plot.Visibility=Visibility.Visible;
    }
    internal void SetLocked(bool value) {
        Locked=value;plot.AlignmentGuide=!value;plot.IsHitTestVisible=!value;heading.Visibility=resize.Visibility=value?Visibility.Hidden:Visibility.Visible;
        var handle=new WindowInteropHelper(this).Handle;if(handle!=IntPtr.Zero){int style=GetStyle(handle,-20);SetStyle(handle,-20,value?style|0x20:style&~0x20);}
        plot.InvalidateVisual();Save();
    }
    internal void SetupSample(){heading.Visibility=Visibility.Collapsed;movementStatus.Visibility=Visibility.Collapsed;resize.Visibility=Visibility.Collapsed;}
    internal void ApplySetupBounds(Rect bounds){
        Left=bounds.X;Top=bounds.Y;Width=Math.Clamp(bounds.Width,MinWidth,4000);Height=Math.Clamp(bounds.Height,MinHeight,4000);
        foreach(int key in saved.Keys.ToArray())saved[key]=saved[key] with{Left=Left,Top=Top,Width=Width,Height=Height};
        Save();
    }
    void Save(){
        if(restoring||map<=0||path==null||!double.IsFinite(Left)||!double.IsFinite(Top))return;
        saved[map]=new(Left,Top,Width,Height,plot.Viewport,Locked);
        saved[0]=saved[map];
        if(saved.Count>32)saved.Remove(saved.Keys.First(key=>key!=0&&key!=map));
        try{Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);File.WriteAllText(path+".tmp",JsonSerializer.Serialize(saved));File.Move(path+".tmp",path,true);}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){LocalLog.Write("Could not save map alignment: "+ex.Message);}
    }
}
