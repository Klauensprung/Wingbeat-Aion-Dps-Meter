using System.Collections.Frozen;
using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal sealed record ZoneNameData(string Source, string SourceSha256, Dictionary<int, string> Names);
internal sealed class ZoneNames
{
    static readonly Lazy<ZoneNames> instance = new(() => new());
    internal static ZoneNames Default => instance.Value;
    readonly FrozenDictionary<int, string> names;
    internal int Count => names.Count;
    internal string? LoadError { get; }
    internal string Find(int id) => id == 0 ? "Unknown map" : names.GetValueOrDefault(id) ?? $"Map {id}";
    ZoneNames()
    {
        try {
            using var stream = typeof(ZoneNames).Assembly.GetManifestResourceStream("Wingbeat.Zones.json") ?? throw new InvalidDataException("Zone names missing.");
            var data = JsonSerializer.Deserialize<ZoneNameData>(stream) ?? throw new InvalidDataException("Empty zone names.");
            Validate(data); names = data.Names.ToFrozenDictionary();
        } catch (Exception ex) when (ex is IOException or JsonException or ArgumentException) {
            names = FrozenDictionary<int, string>.Empty; LoadError = "Map names unavailable; showing IDs.";
        }
    }
    internal static void Validate(ZoneNameData data)
    {
        if (data.SourceSha256?.Length != 64 || !data.SourceSha256.All(Uri.IsHexDigit) || data.Names is not { Count: > 0 and <= 10000 } ||
            data.Names.Any(p => p.Key <= 0 || string.IsNullOrWhiteSpace(p.Value) || p.Value.Length > 300))
            throw new InvalidDataException("Invalid zone-name dictionary.");
    }
}
