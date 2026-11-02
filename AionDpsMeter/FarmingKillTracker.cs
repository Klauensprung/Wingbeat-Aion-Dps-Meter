using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

// Bounded scene evidence and current-capture farming kill totals.
internal sealed class FarmingKillTracker
{
    sealed record Entry(int Npc, long At, bool Engaged = false, bool Dead = false);
    readonly Dictionary<int, Entry> entries = new();
    readonly Dictionary<int, long> kills = new();
    long boundary = -1;
    internal long OmittedKills { get; private set; }
    internal Dictionary<int, long> Snapshot() => new(kills);
    internal Dictionary<int,long> Around()=>entries.Values.Where(e=>e.Npc>0&&!e.Dead).GroupBy(e=>e.Npc).ToDictionary(g=>g.Key,g=>(long)g.Count());
    internal void ClearScene() => entries.Clear();
    internal int RetainedEntries => entries.Count;
    internal void Clear(long at) { if (at < boundary) return; boundary = at; entries.Clear(); }
    internal void Hit(int entity, long at, bool party)
    {
        if (!party || at < boundary || !entries.TryGetValue(entity, out var entry) || entry.Npc <= 0 || at < entry.At) return;
        entries[entity] = entry with { At = at, Engaged = true };
    }
    internal int? Apply(NetworkEvent e, bool player = false)
    {
        if (e.Timestamp < boundary || e.Timestamp < 0 || e.Fields.ValueKind != JsonValueKind.Object) return null;
        long? Number(string name) => e.Fields.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) ? n : null;
        if (e.Kind == "local-zone" && Number("MapId") is > 0) { Clear(e.Timestamp); return null; }
        int entity = (int)(Number("EntityId") ?? 0);
        if (entity <= 0) return null;
        entries.TryGetValue(entity, out var previous);
        if (previous != null && e.Timestamp < previous.At) return null;
        if (e.Kind == "npc-spawn") {
            if (player || Number("OwnerId") is > 0 || Number("NpcId") is not (> 0 and <= int.MaxValue)) return null;
            if (!entries.ContainsKey(entity) && entries.Count >= 4096) entries.Remove(entries.MinBy(p => p.Value.At).Key);
            // A reference spawn replaces health, while engagement survives until
            // death, removal or map change. Do not infer a kill from a spawn.
            entries[entity] = new((int)Number("NpcId")!.Value, e.Timestamp, previous?.Engaged ?? false);
            return null;
        }
        if (previous is not { Npc: > 0 }) return null;
        bool removal = e.Kind == "entity-removed";
        bool died = removal && e.Fields.TryGetProperty("Removal", out var reason) && reason.GetString() == "died";
        bool death = e.Kind == "entity-death" || e.Kind == "hp" && Number("CurrentHp") == 0;
        int? killed = previous.Engaged && (died || death && !previous.Dead) ? previous.Npc : null;
        if (removal) entries[entity] = new(0, e.Timestamp);
        else if (death) entries[entity] = previous with { At = e.Timestamp, Dead = true, Engaged = killed == null && previous.Engaged };
        else if (e.Kind == "hp" && Number("CurrentHp") is > 0) entries[entity] = previous with { At = e.Timestamp, Dead = false };
        if (killed is int npc) {
            if (!kills.ContainsKey(npc) && kills.Count >= 4096 || kills.GetValueOrDefault(npc) == long.MaxValue)
                OmittedKills = OmittedKills == long.MaxValue ? OmittedKills : OmittedKills + 1;
            else kills[npc] = kills.GetValueOrDefault(npc) + 1;
        }
        return killed;
    }
}
