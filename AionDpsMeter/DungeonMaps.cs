using System.Collections.Frozen;
using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal sealed record DungeonMapData(string Source, string SourceSha256, int[] Dungeons, int[] Nightmares,
    Dictionary<int, int[]> BossesByMap, int[] GlobalBosses);
internal static class DungeonMaps
{
    static FrozenDictionary<int, FrozenSet<int>> bosses = FrozenDictionary<int, FrozenSet<int>>.Empty;
    static FrozenSet<int> globalBosses = FrozenSet<int>.Empty;
    static readonly FrozenSet<int> maps = Load();
    internal static bool NamesBosses(int map) => bosses.TryGetValue(map, out var ids) && ids.Count > 0;
    internal static bool IsBoss(int npc, int map) => bosses.TryGetValue(map, out var ids) && ids.Count > 0 ? ids.Contains(npc) : globalBosses.Contains(npc);
    internal static bool Contains(int id) => maps.Contains(id);
    internal static int Count => maps.Count;
    internal static string? LoadError { get; private set; }
    static FrozenSet<int> Load()
    {
        try {
            using var stream = typeof(DungeonMaps).Assembly.GetManifestResourceStream("Wingbeat.DungeonMaps.json") ?? throw new InvalidDataException("Missing dungeon map catalogue.");
            var data = JsonSerializer.Deserialize<DungeonMapData>(stream) ?? throw new InvalidDataException("Empty dungeon map catalogue.");
            if (data.SourceSha256?.Length != 64 || !data.SourceSha256.All(Uri.IsHexDigit) || data.Dungeons is not { Length: > 0 and <= 4096 } ||
                data.Nightmares == null || data.Nightmares.Length > 4096 || data.Dungeons.Concat(data.Nightmares).Any(i => i <= 0))
                throw new InvalidDataException("Invalid dungeon map catalogue.");
            if (data.BossesByMap == null || data.BossesByMap.Count > 4096 || data.GlobalBosses is not { Length: > 0 and <= 20000 } ||
                data.GlobalBosses.Any(id => id <= 0) || data.BossesByMap.Any(p => !data.Dungeons.Contains(p.Key) || p.Value == null || p.Value.Length > 20000 || p.Value.Any(id => id <= 0)))
                throw new InvalidDataException("Invalid map boss catalogue.");
            bosses = data.BossesByMap.ToFrozenDictionary(p => p.Key, p => p.Value.ToFrozenSet());
            globalBosses = data.GlobalBosses.ToFrozenSet();
            return data.Dungeons.Concat(data.Nightmares).ToFrozenSet();
        } catch (Exception ex) when (ex is IOException or JsonException or ArgumentException) {
            LoadError = "Dungeon map catalogue unavailable; waiting for dungeon-state packets.";
            return FrozenSet<int>.Empty;
        }
    }
}
