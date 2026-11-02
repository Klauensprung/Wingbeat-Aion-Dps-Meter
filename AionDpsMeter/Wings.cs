using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

public sealed record CapturedWing(int Id, int Level);
public sealed record WingSnapshot(long ListAt, CapturedWing[] Owned, long EquippedAt = -1, int? Equipped = null, long SkinAt = -1, int? Skin = null);
internal static class Wings
{
    internal static void Validate(WingSnapshot state)
    {
        if (state.ListAt < -1 || state.EquippedAt < -1 || state.SkinAt < -1 || state.Equipped < 0 || state.Skin < 0 || state.Owned == null || state.Owned.Length > 1024 ||
            state.Owned.Any(w => w == null || w.Id <= 0 || w.Level is < 0 or > 255) || state.Owned.Select(w => w.Id).Distinct().Count() != state.Owned.Length)
            throw new InvalidDataException("Invalid wing snapshot.");
    }
    internal static CapturedWing[] ReadList(ref WireReader reader)
    {
        int count = reader.Var(); if (count > 1024) throw new InvalidDataException("Too many wings.");
        var wings = new CapturedWing[count];
        for (int i = 0; i < count; i++) wings[i] = new(checked((int)reader.U32()), reader.Byte());
        if (reader.Remaining != 0) throw new InvalidDataException("Unknown wing list trailer.");
        Validate(new(0, wings)); return wings;
    }
    internal static WingSnapshot? Apply(WingSnapshot? previous, NetworkEvent message)
    {
        if (message.Timestamp < 0) return previous;
        var state = previous ?? new(-1, []);
        if (message.Kind == "wings") {
            if (message.Timestamp < state.ListAt) return previous;
            state = state with { ListAt = message.Timestamp, Owned = message.Fields.GetProperty("Wings").Deserialize<CapturedWing[]>()! };
        } else if (message.Kind == "wing-change") {
            bool skin = message.Fields.GetProperty("Skin").GetBoolean();
            if (message.Timestamp < (skin ? state.SkinAt : state.EquippedAt)) return previous;
            int id = message.Fields.GetProperty("WingId").GetInt32();
            state = skin ? state with { SkinAt = message.Timestamp, Skin = id } : state with { EquippedAt = message.Timestamp, Equipped = id };
        } else return previous;
        Validate(state); return state;
    }
}
