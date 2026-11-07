namespace AionDpsMeter.Local;
public sealed partial class MainWindow
{
    PartyAttackWarningWindow? partyAttackWarning;
    void RefreshPartyAttackWarning(bool allowed){
        string[] names=[];
        lock(engine.Gate)if(capture!=null&&engine.LiveIdentityReady&&preferences.PartyAttackWarningsEnabled)
            names=engine.PartyAttacks.Active(engine.Fight,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),engine.LiveSceneRevision);
        if(names.Length==0||!allowed){partyAttackWarning?.Hide();return;}
        if(partyAttackWarning==null){
            var window=new PartyAttackWarningWindow(preferences,SavePreferences);partyAttackWarning=window;
            window.Closed+=(_,_)=>{if(ReferenceEquals(partyAttackWarning,window))partyAttackWarning=null;};
        }
        partyAttackWarning.Update(names);
        if(OverlayHideControls.Allows(partyAttackWarning)&&!partyAttackWarning.IsVisible)partyAttackWarning.Show();
        else if(!OverlayHideControls.Allows(partyAttackWarning))partyAttackWarning.Hide();
    }
}
