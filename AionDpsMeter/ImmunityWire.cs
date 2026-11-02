using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal sealed record ImmunityReading(int EntityId, int[] EffectIds);
internal static class ImmunityWire
{
    internal static ImmunityReading Read(ref WireReader reader)
    {
        int entity = reader.Var(), count = reader.Var();
        if (entity <= 0 || count > 256 || reader.Remaining != count * 4) throw new InvalidDataException("Invalid immunity record.");
        var ids = new int[count];
        for (int i = 0; i < count; i++) ids[i] = unchecked((int)reader.U32());
        return new(entity, ids);
    }
    internal static int[]? Effects(JsonElement fields)
    {
        if (!fields.TryGetProperty("EffectIds", out var values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > 256) return null;
        var result = new int[values.GetArrayLength()]; int at = 0;
        foreach (var value in values.EnumerateArray()) {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out result[at++])) return null;
        }
        return result;
    }
}
