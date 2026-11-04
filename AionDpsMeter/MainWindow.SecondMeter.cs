using System.Windows;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    OverlayWindow? secondMeter;
    Preferences? secondMeterPreferences;
    SecondMeterSettings SecondMeterOptions => preferences.SecondMeter ??= new();
    void SyncSecondMeterAppearance() {
        if(secondMeterPreferences==null)return;
        secondMeterPreferences.Theme=preferences.Theme;secondMeterPreferences.Accent=preferences.Accent;
        secondMeterPreferences.BackAttackColor=preferences.BackAttackColor;
        secondMeterPreferences.OverlaysGameOnly=preferences.OverlaysGameOnly;
    }
    void ToggleSecondMeter() {
        if(SecondMeterOptions.Enabled){SecondMeterOptions.Enabled=false;secondMeter?.Close();SavePreferences();}
        else OpenSecondMeter();
        UpdateToolbarState();
    }
    void OpenSecondMeter() {
        SecondMeterOptions.Enabled=true;
        if(secondMeter!=null){RefreshCooldownVisibility();return;}
        var settings=SecondMeterOptions.Create();secondMeterPreferences=settings;
        settings.OverlayLeft??=SystemParameters.WorkArea.Right-410;
        settings.OverlayTop??=SystemParameters.WorkArea.Top+100;
        SyncSecondMeterAppearance();
        void Save() {
            SecondMeterOptions.Capture(settings);
            SavePreferences();
        }
        var window=new OverlayWindow(NewFight,id=>FocusActor(id,settings.OverlayHealing),Save,
            id=>skills.GetSkillName(id),skillIcons,ShowTraining){Title="Wingbeat DPS2 overlay",MeterLabel="DPS2",ShowActivated=false};
        secondMeter=window;
        window.SharedVisibilityChanged=value=>preferences.OverlaysGameOnly=value;
        window.PartyRanges=PartyRanges;
        window.PartyRecovery=()=>{lock(engine.Gate)return capture!=null?engine.Recovery.Snapshot(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()):new();};
        window.PartyDebuffs=()=>capture!=null?partyDebuffs.Active(Environment.TickCount64):[];
        window.PartyDistances=PartyDistances;
        window.Closed+=(_,_)=>{
            if(ReferenceEquals(secondMeter,window)){secondMeter=null;secondMeterPreferences=null;}
            if(!shuttingDown){SecondMeterOptions.Enabled=false;SavePreferences();UpdateToolbarState();}
        };
        try {
            window.Update(engine.TrainingSnapshot(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())??(capture!=null?LiveSnapshot():Current),settings);
            RefreshCooldownVisibility();SavePreferences();
        }catch{window.Close();throw;}
    }
    void RefreshSecondMeter(Fight snapshot) {
        if(secondMeter?.IsVisible!=true||secondMeterPreferences==null)return;
        SyncSecondMeterAppearance();
        secondMeter.Update(snapshot,secondMeterPreferences,rows:ReferenceEquals(snapshot,displayedSnapshot)?displayedTotals:null,
            pvpData:ReferenceEquals(snapshot,displayedSnapshot)?displayedPvp:null);
    }
}
