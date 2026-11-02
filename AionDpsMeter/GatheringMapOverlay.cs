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
    sealed record Alignment(double Left,double Top,double Width,double Height,MapViewportState View);
    readonly PetSpawnPlot plot=new(){OverlayOnly=true,AlignmentGuide=true,FollowPlayer=true};
    readonly TextBlock heading=new(){Text="Align the center with your player | Wheel to match scale | Resize corner",Foreground=Brushes.White,Background=UiTheme.Brush("#D0101823"),Padding=new(8)};
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
        if(allowed&&!IsVisible)Show();
        else if(!allowed&&IsVisible)Hide();
    }
    internal double PredictionSeconds {get;set;}
    void RenderMovement(object? sender,EventArgs e){
        if(readMovement==null)return;
        long tick=Environment.TickCount64;
        if(tick-sampledAt>=32){sample=readMovement();sampledAt=tick;
            if(relocation.Observe(sample) is {} position){prediction.Clear();Relocated?.Invoke(position);}}
        plot.SetPlayer(prediction.Frame(sample,map,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),PredictionSeconds));
        movementStatus.Text=prediction.Status;
    }
    void SetRendering(bool enabled){if(rendering==enabled)return;rendering=enabled;if(enabled)CompositionTarget.Rendering+=RenderMovement;else CompositionTarget.Rendering-=RenderMovement;}
    internal bool RenderingActive=>rendering;
    PetSpawnPoint[] currentRoute=[];
    internal void FitRoute(){plot.SetSite(new(0,LocalPositionTracker.MapKey(map)??"", "Route alignment",currentRoute));plot.FitWithPlayer(20000);}
    internal bool Locked {get;private set;}
    internal bool NativeVisible=>IsWindowVisible(new WindowInteropHelper(this).Handle);
    internal bool RouteVisible=>plot.IsVisible&&plot.RouteCount>0;
    internal int StopCount=>plot.RouteCount;
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] static extern bool IsWindowVisible(IntPtr handle);
    internal bool ClickThrough => (GetStyle(new WindowInteropHelper(this).Handle,-20)&0x20)!=0;
    internal MapViewportState AlignmentView=>plot.Viewport;
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int GetStyle(IntPtr h,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongW")] static extern int SetStyle(IntPtr h,int index,int value);
    internal GatheringMapOverlay(string? path,Func<RouteMovement>? readMovement=null) {
        this.readMovement=readMovement;
        this.path=path;
        Title="Gathering map alignment";Width=700;Height=550;MinWidth=220;MinHeight=180;
        WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=false;ShowActivated=false;
        var grid=new Grid();grid.Children.Add(plot);heading.VerticalAlignment=VerticalAlignment.Top;grid.Children.Add(heading);grid.Children.Add(resize);grid.Children.Add(movementStatus);Content=grid;
        heading.MouseLeftButtonDown+=(_,_)=>{if(!Locked)DragMove();};
        resize.DragDelta+=(_,e)=>{Width=Math.Clamp(ActualWidth+e.HorizontalChange,MinWidth,4000);Height=Math.Clamp(ActualHeight+e.VerticalChange,MinHeight,4000);};
        try{if(path!=null&&File.Exists(path)&&new FileInfo(path).Length<65536){var data=JsonSerializer.Deserialize<Dictionary<int,Alignment>>(File.ReadAllText(path));if(data?.Count<=32)foreach(var p in data){
            if(p.Value==null||p.Value.View==null)continue;
            var validator=new PetSpawnViewport();validator.Restore(p.Value.View);
            if(p.Key>0&&new[]{p.Value.Left,p.Value.Top,p.Value.Width,p.Value.Height}.All(double.IsFinite)&&p.Value.Width is >=220 and <=4000&&p.Value.Height is >=180 and <=4000)saved[p.Key]=p.Value;
        }}}catch(Exception ex)when(ex is IOException or JsonException or ArgumentException or UnauthorizedAccessException){saved.Clear();LocalLog.Write("Map alignment unavailable: "+ex.Message);}
        IsVisibleChanged+=(_,_)=>SetRendering(IsVisible);
        Closed+=(_,_)=>{closed=true;SetRendering(false);Save();};
    }
    internal void UpdateMap(int world,PetSpawnPoint[] route) {
        if(map!=world){Save();prediction.Clear();map=world;SetLocked(false);plot.SetSite(new(0,LocalPositionTracker.MapKey(world)??"", "Route alignment",route));plot.FitWithPlayer(20000);
            if(saved.TryGetValue(world,out var a)){Width=a.Width;Height=a.Height;
                Left=Math.Clamp(a.Left,SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+Math.Max(0,SystemParameters.VirtualScreenWidth-Width));
                Top=Math.Clamp(a.Top,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+Math.Max(0,SystemParameters.VirtualScreenHeight-Height));plot.RestoreViewport(a.View);}}
        currentRoute=route;plot.SetRoute(route);plot.Visibility=Visibility.Visible;
    }
    internal void SetLocked(bool value) {
        Locked=value;plot.AlignmentGuide=!value;plot.IsHitTestVisible=!value;heading.Visibility=resize.Visibility=value?Visibility.Hidden:Visibility.Visible;
        var handle=new WindowInteropHelper(this).Handle;if(handle!=IntPtr.Zero){int style=GetStyle(handle,-20);SetStyle(handle,-20,value?style|0x20:style&~0x20);}
        plot.InvalidateVisual();if(value)Save();
    }
    void Save(){
        if(map<=0||path==null)return;
        saved[map]=new(Left,Top,ActualWidth>0?ActualWidth:Width,ActualHeight>0?ActualHeight:Height,plot.Viewport);
        if(saved.Count>32)saved.Remove(saved.Keys.First());
        try{Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);File.WriteAllText(path+".tmp",JsonSerializer.Serialize(saved));File.Move(path+".tmp",path,true);}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){LocalLog.Write("Could not save map alignment: "+ex.Message);}
    }
}
