namespace AionDpsMeter.Local;
public sealed partial class MainWindow
{
    FarmingOverlayWindow? farmingOverlay;FarmingTargetView? farmingOverlayView;long farmingOverlayTick;
    void ToggleFarmingOverlay() {
        if(farmingOverlay!=null){farmingOverlay.Close();return;}
        trackedTargets??=new(demoMode?null:AppDataPaths.FilePath("farming-targets.json"));
        var window=new FarmingOverlayWindow(preferences,SavePreferences,ShowTrackedTargets);farmingOverlay=window;farmingOverlayView=new();farmingOverlayTick=long.MinValue;
        window.Closed+=(_,_)=>{if(ReferenceEquals(farmingOverlay,window)){farmingOverlay=null;farmingOverlayView=null;}};
        RefreshCooldownVisibility();
    }
    void RefreshFarmingOverlay(bool allowed) {
        if(farmingOverlay==null||farmingOverlayView==null)return;
        long tick=Environment.TickCount64;
        if(farmingOverlayTick==long.MinValue||tick-farmingOverlayTick>=1000){farmingOverlayTick=tick;
            var view=farmingOverlayView.Update(engine,trackedTargets!.Snapshot(),preferences.FarmingTargetGoal,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            farmingOverlay.Update(view,capture!=null,preferences.FarmingTargetGoal);
        }
        if(allowed&&!farmingOverlay.IsVisible)farmingOverlay.Show();else if(!allowed&&farmingOverlay.IsVisible)farmingOverlay.Hide();
    }
}
