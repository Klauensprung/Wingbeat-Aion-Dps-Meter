using System.Collections.Frozen;
using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal sealed class WorldObjectNames
{
    static readonly Lazy<WorldObjectNames> instance = new(() => new());
    internal static WorldObjectNames Default => instance.Value;
    readonly FrozenDictionary<int, string> names;
    internal int Count => names.Count;
    internal string? LoadError { get; }
    internal string Find(int id) => names.GetValueOrDefault(id) ?? $"Object {id}";
    WorldObjectNames()
    {
        try {
            using var stream = typeof(WorldObjectNames).Assembly.GetManifestResourceStream("Wingbeat.WorldObjectNames.json") ?? throw new InvalidDataException("Object names missing.");
            var data = JsonSerializer.Deserialize<ItemNameData>(stream) ?? throw new InvalidDataException("Empty object names.");
            Validate(data); names = data.Names.ToFrozenDictionary();
        } catch (Exception ex) when (ex is IOException or JsonException or ArgumentException) {
            names = FrozenDictionary<int, string>.Empty; LoadError = "Object names unavailable; showing IDs.";
        }
    }
    internal static void Validate(ItemNameData data)
    {
        if (data.SourceSha256?.Length != 64 || !data.SourceSha256.All(Uri.IsHexDigit) || data.Names is not { Count: > 0 and <= 4096 } ||
            data.Names.Any(p => p.Key <= 0 || string.IsNullOrWhiteSpace(p.Value) || p.Value.Length > 300))
            throw new InvalidDataException("Invalid object-name dictionary.");
    }
}
