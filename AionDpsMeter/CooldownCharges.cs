using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal sealed record ChargeReading(int Count, double RemainingSeconds, double DurationSeconds);
internal sealed record ChargeDisplay(int Count, int Capacity, double RemainingSeconds, double DurationSeconds, bool Estimated);
internal sealed class CooldownCharges
{
    sealed record State(int Skill, int Count, long At, long Deadline, int DurationMs, long Revision);
    readonly Dictionary<int, State> states = new();
    long revision;
    internal int Count => states.Count;
    internal static int Family(int skill) => skill is >= 11000000 and <= 19999999 && skill % 1000000 >= 10000 ? skill / 10000 * 10000 : skill;
    internal void Clear() { states.Clear(); revision = 0; }
    internal CooldownCharges Copy() {
        var copy = new CooldownCharges { revision = revision };
        foreach (var pair in states) copy.states.Add(pair.Key, pair.Value);
        return copy;
    }
    internal void Apply(NetworkEvent e, int local, long utc, long mono)
    {
        if (e.Timestamp < 0 || e.Timestamp > utc || e.Fields.ValueKind != JsonValueKind.Object ||
            e.Kind is not ("skill-use" or "skill-cooldown" or "skill-charges")) return;
        int Number(string key, int missing = -1) => e.Fields.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : missing;
        int skill = Number("SkillCode"); if (skill <= 0) return;
        if (e.Kind != "skill-cooldown" || e.Fields.TryGetProperty("ActorId", out _))
            if (local <= 0 || Number("ActorId") != local) return;
        int family = Family(skill); states.TryGetValue(family, out var old);
        if (old != null && e.Timestamp < old.At) return;
        if (e.Kind == "skill-use") {
            // A positive cast cooldown replaces the previous charge metadata.
            // A separately emitted charge report follows it in packet order.
            if (Number("CooldownMs") > 0) states.Remove(family);
            return;
        }
        int ms = Number(e.Kind == "skill-charges" ? "RechargeMs" : "RemainingMs");
        if (ms is < 0 or > 86400000) return;
        int count = e.Kind == "skill-charges" ? Number("Charges") : old?.Count ?? -1;
        if (count is < 0 or > 255 || e.Kind == "skill-charges" && ms == 0) return;
        int duration = e.Kind == "skill-cooldown" ? old!.DurationMs : old?.Skill == skill ? Math.Max(old.DurationMs, ms) : ms;
        states[family] = new(e.Kind == "skill-charges" ? skill : old!.Skill, count, e.Timestamp,
            mono + (e.Timestamp + ms - utc), duration, ++revision);
        if (states.Count > 4096) states.Remove(states.MinBy(p => p.Value.At).Key);
    }
    internal void Import(CooldownCharges source, long utc, long mono, long boundary)
    {
        foreach (int key in states.Keys.Where(k => !source.states.ContainsKey(k)).ToArray()) states.Remove(key);
        foreach (var (key, value) in source.states) {
            if (value.At < boundary || value.At > utc) continue;
            if (states.TryGetValue(key, out var old) && old.At == value.At && old.Revision == value.Revision) continue;
            // Preserve overdue time: several charges may have recovered while
            // the observer was closed or the packet was waiting for delivery.
            states[key] = value with { Deadline = mono + (value.Deadline - utc) };
        }
    }
    internal ChargeReading? Reading(int skill, long now) => states.TryGetValue(Family(skill), out var state)
        ? new(state.Count, Math.Max(0, state.Deadline - now) / 1000d, state.DurationMs / 1000d) : null;
    internal ChargeDisplay? Display(int skill, long now, int configuredCapacity = 1)
    {
        if (!states.TryGetValue(Family(skill), out var state)) return null;
        int capacity = Math.Max(configuredCapacity, state.Count + 1), missing = capacity - state.Count;
        long recovered = now < state.Deadline ? 0 : Math.Min(missing, 1 + (now - state.Deadline) / state.DurationMs);
        long next = state.Deadline + Math.Min(recovered, missing - 1) * state.DurationMs;
        return new(state.Count + (int)recovered, capacity, Math.Max(0, next - now) / 1000d, state.DurationMs / 1000d, recovered > 0);
    }
    internal int[] Skills => states.Values.Select(s => s.Skill).ToArray();
}
