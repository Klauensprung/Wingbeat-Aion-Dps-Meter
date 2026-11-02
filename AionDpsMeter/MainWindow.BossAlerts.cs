namespace AionDpsMeter.Local;
public sealed partial class MainWindow
{
    BossAlertOverlayWindow? bossAlertOverlay;
    void ConfigureBossAlerts(bool enabled,bool preview){
        preferences.BossAlertsEnabled=enabled;
        if(!enabled){bossAlertOverlay?.Close();SavePreferences();return;}
        EnsureBossAlerts();if(preview)bossAlertOverlay!.Preview();SavePreferences();RefreshCooldownVisibility();
    }
    void EnsureBossAlerts(){
        if(bossAlertOverlay!=null)return;
        var window=new BossAlertOverlayWindow(preferences,SavePreferences);bossAlertOverlay=window;
        window.Closed+=(_,_)=>{if(ReferenceEquals(bossAlertOverlay,window))bossAlertOverlay=null;if(!shuttingDown){preferences.BossAlertsEnabled=false;SavePreferences();}};
    }
    void RefreshBossAlerts(bool allowed){
        if(placementSetup!=null){
            preferences.BossAlertsEnabled=true;EnsureBossAlerts();bossAlertOverlay!.Preview();
        }
        if(!preferences.BossAlertsEnabled)return;
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();BossAlert[] alerts=[];
        if(capture!=null)lock(engine.Gate)alerts=engine.BossAlerts.Snapshot(now);
        if(alerts.Length>0)EnsureBossAlerts();
        if(bossAlertOverlay==null)return;
        bool show=allowed&&(alerts.Length>0||bossAlertOverlay.PreviewActive);
        if(show){bossAlertOverlay.Update(alerts,now,id=>skills.GetSkillName(id));if(!bossAlertOverlay.IsVisible)bossAlertOverlay.Show();}
        else if(bossAlertOverlay.IsVisible)bossAlertOverlay.Hide();
    }
}
