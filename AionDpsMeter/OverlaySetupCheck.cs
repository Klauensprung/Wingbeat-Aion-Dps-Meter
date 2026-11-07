using System.IO;
using System.Windows;
using System.Windows.Controls;
namespace AionDpsMeter.Local;
internal static class OverlaySetupCheck
{
    internal static int Run(Application app){
        app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        MainWindow? window=null;
        try{window=new MainWindow(true);window.CheckOverlaySetup();File.WriteAllText("overlay-setup-results.txt","PASS cancel, sequential decisions, saved placement, two-meter toggle and preview cleanup.");return 0;}
        catch(Exception ex){File.WriteAllText("overlay-setup-error.txt",ex.ToString());return 1;}
        finally{window?.Close();}
    }
}
public sealed partial class MainWindow
{
    internal void CheckOverlaySetup(){
        Show();timer.Stop();
        bool original=preferences.OverlayPlacementSetupCompleted;double? left=preferences.OverlayLeft;
        ShowOverlayPlacementSetup();var cancelled=placementSetup!;cancelled.PreviewWindow.Left=200;cancelled.Close();
        if(placementSetup!=null||preferences.OverlayLeft!=left||preferences.OverlayPlacementSetupCompleted!=original)throw new Exception("Cancelling setup changed configuration.");
        ShowOverlayPlacementSetup();var wizard=placementSetup!;
        wizard.UpdateLayout();Render(wizard,"overlay-setup-controller.png");Render(wizard.PreviewWindow,"overlay-setup-preview.png");
        int count=0;
        while(placementSetup!=null){
            bool enable=count<2;
            if(wizard.PreviewWindow.GetType()==typeof(Window))throw new Exception("Generic preview instead of actual overlay.");
            var handle=LayoutVisuals<System.Windows.Controls.Primitives.Thumb>(wizard.PreviewWindow).Single(t=>t.Tag?.ToString()=="SetupResizeHandle");
            if(!handle.IsVisible)throw new Exception("Resize handle is not visible.");
            if(count==0){double before=wizard.PreviewWindow.Width;handle.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(30,20));wizard.PreviewWindow.UpdateLayout();if(wizard.PreviewWindow.Width<=before)throw new Exception("Resize handle did not resize the meter.");}
            if(count==9){
                double before=wizard.PreviewWindow.ActualWidth;handle.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(30,30));wizard.PreviewWindow.UpdateLayout();
                if(wizard.PreviewWindow.ActualWidth<=before)throw new Exception("Fixed overlay resize handle did not scale the preview.");
                if(!LayoutVisuals<TextBlock>(wizard.PreviewWindow).Any(t=>t.Text=="12 / 25"))throw new Exception("Pet sample totals are missing.");
            }
            Render(wizard.PreviewWindow,$"overlay-setup-step-{count+1}.png");
            wizard.PreviewWindow.Left=80+count*10;wizard.PreviewWindow.Top=100;
            wizard.UpdateLayout();wizard.PreviewWindow.UpdateLayout();
            var button=LayoutVisuals<System.Windows.Controls.Button>(wizard).Single(b=>b.Content?.ToString()==(enable?"\u2713 "+Localization.T("Enable"):"\u2715 "+Localization.T("Skip")));
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if(++count>20)throw new Exception("Setup did not complete.");
        }
        if(count!=15||!preferences.OverlayPlacementSetupCompleted||!meterWanted||!SecondMeterOptions.Enabled||preferences.OverlayLeft!=80||SecondMeterOptions.Left!=90||preferences.BossAlertsEnabled||preferences.PetGatheringAutoShow)throw new Exception("Setup did not save enable/skip decisions and placement.");
        if(Application.Current.Windows.OfType<Window>().Any(w=>w.Title==Localization.T("Overlay setup")))throw new Exception("Setup left a window behind.");
        ToggleDpsMeters();if(meterWanted||SecondMeterOptions.Enabled)throw new Exception("DPS toggle did not disable both.");
        ToggleDpsMeters();if(!meterWanted||!SecondMeterOptions.Enabled)throw new Exception("DPS toggle did not restore both.");
        ShowOverlayPlacementSetup();placementSetup!.Close();
        if(!meterWanted||!SecondMeterOptions.Enabled)throw new Exception("Cancel lost active meters.");
    }
}
