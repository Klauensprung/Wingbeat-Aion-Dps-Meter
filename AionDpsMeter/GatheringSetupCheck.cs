using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace AionDpsMeter.Local;

internal static class GatheringSetupCheck
{
    internal static int Run(Application app){
        app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var main=new MainWindow(true){Left=-32000,Top=-32000,ShowActivated=false};
        try{main.CheckGatheringSetup();File.WriteAllText("gathering-setup-results.txt","PASS fresh-location gating, material selection, automatic overlay, teleport refresh, continuous gathering and explicit stop.");return 0;}
        catch(Exception ex){File.WriteAllText("gathering-setup-error.txt",ex.ToString());return 1;}
        finally{main.Close();}
    }
}
public sealed partial class MainWindow
{
    internal void CheckGatheringSetup(){
        Show();timer.Stop();engine=new CombatEngine();trackedTargets=new();ShowGatheringRoutes();
        var window=gatheringRoutesWindow!;window.Left=-32000;window.Top=-32000;
        void Wait(){var frame=new DispatcherFrame();var delay=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(650)};delay.Tick+=(_,_)=>{delay.Stop();frame.Continue=false;};delay.Start();Dispatcher.PushFrame(frame);}
        void Position(double x){engine.Position.Apply(new("local-zone",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),"synthetic",JsonSerializer.SerializeToElement(new{MapId=1010,X=x,Y=456,Z=0})));Wait();}
        Button Find(string content)=>LayoutVisuals<Button>(window).Single(b=>b.Content?.ToString()==content);
        var start=Find("Start route");
        if(start.IsEnabled)throw new Exception("Route allowed an unknown origin.");
        Position(123);
        var resource=LayoutVisuals<ComboBox>(window).Single(c=>c.Tag?.ToString()=="Gathering route resource");
        if(resource.Items.Count==0||!start.IsEnabled)throw new Exception("Fresh position did not populate materials and enable Start.");
        var selected=(GatherRouteChoice)resource.SelectedItem;
        var second=LayoutVisuals<ComboBox>(window).Single(c=>c.Tag?.ToString()=="Gathering route second resource");
        second.SelectedItem=selected;if(start.IsEnabled)throw new Exception("Duplicate material accepted for both routes.");
        second.SelectedItem=second.Items.OfType<GatherRouteChoice>().First(c=>c.ItemId!=selected.ItemId);
        var secondSelected=(GatherRouteChoice)second.SelectedItem;
        start.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Wait();
        var overlay=Application.Current.Windows.OfType<GatheringMapOverlay>().Single();
        if(overlay.StopCount==0)throw new Exception("Start did not open a populated overlay.");
        Position(90000);
        if(!overlay.ResourceLabel.Contains(selected.Name)||!overlay.ResourceLabel.Contains(secondSelected.Name)||overlay.SecondStopCount==0||overlay.StopCount<=overlay.SecondStopCount)throw new Exception("Teleport lost route or material.");
        if(!LayoutVisuals<TextBlock>(window).Single(t=>t.Tag?.ToString()=="Gathering route feedback").Text.Contains("refreshed"))throw new Exception("Teleport did not refresh route.");
        lootTracking.Record(selected.ItemId,1000000);Wait();
        if(!Application.Current.Windows.OfType<GatheringMapOverlay>().Any()||!Find("Stop route").IsEnabled||overlay.StopCount==0)throw new Exception("Gathering items stopped the continuous route.");
        window.UpdateLayout();Render(window,"gathering-setup.png");
        Find("Stop route").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        if(Application.Current.Windows.OfType<GatheringMapOverlay>().Any()||Find("Stop route").IsEnabled)throw new Exception("Explicit stop did not finish route.");
        second.SelectedItem=null;
        start.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Wait();
        var single=Application.Current.Windows.OfType<GatheringMapOverlay>().Single();
        if(single.SecondStopCount!=0||single.StopCount==0)throw new Exception("Removing second resource did not restore a single route.");
        Find("Stop route").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        window.Close();
    }
}
