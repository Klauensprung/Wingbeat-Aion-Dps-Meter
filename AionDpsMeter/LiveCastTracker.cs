using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal sealed record LiveCastView(int SkillCode, string Status, double? RemainingSeconds, double? Progress);

// One local action only. Presentation expiry never asserts successful completion.
internal sealed class LiveCastTracker
{
    sealed record State(int Skill, bool Charge, long Started, int Duration, long? Stopped = null, string? Reason = null);
    State? current;
    long latest = -1;
    (long At, int Skill)? executed;
    internal void Clear(long boundary = -1) { current = null; latest = boundary; executed = null; }
    internal void Apply(NetworkEvent message, int local)
    {
        if (message.Timestamp < 0 || message.Timestamp < latest || message.Fields.ValueKind != JsonValueKind.Object) return;
        if (message.Kind is "local-zone" or "account-login") { Clear(message.Timestamp); return; }
        if (local <= 0) return;
        var fields = message.Fields;
        int Number(string name) => fields.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int n) ? n : 0;
        if (message.Kind is "entity-death" or "entity-removed") {
            if (Number("EntityId") == local) Clear(message.Timestamp);
            return;
        }
        if (Number("ActorId") != local) return;
        if (message.Kind is "cast-start" or "charge-start") {
            var cast = fields.Deserialize<CastReading>();
            if (cast == null || cast.SkillCode <= 0 || cast.CastingMs < 0) return;
            if (executed == (message.Timestamp, cast.SkillCode)) return;
            bool charge = message.Kind == "charge-start";
            if (current is { } prior && prior.Started == message.Timestamp && prior.Skill == cast.SkillCode && prior.Charge == charge) return;
            current = new(cast.SkillCode, charge, message.Timestamp, charge ? 0 : cast.CastingMs);
            latest = message.Timestamp;
        } else if (message.Kind is "cast-stop" or "charge-stop") {
            var stop = fields.Deserialize<CastStopReading>();
            if (stop == null || current is not { Stopped: null } prior || prior.Skill != stop.SkillCode || prior.Charge != (message.Kind == "charge-stop")) return;
            current = prior with { Stopped = message.Timestamp, Reason = stop.Reason };
            latest = message.Timestamp;
        } else if (message.Kind is "skill-use" or "skill-end") {
            int skill = Number("SkillCode");
            if (skill <= 0) return;
            executed = (message.Timestamp, skill);
            latest = message.Timestamp;
            if (current?.Skill == skill) current = null;
        }
    }
    internal LiveCastView? Snapshot(long now)
    {
        if (current is not { } cast || now < cast.Started) return null;
        if (cast.Stopped is long stopped) {
            if (now < stopped || now - stopped >= 1000) return null;
            return new(cast.Skill, cast.Reason switch { "interrupted" => "Interrupted", "cancelled" => "Cancelled", _ => "Failed" }, null, 0);
        }
        if (cast.Duration > 0) {
            double elapsed = now - cast.Started;
            if (elapsed >= cast.Duration) return null;
            return new(cast.Skill, "Casting", (cast.Duration - elapsed) / 1000d, elapsed / cast.Duration);
        }
        return new(cast.Skill, cast.Charge ? "Charging" : "Casting", null, null);
    }
}
