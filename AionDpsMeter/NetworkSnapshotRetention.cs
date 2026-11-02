using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal sealed class NetworkSnapshotRetention
{
    List<NetworkEvent>? owner;
    readonly Dictionary<int, int> mapIndices = new();
    readonly Dictionary<int, int> guardIndices = new();
    readonly Dictionary<(int Map, long Stamp, int State), int> zoneIndices = new();
    internal void Reset() { owner = null; mapIndices.Clear(); zoneIndices.Clear(); guardIndices.Clear(); }
    static int GuardActor(NetworkEvent item) => item.Kind == "guard" && item.Fields.ValueKind == JsonValueKind.Object &&
        item.Fields.TryGetProperty("ActorId", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int id) && id > 0 ? id : 0;
    static (int Map, long Stamp, int State)? Zone(NetworkEvent item) {
        if (item.Kind != "zone-state" || item.Fields.ValueKind != JsonValueKind.Object) return null;
        try {
            var state = item.Fields.Deserialize<ZoneStateReading>();
            return state is { MapId: > 0, State: >= 1 and <= 3 } ? (state.MapId, state.ServerTimestampMs ?? 0, state.State) : null;
        } catch (JsonException) { return null; }
    }
    static int Map(NetworkEvent item) => item.Kind == "field-boss-list" && item.Fields.ValueKind == JsonValueKind.Object &&
        item.Fields.TryGetProperty("MapId", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int id) && id is >= 1000 and <= 99999 ? id : 0;
    internal void Add(List<NetworkEvent> events, NetworkEvent item)
    {
        // Live cast and compact cooldown state consume these before retention.
        // Historical cast outcomes have their own accumulator.
        if (item.Kind is "skill-end" or "skill-use" or "skill-cooldown" or "skill-charges") return;
        // The inventory tracker retains only current stack counts. Raw inventory
        // snapshots and updates do not belong in combat history.
        if (item.Kind.StartsWith("party-finder", StringComparison.Ordinal)) return;
        // DungeonRun retains completion, timers and earned item totals. Result
        // rewards are previews, not additional loot or historical event data.
        if (item.Kind is "dungeon-state" or "dungeon-result") return;
        if (item.Kind is "appearance-current" or "appearance-presets" or "telegraph" or "boss-kill" or "scoreboard" or "artifact-siege" or "artifact-captured" or "abyss-season" or "abyss-matching" or "cutscene" or "npc-talk" or "controls" or "black-market" or "black-market-purchase" or "morph-counts" or "shop-purchases" or "account-login" or "duty-missions" or "player-level" or "player-guild" or "map-event-join" or "map-event-progress" or "map-event-leave" or "gathered" or "world-object" or "world-object-removed" or "party-boss-target" or "content-tickets" or "currency-ticket" or "inventory" or "inventory-update" or "actor-position" or "actor-stats" or "resources" or "player-stats" or "local-combat-power" or "dungeon-boss-killed" or "dungeon-deaths" or
            "pet-roster" or "pet-progress" or "pet-progress-decrease" or "pet-added" or "pet-level" or "visible-equipment" or "skill-levels" or "skill-choices" or "skill-chain" or "daevanion" or "wings" or "wing-change" or "titles" or "achievements" or "skins" or "decorations" or "monoliths") return;
        int map = Map(item);
        if (!ReferenceEquals(owner, events)) {
            owner = events; mapIndices.Clear(); zoneIndices.Clear(); guardIndices.Clear();
            for (int i = 0; i < events.Count; i++) {
                int existing = Map(events[i]); if (existing > 0) mapIndices[existing] = i;
                if (Zone(events[i]) is { } zone) zoneIndices[zone] = i;
                int guard = GuardActor(events[i]); if (guard > 0) guardIndices[guard] = i;
            }
        }
        if (GuardActor(item) is > 0 and var guardActor) {
            if (guardIndices.TryGetValue(guardActor, out int existing)) {
                if (item.Timestamp >= events[existing].Timestamp) events[existing] = item;
            } else if (guardIndices.Count >= 4096) {
                var oldest = guardIndices.MinBy(p => events[p.Value].Timestamp);
                if (item.Timestamp < events[oldest.Value].Timestamp) return;
                guardIndices.Remove(oldest.Key); events[oldest.Value] = item; guardIndices[guardActor] = oldest.Value;
            } else { guardIndices[guardActor] = events.Count; events.Add(item); }
            return;
        }
        if (Zone(item) is { } key) {
            // Preserve the first observation of each phase. A repeated phase-3
            // snapshot on re-entry must not move an observed transition later.
            if (zoneIndices.TryGetValue(key, out int existing)) {
                if (item.Timestamp < events[existing].Timestamp) events[existing] = item;
            } else if (zoneIndices.Count >= 64) {
                var oldest = zoneIndices.MinBy(p => events[p.Value].Timestamp);
                if (item.Timestamp < events[oldest.Value].Timestamp) return;
                zoneIndices.Remove(oldest.Key); events[oldest.Value] = item; zoneIndices[key] = oldest.Value;
            } else { zoneIndices[key] = events.Count; events.Add(item); }
            return;
        }
        if (map == 0) { events.Add(item); return; }
        if (mapIndices.TryGetValue(map, out int index)) {
            if (events[index].Timestamp <= item.Timestamp) events[index] = item;
            return;
        }
        if (mapIndices.Count >= 32) {
            var oldest = mapIndices.MinBy(p => events[p.Value].Timestamp);
            if (events[oldest.Value].Timestamp > item.Timestamp) return;
            mapIndices.Remove(oldest.Key); events[oldest.Value] = item; mapIndices.Add(map, oldest.Value);
        } else { mapIndices.Add(map, events.Count); events.Add(item); }
    }
    internal static void Merge(List<NetworkEvent> destination, IEnumerable<NetworkEvent> source)
    {
        var retention = new NetworkSnapshotRetention();
        foreach (var item in source) retention.Add(destination, item);
    }
}
