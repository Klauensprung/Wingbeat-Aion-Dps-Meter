using System.Collections.Frozen;
using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal sealed record ItemNameData(string Source, string SourceSha256, Dictionary<int, string> Names);
internal sealed class ItemNames
{
    static readonly Lazy<ItemNames> instance = new(() => new());
    internal static ItemNames Default => instance.Value;
    readonly FrozenDictionary<int, string> names;
    internal int Count => names.Count;
    internal string? LoadError { get; }
    internal string Find(int id) => names.GetValueOrDefault(id) ?? $"Item {id}";
    internal IEnumerable<KeyValuePair<int,string>> Search(string query) => names.Where(p=>p.Value.Contains(query,StringComparison.OrdinalIgnoreCase)||p.Key.ToString()==query).OrderBy(p=>p.Value).Take(50);
    ItemNames()
    {
        try {
            using var stream = typeof(ItemNames).Assembly.GetManifestResourceStream("Wingbeat.Items.json") ?? throw new InvalidDataException("Item names missing.");
            var data = JsonSerializer.Deserialize<ItemNameData>(stream) ?? throw new InvalidDataException("Empty item names.");
            Validate(data); names = data.Names.ToFrozenDictionary();
        } catch (Exception ex) when (ex is IOException or JsonException or ArgumentException) {
            names = FrozenDictionary<int, string>.Empty; LoadError = "Item names unavailable; showing IDs.";
        }
    }
    internal static void Validate(ItemNameData data)
    {
        if (data.SourceSha256?.Length != 64 || !data.SourceSha256.All(Uri.IsHexDigit) || data.Names is not { Count: > 0 and <= 100000 } ||
            data.Names.Any(p => p.Key <= 0 || string.IsNullOrWhiteSpace(p.Value) || p.Value.Length > 300))
            throw new InvalidDataException("Invalid item-name dictionary.");
    }
}
