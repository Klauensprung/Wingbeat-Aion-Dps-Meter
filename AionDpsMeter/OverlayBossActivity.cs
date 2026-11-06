using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

// Encounter visibility is independent of short-lived warning banners and of
// the damage meter's automatic idle split. Keep only engaged boss IDs/times.
internal sealed class OverlayBossActivity
{
    readonly Dictionary<int,long> engaged = new();
    internal void Clear() => engaged.Clear();
    internal void Engage(int id,long time) {
        if(id<=0||time<0)return;
        engaged[id]=Math.Max(engaged.GetValueOrDefault(id,-1),time);
        if(engaged.Count>32)engaged.Remove(engaged.MinBy(p=>p.Value).Key);
    }
    internal bool Active(long now) => engaged.Values.Any(t=>now>=t&&now-t<=300000);
    internal void Apply(NetworkEvent e) {
        if(e.Kind is "local-zone" or "account-login"){Clear();return;}
        int id=Number("EntityId") is >0 and <=int.MaxValue and var entity?(int)entity:
            (int)(Number(e.Kind=="boss-cast"?"CasterId":"ActorId")??0);
        if(!engaged.TryGetValue(id,out long last)||e.Timestamp<last)return;
        if(e.Kind is "entity-death" or "entity-removed" || e.Kind=="hp"&&Number("CurrentHp")==0 ||
            e.Kind=="combat-state"&&Number("State")==0){engaged.Remove(id);return;}
        if(e.Kind is "hp" or "combat-state" or "boss-cast" or "cast-start" or "skill-use" or "actor-phase" or "boss-enrage")engaged[id]=e.Timestamp;
        long? Number(string key)=>e.Fields.ValueKind==System.Text.Json.JsonValueKind.Object&&
            e.Fields.TryGetProperty(key,out var value)&&value.ValueKind==System.Text.Json.JsonValueKind.Number&&value.TryGetInt64(out long n)?n:null;
    }
}
