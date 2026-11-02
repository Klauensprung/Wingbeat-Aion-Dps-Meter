using System.Collections.Frozen;
using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;
internal sealed record StatNameData(string Source, string SourceSha256, Dictionary<int, string> Names, int[] PercentStats, int[] HundredthStats);
internal sealed class StatNames
{
    static readonly Lazy<StatNames> instance = new(() => new());
    internal static StatNames Default => instance.Value;
    readonly FrozenDictionary<int, string> names;
    readonly FrozenSet<int> percent, hundredths;
    internal int Count => names.Count;
    internal string? LoadError { get; }
    internal string Find(int id) => names.GetValueOrDefault(id) ?? $"Stat {id}";
    internal string Value(int id, long raw) => names.ContainsKey(id)
        ? StatValueFormatting.Format(raw, percent.Contains(id), hundredths.Contains(id))
        : raw.ToString(System.Globalization.CultureInfo.InvariantCulture);
    StatNames() {
        try {
            using var stream = typeof(StatNames).Assembly.GetManifestResourceStream("Wingbeat.StatNames.json") ?? throw new InvalidDataException();
            var data = JsonSerializer.Deserialize<StatNameData>(stream) ?? throw new InvalidDataException();
            if (data.SourceSha256?.Length != 64 || !data.SourceSha256.All(Uri.IsHexDigit) || data.Names is not {Count: >0 and <=4096} ||
                data.Names.Any(p => p.Key is <=0 or >65535 || string.IsNullOrWhiteSpace(p.Value) || p.Value.Length > 300)) throw new InvalidDataException();
            if (data.PercentStats == null || data.HundredthStats == null ||
                data.PercentStats.Concat(data.HundredthStats).Any(id => !data.Names.ContainsKey(id)) ||
                data.PercentStats.Distinct().Count() != data.PercentStats.Length || data.HundredthStats.Distinct().Count() != data.HundredthStats.Length)
                throw new InvalidDataException();
            names = data.Names.ToFrozenDictionary(); percent = data.PercentStats.ToFrozenSet(); hundredths = data.HundredthStats.ToFrozenSet();
        } catch (Exception ex) when (ex is IOException or JsonException or ArgumentException) {
            names = FrozenDictionary<int,string>.Empty; LoadError = "Stat names unavailable; showing IDs.";
            percent = FrozenSet<int>.Empty; hundredths = FrozenSet<int>.Empty;
        }
    }
}
