namespace AionDpsMeter.Local;
internal sealed record FarmingNearbySnapshot(int MapId,Dictionary<int,long> Npcs,Dictionary<int,long> Objects);
internal static class FarmingNearby
{
    internal static decimal? Count(MapTarget target,MapTargetIndex markers,PetFarmLinks pets,FarmingNearbySnapshot state) {
        if(state.MapId<=0 && target.Kind is "gatherable" or "item") return null;
        if(target.Kind=="item") {
            if(ItemFarmLinks.Default.Find(target.Id) is not {} item)return null;
            var sourceObjects=state.MapId>0?item.Gatherables.SelectMany(id=>markers.ObjectIds(new("gatherable",id),state.MapId)).Distinct():[];
            return item.Npcs.Sum(id=>(decimal)state.Npcs.GetValueOrDefault(id))+sourceObjects.Sum(id=>(decimal)state.Objects.GetValueOrDefault(id));
        }
        var npcs=target.Kind=="pet"?pets.Find(target.Id)?.Npcs:target.Kind=="npc"?[target.Id]:Array.Empty<int>();
        if(npcs==null)return null;
        var objects=target.Kind=="gatherable"&&state.MapId>0?markers.ObjectIds(target,state.MapId):[];
        return npcs.Distinct().Sum(id=>(decimal)state.Npcs.GetValueOrDefault(id))+objects.Sum(id=>(decimal)state.Objects.GetValueOrDefault(id));
    }
}
public sealed partial class CombatEngine
{
    internal FarmingNearbySnapshot CurrentFarmingNearby() {
        lock(Gate) return new(lootMapId switch {901=>1110,902=>1010,_=>lootMapId},FarmingKills.Around(),WorldObjects.Around());
    }
}
