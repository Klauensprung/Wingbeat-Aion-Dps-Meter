using System.Text.Json;
using AionDpsMeter.Models;
namespace AionDpsMeter.Local;

internal sealed record BossAlert(int Entity,string Boss,int Skill,string State,long Started,long Until,int Duration,bool TargetsYou,string? Detail=null);
internal sealed class BossAlertTracker
{
    readonly Dictionary<(int Entity,string Lane),BossAlert> alerts=new();
    readonly Dictionary<int,long> latest=new();
    readonly BossHealthAlerts health=new();
    int map;
    long zoneTime=-1;
    long boundary=-1;
    void Remember(int entity,long time){latest[entity]=time;if(latest.Count>32)latest.Remove(latest.MinBy(p=>p.Value).Key);}
    internal void Clear(long at=-1){alerts.Clear();latest.Clear();health.Clear();boundary=at;}
    internal void ResetEncounter(){alerts.Clear();health.ResetEncounter();}
    internal void Remove(int entity){foreach(var key in alerts.Keys.Where(k=>k.Entity==entity).ToArray())alerts.Remove(key);health.Remove(entity);}
    internal void Apply(NetworkEvent message,Fight fight){
        if(message.Timestamp<0||message.Timestamp<boundary||message.Fields.ValueKind!=JsonValueKind.Object)return;
        long now=message.Timestamp;
        int Number(string name)=>message.Fields.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetInt32(out int n)?n:0;
        if(message.Kind=="account-login"){if(now<zoneTime)return;zoneTime=now;map=0;Clear(now);return;}
        if(message.Kind=="local-zone"){
            if(now<zoneTime)return;zoneTime=now;
            int nextMap=Number("MapId");
            bool preparing=message.Fields.TryGetProperty("AwaitingIdentity",out var value)&&value.ValueKind==JsonValueKind.True;
            if(preparing||nextMap!=map){Clear(now);map=nextMap;}
            return;
        }
        if(message.Kind is "entity-death" or "entity-removed"){
            int id=Number("EntityId");if(latest.TryGetValue(id,out long last)&&now<last)return;
            Remove(id);Remember(id,now);return;
        }
        int entity=message.Kind=="boss-cast"?Number("CasterId"):message.Kind is "cast-start" or "cast-stop" or "charge-start" or "charge-stop" or "skill-use" or "skill-end"?Number("ActorId"):Number("EntityId");
        if(latest.TryGetValue(entity,out long lastEvent)&&now<lastEvent)return;
        if(!fight.Actors.TryGetValue(entity,out var actor)||!CapturedBoss.IsBoss(actor))return;
        if(message.Kind=="hp"){
            var warning=health.Apply(entity,actor.Name,now,message.Fields,out bool reset);
            if(reset)foreach(var old in alerts.Keys.Where(k=>k.Entity==entity).ToArray())alerts.Remove(old);
            if(message.Fields.TryGetProperty("CurrentHp",out var hp)&&hp.ValueKind==JsonValueKind.Number&&hp.TryGetInt64(out long value)&&value==0){
                foreach(var old in alerts.Keys.Where(k=>k.Entity==entity).ToArray())alerts.Remove(old);
            }
            if(warning!=null){
                foreach(var expired in alerts.Where(p=>p.Value.Until<=now).Select(p=>p.Key).ToArray())alerts.Remove(expired);
                if(!alerts.ContainsKey((entity,"prepare"))&&alerts.Count>=16)alerts.Remove(alerts.MinBy(p=>p.Value.Started).Key);
                alerts[(entity,"prepare")]=warning;
            }
            return;
        }
        int skill=Number("SkillCode");string lane="cast",state;int duration=0,visible=3000;
        switch(message.Kind){
            case "cast-start":case "boss-cast":state="Casting";duration=Math.Clamp(Number("CastingMs"),0,60000);break;
            case "charge-start":state="Charging";break;
            case "cast-stop":case "charge-stop":
                if(!alerts.TryGetValue((entity,lane),out var cast)||cast.Skill!=skill||now<cast.Started)return;
                state=Number("Result")==6152?"Interrupted":"Cast stopped";visible=1500;break;
            case "skill-use":case "skill-end":
                if(alerts.TryGetValue((entity,lane),out var done)&&done.Skill==skill&&now>=done.Started){alerts.Remove((entity,lane));Remember(entity,now);}return;
            case "telegraph":lane="area";state="Ground attack";skill=Number("Skill");duration=Math.Clamp(Number("DurationMs"),0,60000);break;
            case "actor-phase":lane="phase";state="Phase "+Number("Phase");break;
            case "boss-enrage":lane="enrage";state="Enrage";visible=5000;break;
            default:return;
        }
        var cue=message.Kind is "cast-start" or "boss-cast" ? BossMechanicCues.Cast(actor.NpcId,skill)
            : message.Kind=="actor-phase" ? BossMechanicCues.Phase(actor.NpcId,Number("Phase")) : null;
        if(cue is {} mechanic){state=Localization.T(mechanic.Title);visible=6000;}
        var key=(entity,lane);
        if(alerts.TryGetValue(key,out var previous)){
            if(now<previous.Started)return;
            if(previous.Skill==skill&&previous.State==state&&(now==previous.Started||lane=="area"&&now-previous.Started<1000))return;
        }
        foreach(var expired in alerts.Where(p=>p.Value.Until<=now).Select(p=>p.Key).ToArray())alerts.Remove(expired);
        if(!alerts.ContainsKey(key)&&alerts.Count>=16)alerts.Remove(alerts.MinBy(p=>p.Value.Started).Key);
        Remember(entity,now);
        alerts[key]=new(entity,actor.Name,skill,state,now,now+(duration>0?duration:visible),duration,fight.LocalPlayer>0&&Number("TargetId")==fight.LocalPlayer,
            cue is {} instruction ? Localization.T(instruction.Detail) : null);
    }
    internal BossAlert[] Snapshot(long now)=>alerts.Values.Where(a=>now>=a.Started&&now<a.Until).OrderByDescending(a=>a.TargetsYou).ThenByDescending(a=>a.Started).Take(3).ToArray();
}
