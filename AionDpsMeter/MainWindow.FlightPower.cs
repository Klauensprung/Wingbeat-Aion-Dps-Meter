namespace AionDpsMeter.Local;
public sealed partial class MainWindow
{
    FlightPowerOverlayWindow? flightPowerOverlay;
    void ToggleFlightPowerOverlay(){
        if(flightPowerOverlay!=null){flightPowerOverlay.Close();return;}
        var window=new FlightPowerOverlayWindow(preferences,SavePreferences);flightPowerOverlay=window;
        window.Closed+=(_,_)=>{if(ReferenceEquals(flightPowerOverlay,window))flightPowerOverlay=null;};RefreshCooldownVisibility();
    }
    void RefreshFlightPowerOverlay(bool allowed){
        if(flightPowerOverlay==null)return;
        ResourceSnapshot? sample;
        lock(engine.Gate)sample=engine.LiveIdentityReady?engine.Fight.LocalResources:null;
        flightPowerOverlay.Update(sample,capture!=null);
        if(allowed&&!flightPowerOverlay.IsVisible)flightPowerOverlay.Show();
        else if(!allowed&&flightPowerOverlay.IsVisible)flightPowerOverlay.Hide();
    }
}
