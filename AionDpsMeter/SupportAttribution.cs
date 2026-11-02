using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class SupportAttribution
{
    internal const string Field = "AttributedCasterId";
    internal static int? Captured(NetworkEvent e) => e.Fields.ValueKind == JsonValueKind.Object &&
        e.Fields.TryGetProperty(Field, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out int id) && id >= 0 ? id : null;
    internal static NetworkEvent Capture(NetworkEvent e, Fight fight)
    {
        if (e.Kind is not ("barrier" or "abnormal-apply" or "abnormal-refresh") || Captured(e) != null) return e;
        string key = e.Kind == "barrier" ? "ActorId" : "CasterId";
        if (!e.Fields.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out int caster) || caster <= 0) return e;
        int owner = fight.Actors.GetValueOrDefault(caster) is { Player: false, OwnerId: > 0 } summon ? summon.OwnerId.Value : caster;
        return WithCaster(e, owner);
    }
    internal static NetworkEvent WithCaster(NetworkEvent e, int caster)
    {
        var fields = e.Fields.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
        fields[Field] = JsonSerializer.SerializeToElement(caster);
        return e with { Fields = JsonSerializer.SerializeToElement(fields) };
    }
}
