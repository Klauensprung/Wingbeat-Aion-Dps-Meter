using AionDpsMeter.Models;
namespace AionDpsMeter.Local;
internal static class PartyAttackWarningTests
{
    internal static void Run(Action<bool,string> check){
        var fight=new Fight{LocalPlayer=1};
        fight.Actors[1]=new(1,"You",true,true,0,"Cleric",1);
        fight.Actors[2]=new(2,"Friend",true,true,0,"Chanter",1);
        fight.Actors[3]=new(3,"Attacker",true,false,0,"Gladiator",2);
        fight.Actors[4]=new(4,"Monster",false,false,0,"Unknown",0);
        var warnings=new PartyAttackWarnings();
        var hit=new DamageEvent{ActorId=3,TargetId=2,Timestamp=1000,Damage=10};
        warnings.Observe(fight,hit,1);
        check(warnings.Active(fight,1000,1).SequenceEqual(["Friend"]),"First captured PvP hit warns for its party victim");
        warnings.Observe(fight,hit with{TargetId=1},1);
        check(warnings.Active(fight,1000,1).SequenceEqual(["Friend"]),"Incoming attacks on the local player never create party warnings");
        fight.LocalPlayer=2;
        check(warnings.Active(fight,1000,1).Length==0,"Pending warnings exclude a target now identified as the local player");
        fight.LocalPlayer=1;
        warnings.Observe(fight,hit with{Timestamp=4500},1);
        check(warnings.Active(fight,6000,1).Length==0,"Repeated strikes do not extend or repeat the first-hit warning");
        warnings.Observe(fight,hit with{Timestamp=19500},1);
        check(warnings.Active(fight,19500,1).Length==1,"Party attack warning rearms after 15 seconds without PvP damage");
        warnings.Observe(fight,hit with{Timestamp=1000},1);
        check(warnings.Active(fight,24500,1).Length==0,"Older hits cannot restart expired warnings");
        check(warnings.Active(fight,19500,2).Length==0,"Teleport scene change clears attack warnings");
        foreach(var ignored in new[]{hit with{ActorId=4},hit with{ActorId=1},hit with{TargetId=3},hit with{IsHeal=true},hit with{Damage=0},hit with{ActorId=99}}){
            warnings.Observe(fight,ignored,2);
        }
        check(warnings.Active(fight,1000,2).Length==0,"Monster, friendly, self, healing, zero-damage and unidentified hits do not warn");
        warnings.Observe(fight,hit,2);fight.Actors[2]=fight.Actors[2] with{Party=false};
        check(warnings.Active(fight,1100,2).Length==0,"Leaving the party removes a pending warning");
    }
}
