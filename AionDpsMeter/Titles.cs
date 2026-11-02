using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

public sealed record EquippedTitle(byte Slot, int Id);
public sealed record TitleSnapshot(long Timestamp, int[] Owned, EquippedTitle[] Equipped);
internal static class Titles
{
    internal static void Validate(TitleSnapshot state)
    {
        if (state.Timestamp < 0 || state.Owned == null || state.Equipped == null || state.Owned.Length > 4096 || state.Equipped.Length > 256 ||
            state.Owned.Any(id => id <= 0) || state.Owned.Distinct().Count() != state.Owned.Length ||
            state.Equipped.Any(t => t == null || t.Id < 0) || state.Equipped.Select(t => t.Slot).Distinct().Count() != state.Equipped.Length)
            throw new InvalidDataException("Invalid title snapshot.");
    }
    internal static TitleSnapshot Read(ref WireReader reader, long timestamp)
    {
        int count = reader.Var(); if (count > 4096) throw new InvalidDataException("Too many titles.");
        var owned = new int[count]; for (int i = 0; i < count; i++) owned[i] = checked((int)reader.U32());
        count = reader.Var(); if (count > 256) throw new InvalidDataException("Too many title slots.");
        var equipped = new EquippedTitle[count]; for (int i = 0; i < count; i++) equipped[i] = new(reader.Byte(), checked((int)reader.U32()));
        if (reader.Remaining != 0) throw new InvalidDataException("Unknown title trailer.");
        var state = new TitleSnapshot(timestamp, owned, equipped); Validate(state); return state;
    }
    internal static TitleSnapshot? Apply(TitleSnapshot? previous, NetworkEvent message)
    {
        if (message.Timestamp < 0 || previous != null && message.Timestamp < previous.Timestamp) return previous;
        var state = message.Fields.Deserialize<TitleSnapshot>() ?? throw new InvalidDataException("Missing title snapshot.");
        state = state with { Timestamp = message.Timestamp }; Validate(state); return state;
    }
    internal static string Slot(byte slot) => slot switch { 1 => "Attack", 2 => "Defense", 3 => "Other", _ => $"Slot {slot}" };
}
