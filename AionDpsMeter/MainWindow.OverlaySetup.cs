using System.Windows;
using System.Windows.Controls;
namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    OverlayPlacementSetupWindow? placementSetup;
    Button? dpsToggleButton;
    bool resumeDps1=true,resumeDps2;
    void ToggleDpsMeters(){
        if(meterWanted||SecondMeterOptions.Enabled){
            resumeDps1=meterWanted;resumeDps2=SecondMeterOptions.Enabled;
            meterWanted=false;overlay?.Hide();if(SecondMeterOptions.Enabled)ToggleSecondMeter();
            preferences.OpenMeterOnStartup=false;
        }else{
            if(resumeDps1||!resumeDps2){OpenOverlay();preferences.OpenMeterOnStartup=true;}
            if(resumeDps2)OpenSecondMeter();
        }
        SavePreferences();RefreshCooldownVisibility();
    }
    Window?[] SetupWindows()=>[radarOverlay,partyAttackWarning,overlay,secondMeter,cooldownOverlay,bossAlertOverlay,controlOverlay,nearbyEnemiesOverlay,lootOverlay,farmingOverlay,petTrackerOverlay,petMapOverlay,pingOverlay,flightPowerOverlay,hiddenCubeOverlay,currencyOverlay,ticketOverlay];
    void ShowOverlayPlacementSetup(){
        if(placementSetup!=null){placementSetup.Activate();return;}
        var steps=new List<OverlaySetupStep>();
        void Add(string name,string prefix,Func<Window?> live,Action<bool> enable,double width,double height,string hint="Only real events appear after setup.",object? options=null){
            object source=options??preferences;var type=source.GetType();
            double Read(string suffix,double fallback)=>type.GetProperty(prefix+suffix)?.GetValue(source) is double value&&double.IsFinite(value)&&value>0?value:fallback;
            double Position(string suffix,double fallback)=>type.GetProperty(prefix+suffix)?.GetValue(source) is double value&&double.IsFinite(value)?value:fallback;
            var current=live();var bounds=new Rect(current?.Left??Position("Left",SystemParameters.WorkArea.Left+60),current?.Top??Position("Top",SystemParameters.WorkArea.Top+100),
                current?.ActualWidth>0?current.ActualWidth:Read("Width",width),current?.ActualHeight>0?current.ActualHeight:Read("Height",height));
            bool resize=type.GetProperty(prefix+"Width")!=null;
            steps.Add(new(name,hint,bounds,resize,(enabled,rect)=>{
                void Set(string suffix,double value)=>type.GetProperty(prefix+suffix)?.SetValue(source,value);
                // Move existing windows too, so their delayed placement saves agree with setup.
                if(live() is {} window){window.Left=rect.X;window.Top=rect.Y;if(resize){window.Width=rect.Width;window.Height=rect.Height;}}
                Set("Left",rect.X);Set("Top",rect.Y);if(resize){Set("Width",rect.Width);Set("Height",rect.Height);}
                enable(enabled);
            },()=>CreateOverlaySample(name)));
        }
        void Optional(string name,string prefix,Func<Window?> live,Action open,double width,double height,string hint="Only real events appear after setup."){
            Add(name,prefix,live,enabled=>{
                preferences.SetupOverlayEnabled[prefix]=enabled;
                if(enabled&&live()==null)open();else if(!enabled)live()?.Close();
            },width,height,hint);
        }
        Add("DPS1 overlay","Overlay",()=>overlay,on=>{preferences.OpenMeterOnStartup=on;if(on)OpenOverlay();else{meterWanted=false;overlay?.Hide();}},390,530);
        Add("DPS2 overlay","",()=>secondMeter,on=>{if(on)OpenSecondMeter();else if(SecondMeterOptions.Enabled)ToggleSecondMeter();},390,420,options:SecondMeterOptions);
        Add("Cooldowns and buff uptime","Cooldown",()=>cooldownOverlay,on=>{preferences.OpenCooldownsOnStartup=on;if(on)OpenCooldownOverlay();else{cooldownWanted=false;cooldownOverlay?.Hide();}},300,120,"Choose tracked skills in Cooldowns after setup.");
        Add("Boss alerts","BossAlerts",()=>bossAlertOverlay,on=>ConfigureBossAlerts(on,false),400,130);
        Add("Party attack warnings","PartyAttack",()=>partyAttackWarning,on=>{preferences.PartyAttackWarningsEnabled=on;if(!on)partyAttackWarning?.Hide();},420,120);
        Add("Control alerts","Control",()=>controlOverlay,on=>ConfigureControlAlerts(on,false),330,130);
        Add("Nearby enemies","NearbyEnemies",()=>nearbyEnemiesOverlay,on=>{preferences.NearbyEnemiesEnabled=on;if(on)EnsureNearbyEnemies();else nearbyEnemiesOverlay?.Close();},330,400);
        Add("Loot overlay","Loot",()=>lootOverlay,on=>{preferences.LootOverlayEnabled=on;if(on)EnsureLootOverlay();else lootOverlay?.Close();},330,160);
        Optional("Farming overlay","FarmingOverlay",()=>farmingOverlay,OpenFarmingOverlay,360,160,"Choose materials from the Farming overlay after setup.");
        Optional("Pet farming","PetTracker",()=>petTrackerOverlay,()=>{preferences.PetGatheringAutoShow=true;OpenPetTracker();},350,160);
        // Skipping pet tracking must also disable its event-driven auto-open.
        var pet=steps[^1];steps[^1]=pet with{Apply=(on,rect)=>{preferences.PetGatheringAutoShow=on;pet.Apply(on,rect);}};
        Optional("Pet map","PetMap",()=>petMapOverlay,OpenPetMap,410,360);
        Optional("Ping overlay","Ping",()=>pingOverlay,TogglePingOverlay,180,80);
        Optional("Flight power","FlightPower",()=>flightPowerOverlay,ToggleFlightPowerOverlay,130,300);
        Optional("Hidden cubes","HiddenCube",()=>hiddenCubeOverlay,ToggleHiddenCubeOverlay,235,130);
        Optional("3D radar","Radar",()=>radarOverlay,ToggleRadarOverlay,420,400);
        Add("Gathering map","GatheringSetup",()=>null,on=>{
            stopGatheringRoute?.Invoke();preferences.GatheringSetupPending=on;
            if(on)ShowGatheringRoutes();
        },700,550,"After setup, choose materials and start a route. Match the map scale in Gathering routes.");
        var wizard=new OverlayPlacementSetupWindow(preferences,steps.ToArray(),()=>{
            preferences.OverlayPlacementSetupCompleted=true;SavePreferences();
        }){Owner=this};
        placementSetup=wizard;
        wizard.Closed+=(_,_)=>{placementSetup=null;if(!shuttingDown){Refresh();RefreshCooldownVisibility();}};
        wizard.Show();RefreshCooldownVisibility();
    }
    void RestoreSetupExtras(){
        preferences.SetupOverlayEnabled.Remove("Currency");preferences.SetupOverlayEnabled.Remove("Ticket");
        if(!preferences.AutoStartConfigured)return;
        foreach(var (key,live,open) in new (string,Func<Window?>,Action)[]{
            ("FarmingOverlay",()=>farmingOverlay,OpenFarmingOverlay),("PetTracker",()=>petTrackerOverlay,OpenPetTracker),
            ("PetMap",()=>petMapOverlay,OpenPetMap),("Ping",()=>pingOverlay,TogglePingOverlay),
            ("Radar",()=>radarOverlay,ToggleRadarOverlay),("FlightPower",()=>flightPowerOverlay,ToggleFlightPowerOverlay),("HiddenCube",()=>hiddenCubeOverlay,ToggleHiddenCubeOverlay)})
            if(preferences.SetupOverlayEnabled.GetValueOrDefault(key)&&live()==null)open();
    }
    void SaveSetupExtras(){
        foreach(var (key,window) in new (string,Window?)[]{("FarmingOverlay",farmingOverlay),("PetTracker",petTrackerOverlay),("PetMap",petMapOverlay),("Ping",pingOverlay),("Radar",radarOverlay),("FlightPower",flightPowerOverlay),("HiddenCube",hiddenCubeOverlay)})
            if(preferences.SetupOverlayEnabled.ContainsKey(key))preferences.SetupOverlayEnabled[key]=window!=null;
    }
}
