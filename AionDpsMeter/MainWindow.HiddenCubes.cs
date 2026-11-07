namespace AionDpsMeter.Local;
public sealed partial class MainWindow
{
    HiddenCubeOverlayWindow? hiddenCubeOverlay;
    void ToggleHiddenCubeOverlay(){
        if(hiddenCubeOverlay!=null){hiddenCubeOverlay.Close();return;}
        var window=new HiddenCubeOverlayWindow(preferences,SavePreferences);hiddenCubeOverlay=window;
        window.Closed+=(_,_)=>{if(ReferenceEquals(hiddenCubeOverlay,window))hiddenCubeOverlay=null;};RefreshCooldownVisibility();
    }
    void RefreshHiddenCubeOverlay(bool allowed){
        if(hiddenCubeOverlay==null)return;
        HiddenCubeDirection? target=null;bool detected=false;
        if(capture!=null)lock(engine.Gate){
            if(engine.LiveIdentityReady){
                var objects=engine.WorldObjects.Snapshot();detected=objects.Any(HiddenCubes.IsCube);
                target=HiddenCubes.Nearest(objects,engine.WorldObjects.MapId,engine.Position.Latest,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            }
        }
        hiddenCubeOverlay.Update(target);
        if(allowed&&detected&&!hiddenCubeOverlay.IsVisible)hiddenCubeOverlay.Show();
        else if((!allowed||!detected)&&hiddenCubeOverlay.IsVisible)hiddenCubeOverlay.Hide();
    }
}
