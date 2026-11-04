using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AionDpsMeter.Local;

internal static class OverlayScale
{
    internal static readonly (string Key,string Label)[] Options = [
        ("DPS1","DPS1"),("DPS2","DPS2"),(nameof(CooldownOverlayWindow),"Cooldowns and buff uptime"),
        (nameof(BossAlertOverlayWindow),"Boss warnings"),(nameof(ControlOverlayWindow),"Control alerts"),
        (nameof(PingOverlayWindow),"Ping"),(nameof(LootOverlayWindow),"Loot history"),
        (nameof(FarmingOverlayWindow),"Farming"),(nameof(CurrencyOverlayWindow),"Currency"),
        (nameof(TicketOverlayWindow),"Tickets"),(nameof(PetTrackerOverlayWindow),"Pet gathering"),
        (nameof(PetMapOverlayWindow),"Pet map"),(nameof(GatheringMapOverlay),"Gathering map")];
    static readonly Dictionary<string,double> values=Load();
    static readonly DispatcherTimer saveTimer=new(){Interval=TimeSpan.FromMilliseconds(400)};
    static event Action? Changed;
    static string PathName=>AppDataPaths.FilePath("overlay-scales.json");
    static OverlayScale(){saveTimer.Tick+=(_,_)=>Save();}
    internal static double Normalize(double value)=>double.IsFinite(value)?Math.Clamp(Math.Round(value/5)*5,50,200):100;
    internal static string Key(Window window)=>window is OverlayWindow meter ? meter.MeterLabel=="DPS2"?"DPS2":"DPS1" : window.GetType().Name;
    internal static double Get(string key)=>values.GetValueOrDefault(key,100);
    internal static void Set(string key,double value)
    {
        if(!Options.Any(o=>o.Key==key))return;
        value=Normalize(value);if(Get(key)==value)return;
        values[key]=value;Changed?.Invoke();saveTimer.Stop();saveTimer.Start();
    }
    static Dictionary<string,double> Load()
    {
        try {if(File.Exists(PathName)&&new FileInfo(PathName).Length<16384)
            return (JsonSerializer.Deserialize<Dictionary<string,double>>(File.ReadAllText(PathName))??new())
                .Where(p=>Options.Any(o=>o.Key==p.Key)).ToDictionary(p=>p.Key,p=>Normalize(p.Value));}
        catch(Exception ex)when(ex is IOException or JsonException or UnauthorizedAccessException){LocalLog.Write("Overlay scales unavailable: "+ex.Message);}
        return new();
    }
    static void Save()
    {
        saveTimer.Stop();
        try {Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathName)!);File.WriteAllText(PathName+".tmp",JsonSerializer.Serialize(values));File.Move(PathName+".tmp",PathName,true);}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){LocalLog.Write("Could not save overlay scales: "+ex.Message);}
    }
    internal static FrameworkElement Editor(string key)
    {
        var row=new DockPanel{Margin=new(4),MinWidth=210};
        var label=new TextBlock{Width=48,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(label,Dock.Right);row.Children.Add(label);
        var reset=new Button{Content="100%",ToolTip="Reset scale",Padding=new(5,2,5,2),Margin=new(6,0,6,0)};
        DockPanel.SetDock(reset,Dock.Right);row.Children.Add(reset);reset.Click+=(_,_)=>Set(key,100);
        var slider=new Slider{Minimum=50,Maximum=200,TickFrequency=5,IsSnapToTickEnabled=true,SmallChange=5,LargeChange=25,MinWidth=110,VerticalAlignment=VerticalAlignment.Center,ToolTip="Scale text, icons and controls"};row.Children.Add(slider);
        bool updating=false;
        void Refresh(){updating=true;slider.Value=Get(key);label.Text=$"{Get(key):0}%";updating=false;}
        slider.ValueChanged+=(_,_)=>{if(!updating)Set(key,slider.Value);};
        row.Loaded+=(_,_)=>{Changed-=Refresh;Changed+=Refresh;Refresh();};row.Unloaded+=(_,_)=>Changed-=Refresh;
        Refresh();return row;
    }
    internal static FrameworkElement Settings()
    {
        var panel=new StackPanel();
        panel.Children.Add(new TextBlock{Text="Scale text, icons and controls independently for each overlay. Window size stays separate.",TextWrapping=TextWrapping.Wrap,Margin=new(8)});
        foreach(var option in Options){panel.Children.Add(new TextBlock{Text=option.Label,Margin=new(8,10,8,0)});panel.Children.Add(Editor(option.Key));}
        return panel;
    }
    internal static void Attach(Window window,FrameworkElement shell,Panel controls)
    {
        var transform=new ScaleTransform(1,1);shell.LayoutTransform=transform;
        var button=new Button{Content="Scale 100%",FontSize=10,Padding=new(5,1,5,1),Margin=new(2),Focusable=false,ToolTip="Scale this overlay's text, icons and controls"};controls.Children.Insert(0,button);
        void Refresh(){double scale=Get(Key(window));transform.ScaleX=transform.ScaleY=scale/100;button.Content=$"Scale {scale:0}%";}
        button.Click+=(_,_)=>{
            var menu=UiTheme.Menu(window,button);
            menu.Items.Add(new MenuItem{Header=Editor(Key(window)),StaysOpenOnClick=true});
            button.ContextMenu=menu;menu.IsOpen=true;
        };
        // DPS2 is assigned after its window constructor; resolve the key on load.
        window.Loaded+=(_,_)=>{Changed-=Refresh;Changed+=Refresh;Refresh();};
        window.Closed+=(_,_)=>{Changed-=Refresh;Save();};
    }
}
