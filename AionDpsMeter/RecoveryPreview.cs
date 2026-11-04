using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

// Live, bounded state only: never accumulate recovery previews in fight logs.
internal sealed class RecoveryPreview
{
    sealed record Entry(long Time,long Amount);
    readonly Dictionary<(int Target,int Slot,int Effect),Entry> entries=new();
    readonly Dictionary<(int Target,int Slot),(long Time,long End)> buffs=new();
    internal void Clear(){entries.Clear();buffs.Clear();}
    static long? Number(JsonElement f,string key)=>f.ValueKind==JsonValueKind.Object&&f.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetInt64(out long n)?n:null;
    internal void Apply(NetworkEvent e)
    {
        int target=(int)(Number(e.Fields,"TargetId")??0),slot=(int)(Number(e.Fields,"Slot")??-1);
        if(e.Kind is "entity-removed" or "entity-death"){
            target=(int)(Number(e.Fields,"EntityId")??0);
            foreach(var k in entries.Keys.Where(k=>k.Target==target).ToArray())entries.Remove(k);
            foreach(var k in buffs.Keys.Where(k=>k.Target==target).ToArray())buffs.Remove(k);
            return;
        }
        if(target<=0||slot<0)return;
        if(e.Kind=="recovery-preview"){
            Set(target,slot,(int)(Number(e.Fields,"EffectId")??0),Number(e.Fields,"Remaining")??0,e.Timestamp);return;
        }
        if(e.Kind is not ("abnormal-apply" or "abnormal-refresh" or "abnormal-remove"))return;
        var key=(target,slot);
        if(buffs.TryGetValue(key,out var old)&&old.Time>e.Timestamp)return;
        long end=e.Kind=="abnormal-remove"?e.Timestamp:
            EffectDuration.TryRead(e.Fields,out var duration,out _)&&duration is long ms?EffectDuration.Add(e.Timestamp,ms):long.MaxValue;
        if(buffs.Count>=2048&&!buffs.ContainsKey(key))buffs.Remove(buffs.MinBy(p=>p.Value.Time).Key);
        buffs[key]=(e.Timestamp,end);
        if(e.Kind=="abnormal-remove")foreach(var k in entries.Keys.Where(k=>k.Target==target&&k.Slot==slot&&entries[k].Time<=e.Timestamp).ToArray())entries.Remove(k);
    }
    internal void Set(int target,int slot,int effect,long remaining,long time)
    {
        if(target<=0||slot<0||effect<=0||remaining<0)return;
        var key=(target,slot,effect);
        if(entries.TryGetValue(key,out var old)&&old.Time>time)return;
        if(buffs.TryGetValue((target,slot),out var buff)&&(buff.End<=time||buff.End==buff.Time&&buff.Time>=time))return;
        if(entries.Count>=2048&&!entries.ContainsKey(key))entries.Remove(entries.MinBy(p=>p.Value.Time).Key);
        entries[key]=new(time,remaining);
    }
    internal Dictionary<int,double> Snapshot(long now)
    {
        foreach(var k in entries.Where(p=>now-p.Value.Time>5000||buffs.TryGetValue((p.Key.Target,p.Key.Slot),out var b)&&b.End<=now).Select(p=>p.Key).ToArray())entries.Remove(k);
        return entries.Where(p=>p.Value.Time<=now&&p.Value.Amount>0).GroupBy(p=>p.Key.Target).ToDictionary(g=>g.Key,g=>g.Sum(p=>(double)p.Value.Amount));
    }
}
