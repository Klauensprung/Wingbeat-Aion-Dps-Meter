using AionDpsMeter.Models;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class FightStorageEvents
{
    // Only evidence consumed by historical views survives. Packet diagnostics,
    // cooldown traffic, raw loot correlation and redundant state feeds do not.
    internal static List<NetworkEvent> Select(Fight fight)
    {
        var result = new List<NetworkEvent>();
        var snapshots = new NetworkSnapshotRetention();
        foreach (var e in fight.Network) {
            string[]? fields = e.Kind switch {
                "hp" => ["EntityId", "CurrentHp", "MaximumHp"],
                "boss-cast" => ["CasterId", "SkillCode"],
                "boss-enrage" => ["EntityId", "AbnormalId"],
                "combat-state" => ["EntityId", "State", "InCombat", "BerserkAtMs"],
                "abnormal-immunity" => ["EntityId", "EffectIds"],
                "displacement" => ["ActorId", "CasterId", "EffectId", "MoveType", "DurationMs"],
                "actor-phase" => ["EntityId", "Phase"],
                "npc-aggro" => ["EntityId"],
                "target" => ["ActorId", "TargetId"],
                "entity-revived" => ["EntityId"],
                "entity-death" => ["EntityId", "KillerId", "SkillCode", "NpcId", "ActionId", "Reason", "DieType"],
                "field-boss-list" => ["MapId", "Slots"],
                _ => null
            };
            if (fields == null || e.Fields.ValueKind != JsonValueKind.Object) continue;
            var kept = new Dictionary<string, JsonElement>();
            foreach (string name in fields) if (e.Fields.TryGetProperty(name, out var value)) kept[name] = value;
            snapshots.Add(result, new(e.Kind, e.Timestamp, "saved-analysis", JsonSerializer.SerializeToElement(kept)));
        }
        // Persist only the displayed last-seen state. Removed/reused identities
        // are filtered before scene/removal diagnostics are discarded.
        foreach (var pair in CapturedGuard.Build(fight)) {
            var guard = pair.Value;
            snapshots.Add(result, new("guard", guard.Timestamp, "saved-analysis", JsonSerializer.SerializeToElement(new {
                ActorId = pair.Key, State = guard.Broken ? 3 : 1, MaxGuard = guard.Maximum, CurrentGuard = guard.Current
            })));
        }
        return result;
    }
}
