using AionDpsMeter.Models;
namespace AionDpsMeter.Local;

internal sealed class PartyAttackWarnings
{
    sealed record Entry(string Name,int Server,long LastHit,long WarnedAt);
    readonly Dictionary<int,Entry> members=new();
    long scene=long.MinValue;
    void SetScene(long value){if(scene==value)return;scene=value;members.Clear();}
    internal void Observe(Fight fight,DamageEvent hit,long currentScene){
        SetScene(currentScene);
        if(hit.IsHeal||hit.Damage<=0||hit.ActorId==hit.TargetId||
            !fight.Actors.TryGetValue(hit.ActorId,out var attacker)||!attacker.Player||attacker.OriginalEntityId!=0||attacker.Party||attacker.Id==fight.LocalPlayer||
            !fight.Actors.TryGetValue(hit.TargetId,out var target)||!target.Player||target.OriginalEntityId!=0||!target.Party||target.Id==fight.LocalPlayer)return;
        bool same=members.TryGetValue(target.Id,out var previous)&&previous.Name==target.Name&&previous.Server==target.Server;
        if(same&&hit.Timestamp<=previous!.LastHit)return;
        long warned=!same||hit.Timestamp-previous!.LastHit>=15000?hit.Timestamp:previous!.WarnedAt;
        if(!members.ContainsKey(target.Id)&&members.Count>=64)members.Remove(members.MinBy(p=>p.Value.LastHit).Key);
        members[target.Id]=new(target.Name,target.Server,hit.Timestamp,warned);
    }
    internal string[] Active(Fight fight,long now,long currentScene){
        SetScene(currentScene);
        return members.Where(p=>now>=p.Value.WarnedAt&&now-p.Value.WarnedAt<5000&&
            fight.Actors.TryGetValue(p.Key,out var target)&&target.Name==p.Value.Name&&target.Server==p.Value.Server&&
            target.Player&&target.OriginalEntityId==0&&target.Party&&target.Id!=fight.LocalPlayer)
            .OrderByDescending(p=>p.Value.WarnedAt).Take(6).Select(p=>p.Value.Name).ToArray();
    }
}
