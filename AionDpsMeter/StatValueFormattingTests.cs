using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;
internal static class StatValueFormattingTests
{
    internal static void Run(Action<bool,string> check, string temp)
    {
        check(StatNames.Default.Value(146, 1035) == "10.3%" && StatNames.Default.Value(146, -1035) == "-10.3%",
            "Percentage stats apply hundredth scaling and reference binary rounding symmetrically");
        check(StatNames.Default.Value(242, 1234) == "12.3" && StatNames.Default.Value(298, 1234) == "12.3" && StatNames.Default.Value(19, 1234) == "1,234",
            "Stamina and flight power scale independently of ordinary flat stats");
        check(StatNames.Default.Value(65535, 1234) == "1234" && StatNames.Default.Value(19, long.MaxValue) == "9223372036854775807 (raw)",
            "Unknown stats and values outside exact JS integers preserve raw precision");
        using var stream = typeof(StatNames).Assembly.GetManifestResourceStream("Wingbeat.StatNames.json")!;
        var data = JsonSerializer.Deserialize<StatNameData>(stream)!;
        long[] values = [0, 1, -1, 5, 99, 100, 999, 1000, 1005, 1015, 1025, 1035, -1035, 9999, 10000, 123456, 9007199254740991];
        using var output = new StreamWriter(Path.Combine(temp, "stat-value-fixtures.jsonl"));
        foreach (int id in data.Names.Keys) foreach (long value in values)
            output.WriteLine(JsonSerializer.Serialize(new {Id=id,Raw=value,Expected=StatNames.Default.Value(id,value)}));
    }
}
