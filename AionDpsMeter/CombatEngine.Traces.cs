namespace AionDpsMeter.Local;

internal sealed record LiveTraceContext(AchievementSnapshot? Achievements,string? Race,int MapId,bool IdentityReady);
public sealed partial class CombatEngine
{
    internal LiveTraceContext CurrentTraceContext() {
        lock(Gate) {
            if(!LiveIdentityReady||!Fight.Actors.TryGetValue(Fight.LocalPlayer,out var actor))return new(null,null,0,false);
            var achievements=actor.Collections?.Achievements;
            if(achievements?.Timestamp<skillBoundaryTime)achievements=null;
            return new(achievements,TraceRace(actor,AppearancePresets.Snapshot(),skillBoundaryTime),lootMapId switch {901=>1110,902=>1010,_=>lootMapId},true);
        }
    }
    internal static string? TraceRace(Actor actor,CharacterPresets? appearance,long boundary) {
        if(appearance?.Current is not {} current || appearance.CurrentAt<boundary || appearance.Name!=actor.Name || appearance.Server!=actor.Server ||
            current.EntityId!=actor.Id || actor.PersistentId>0 && appearance.CharacterId!=actor.PersistentId.ToString(System.Globalization.CultureInfo.InvariantCulture))return null;
        return current.Race is "light" or "dark"?current.Race:null;
    }
}
