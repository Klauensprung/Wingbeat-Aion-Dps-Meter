using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

public sealed record CastOutcome(int ActorId, int SkillCode, string Kind, int Starts, int Cancelled, int Interrupted, int Failed)
{
    public int NoStopRecorded => Starts - Cancelled - Interrupted - Failed;
}
public sealed record SavedCastOutcomes(CastOutcome[] Rows, int OmittedStarts);

internal static class CastOutcomes
{
    const int Limit = 2048;
    internal static void Validate(SavedCastOutcomes summary)
    {
        if (summary.Rows == null || summary.Rows.Length > Limit || summary.OmittedStarts < 0 ||
            summary.Rows.Any(r => r == null || r.ActorId <= 0 || r.SkillCode <= 0 || r.Kind is not ("cast" or "charge") || r.Starts <= 0 ||
                r.Cancelled < 0 || r.Interrupted < 0 || r.Failed < 0 || (long)r.Cancelled + r.Interrupted + r.Failed > r.Starts) ||
            summary.Rows.Select(r => (r.ActorId, r.SkillCode, r.Kind)).Distinct().Count() != summary.Rows.Length)
            throw new InvalidDataException("Invalid saved cast outcomes.");
    }
    internal static SavedCastOutcomes Copy(SavedCastOutcomes summary) => summary with { Rows = summary.Rows.ToArray() };
    internal static SavedCastOutcomes Build(Fight fight)
    {
        if (fight.SavedCasts is { } saved) { Validate(saved); return Copy(saved); }
        if (fight.LiveCasts is { } live) return live.Snapshot();
        var reducer = new CastOutcomeAccumulator();
        var reused = fight.Actors.Values.Where(a => a.OriginalEntityId > 0).Select(a => a.OriginalEntityId).ToHashSet();
        foreach (var message in fight.Network) reducer.Apply(message, reused);
        return reducer.Snapshot();
    }
}

// Bounded reduction in delivery order, matching the reference cast reducer.
// Live capture and detached fight snapshots own independent copies of this state.
internal sealed class CastOutcomeAccumulator
{
    const int Limit = 2048;
    readonly Dictionary<(int Actor, int Skill, string Kind), CastOutcome> rows = new();
    readonly Dictionary<(int Actor, int Skill), (int Actor, int Skill, string Kind)> pending = new();
    int omitted;
    internal bool HasData => rows.Count > 0 || omitted > 0;
    internal bool HasActor(int id) => rows.Keys.Any(k => k.Actor == id);
    internal CastOutcomeAccumulator Copy()
    {
        var copy = new CastOutcomeAccumulator { omitted = omitted };
        foreach (var pair in rows) copy.rows.Add(pair.Key, pair.Value);
        foreach (var pair in pending) copy.pending.Add(pair.Key, pair.Value);
        return copy;
    }
    internal void ArchiveActor(int id, int archived)
    {
        foreach (var key in rows.Keys.Where(k => k.Actor == id).ToArray()) {
            var row = rows[key]; rows.Remove(key);
            rows.Add((archived, key.Skill, key.Kind), row with { ActorId = archived });
        }
        // A stop on the reused wire ID cannot terminate the previous identity's cast.
        foreach (var key in pending.Keys.Where(k => k.Actor == id).ToArray()) pending.Remove(key);
    }
    internal int RowCount => rows.Count;
    internal int PendingCount => pending.Count;
    internal void Apply(AionDpsMeter.Models.NetworkEvent message, ISet<int>? reused = null)
    {
        if (message.Kind is not ("cast-start" or "charge-start" or "cast-stop" or "charge-stop") ||
            message.Timestamp < 0 || message.Fields.ValueKind != JsonValueKind.Object) return;
        try {
            if (message.Kind is "cast-start" or "charge-start") {
                var cast = message.Fields.Deserialize<CastReading>();
                if (cast == null || cast.ActorId <= 0 || cast.SkillCode <= 0) return;
                if (reused?.Contains(cast.ActorId) == true) { omitted++; return; }
                var key = (cast.ActorId, cast.SkillCode, message.Kind == "cast-start" ? "cast" : "charge");
                if (!rows.TryGetValue(key, out var row)) {
                    if (rows.Count >= Limit) { omitted++; pending.Remove((cast.ActorId, cast.SkillCode)); return; }
                    row = new(cast.ActorId, cast.SkillCode, key.Item3, 0, 0, 0, 0);
                }
                rows[key] = row with { Starts = row.Starts + 1 };
                pending[(cast.ActorId, cast.SkillCode)] = key;
            } else {
                var stop = message.Fields.Deserialize<CastStopReading>();
                if (stop == null || !pending.Remove((stop.ActorId, stop.SkillCode), out var key)) return;
                var row = rows[key];
                rows[key] = stop.Reason switch {
                    "cancelled" => row with { Cancelled = row.Cancelled + 1 },
                    "interrupted" => row with { Interrupted = row.Interrupted + 1 },
                    _ => row with { Failed = row.Failed + 1 }
                };
            }
        } catch (JsonException) { }
    }
    internal SavedCastOutcomes Snapshot() => new(rows.Values.OrderBy(r => r.ActorId).ThenBy(r => r.SkillCode).ThenBy(r => r.Kind).ToArray(), omitted);
}
