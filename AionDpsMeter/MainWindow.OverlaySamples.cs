using System.Text.Json;
using System.Windows;
using AionDpsMeter.Models;
namespace AionDpsMeter.Local;
public sealed partial class MainWindow
{
    Window CreateOverlaySample(string name){
        var p=JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(preferences))!;
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        p.OverlayLocked=p.BossAlertsLocked=p.ControlLocked=p.NearbyEnemiesLocked=p.PetMapLocked=false;
        switch(name){
            case "DPS1 overlay":case "DPS2 overlay":{
                if(name=="DPS2 overlay"){var second=SecondMeterOptions.Create();second.Theme=p.Theme;second.Accent=p.Accent;p=second;}
                p.OverlayLocked=false;p.OverlayScope="Party";p.OverlayFollowGameTarget=false;p.OverlayBossesOnly=false;
                var fight=new Fight{LocalPlayer=1,Label="Krao Cave",PartyKnown=true};
                string[] names=["Aeris","Kael","Lyra","Riven","Sora"];string[] jobs=["Cleric","Templar","Chanter","Gladiator","Sorcerer"];
                for(int i=0;i<5;i++){
                    fight.Actors[i+1]=new(i+1,names[i],true,true,18000,jobs[i],1);
                    fight.Network.Add(new("hp",now,"sample",JsonSerializer.SerializeToElement(new{EntityId=i+1,CurrentHp=new[]{1000,650,220,850,480}[i],MaximumHp=1000})));
                    fight.Damage.Add(new(){ActorId=i+1,TargetId=100,SkillCode=17100000,Timestamp=now-30000,Damage=20000+i*19000});
                    fight.Damage.Add(new(){ActorId=i+1,TargetId=100,SkillCode=17100000,Timestamp=now,Damage=30000+i*17000});
                }
                fight.Actors[100]=new(100,"Krao",false,false,0,"Unknown",0);
                var meter=new OverlayWindow(skillName:id=>skills.GetSkillName(id),skillIcons:skillIcons){MeterLabel=name=="DPS2 overlay"?"DPS2":"DPS1"};
                meter.PartyRecovery=()=>new(){[2]=120,[3]=180};meter.PartyDebuffs=()=>[new(3,12000101,"Poison",12)];meter.Update(fight,p);return meter;
            }
            case "Cooldowns and buff uptime":{
                var tracker=new CooldownTracker();p.CooldownSkills=[17100000,17110000,17120000];p.CooldownBuffs=[];p.CooldownCombatOnly=false;
                foreach(int id in p.CooldownSkills)tracker.Capture(new("skill-cooldown",Environment.TickCount64,"sample",JsonSerializer.SerializeToElement(new{SkillCode=id,RemainingMs=18000+(id%3)*5000})),1);
                return new CooldownOverlayWindow(p,tracker,skills,skillIcons,()=>true,()=>{},()=>{});
            }
            case "Boss alerts":{var w=new BossAlertOverlayWindow(p,()=>{});w.Update([new(1,"Krao",1,"Casting",now,now+8000,10000,true)],now,_=>"Powerful strike");return w;}
            case "Party attack warnings":{var w=new PartyAttackWarningWindow(p,()=>{});w.Update(["Lyra"]);return w;}
            case "Control alerts":{var w=new ControlOverlayWindow(p,()=>{});w.Update([new("Stun",2.4)]);return w;}
            case "Nearby enemies":{var w=new NearbyEnemiesOverlayWindow(p,()=>{});w.Update([new(1,"Shade",21000,1200){Job="Assassin",LocalCp=18000,HealthPercent=68,Targeted=true},new(2,"Ember",17000,2400){Job="Sorcerer",LocalCp=18000,HealthPercent=91}],"");return w;}
            case "Loot overlay":{var w=new LootOverlayWindow(p,()=>{},()=>{});w.Update(new([new(1,now,"Aeris",930100003,1200,1,now,now,1010)],false),true);return w;}
            case "Farming overlay":{var w=new FarmingOverlayWindow(p,()=>{},()=>{});w.Update(new([new(new("gatherable",1),"Orichalcum",new("gatherable",1,12,30,false,[]),null),new(new("gatherable",2),"Odyle",new("gatherable",2,8,30,false,[]),null)],new(now,now,20,1200,2,false),[],false),true,1);return w;}
            case "Pet farming":{var w=new PetTrackerOverlayWindow(p,()=>{},_=>{},()=>{});w.UpdateCollected([new(1002,"Unstable Spider","12 / 25",3){Held=12,Required=25},new(1005,"Young Kailin","2 / 5",1){Held=2,Required=5}]);return w;}
            case "Pet map":{var w=new PetMapOverlayWindow(p,()=>{});w.Update([new(1002,"Unstable Spider","Spider",13,"12 / 25","altgard",1000,new(1,1000,1500,0)),new(1005,"Young Kailin","Kailin",3,"2 / 5","altgard",3000,new(2,-2000,2500,0))],new(1010,0,0,0,now),now,true,1);w.SetupSample();return w;}
            case "Ping overlay":{var w=new PingOverlayWindow(p,()=>{});w.Update(new(42,now,"sample"),now);return w;}
            case "Flight power":{var w=new FlightPowerOverlayWindow(p,()=>{});w.Update(new(now,Fp:650,MaximumFp:1000),true);return w;}
            case "Hidden cubes":{var w=new HiddenCubeOverlayWindow(p,()=>{});w.Update(new(1,45,"NE"));return w;}
            case "Gathering map":{var w=new GatheringMapOverlay(null);w.UpdateMap(1010,[new(1,0,0,0),new(2,1000,2000,0),new(3,3000,1000,0)]);w.SetupSample();return w;}
            default:throw new InvalidOperationException("Missing overlay preview: "+name);
        }
    }
}
