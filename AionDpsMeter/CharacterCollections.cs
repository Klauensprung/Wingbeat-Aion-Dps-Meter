using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

public sealed record CollectionValue(int Id, int Value);
public sealed record CollectionList(long Timestamp, CollectionValue[] Entries);
public sealed record AchievementSnapshot(long Timestamp, bool Complete, int[] Completed)
{
    public long? FullSnapshotAt { get; init; }
}
public sealed record CharacterCollectionSnapshot(CollectionList? Skins = null, CollectionList? Decorations = null, CollectionList? Monoliths = null,
    AchievementSnapshot? Achievements = null);
internal static class CharacterCollections
{
    internal static void Validate(CharacterCollectionSnapshot state)
    {
        if (state.Achievements is { } achievements && (achievements.Timestamp < 0 || achievements.Completed == null || achievements.Completed.Length > 16384 ||
            achievements.FullSnapshotAt is long baseline && (baseline < 0 || baseline > achievements.Timestamp || !achievements.Complete) ||
            achievements.Completed.Any(id => id <= 0) || achievements.Completed.Distinct().Count() != achievements.Completed.Length))
            throw new InvalidDataException("Invalid achievement snapshot.");
        foreach (var (list, decorations) in new[] { (state.Skins, false), (state.Decorations, true), (state.Monoliths, false) }) {
            if (list == null) continue;
            if (list.Timestamp < 0 || list.Entries == null || list.Entries.Length > 4096 || list.Entries.Any(e => e == null || e.Id <= 0 || e.Value < 0))
                throw new InvalidDataException("Invalid character collection.");
            // Decorations are keyed by equipped slot; other collections by item ID.
            if (list.Entries.Select(e => decorations ? e.Value : e.Id).Distinct().Count() != list.Entries.Length || decorations && list.Entries.Any(e => e.Value is < 1 or > 255))
                throw new InvalidDataException("Duplicate or invalid collection key.");
        }
    }
    internal static CollectionValue[] Read(ref WireReader reader, ushort opcode)
    {
        int count = reader.Var(); if (count > 4096) throw new InvalidDataException("Collection exceeds limit.");
        var entries = new List<CollectionValue>(count);
        for (int i = 0; i < count; i++) {
            int id = checked((int)reader.U32()); int value = opcode == 0xaf8a ? reader.Byte() : checked((int)reader.U32());
            if (opcode != 0xaf8a || value > 0) entries.Add(new(id, value));
        }
        // The reference explicitly leaves SkinList's tail uninterpreted.
        if (opcode == 0x5b8d) reader.Take(reader.Remaining);
        else if (reader.Remaining != 0) throw new InvalidDataException("Unknown collection trailer.");
        var list = new CollectionList(0, entries.ToArray());
        Validate(opcode == 0xaf8a ? new(Decorations: list) : opcode == 0x0be3 ? new(Monoliths: list) : new(Skins: list));
        return list.Entries;
    }
    internal static CharacterCollectionSnapshot Apply(CharacterCollectionSnapshot? previous, NetworkEvent message)
    {
        var state = previous ?? new();
        if (message.Kind == "achievements") {
            var oldAchievements = state.Achievements;
            if (message.Timestamp < 0 || oldAchievements != null && message.Timestamp < oldAchievements.Timestamp) return state;
            bool all = message.Fields.GetProperty("All").GetBoolean();
            var completed = message.Fields.GetProperty("Completed").Deserialize<int[]>() ?? throw new InvalidDataException("Missing achievements.");
            var next = new AchievementSnapshot(message.Timestamp, all || oldAchievements?.Complete == true,
                (all ? completed : (oldAchievements?.Completed ?? []).Concat(completed)).Distinct().Order().ToArray()) { FullSnapshotAt = all ? message.Timestamp : oldAchievements?.FullSnapshotAt };
            state = state with { Achievements = next }; Validate(state); return state;
        }
        var old = message.Kind switch { "skins" => state.Skins, "decorations" => state.Decorations, "monoliths" => state.Monoliths, _ => throw new InvalidDataException("Unknown collection kind.") };
        if (message.Timestamp < 0 || old != null && message.Timestamp < old.Timestamp) return state;
        var list = new CollectionList(message.Timestamp, message.Fields.GetProperty("Entries").Deserialize<CollectionValue[]>()!);
        state = message.Kind switch { "skins" => state with { Skins = list }, "decorations" => state with { Decorations = list }, _ => state with { Monoliths = list } };
        Validate(state); return state;
    }
}
