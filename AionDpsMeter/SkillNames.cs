using System.Collections.Frozen;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

public readonly record struct SkillName(int Code, int? MatchedCode, string Name, string Match);

// Display metadata only: names must never change a packet's skill ID or classify healing.
public sealed class SkillNames
{
    readonly FrozenDictionary<int, string> names;
    public static string LocalPath => Path.Combine(AppContext.BaseDirectory, "local-data", "skill-names.json");
    public int Count => names.Count;
    public string? LoadError { get; }

    public SkillNames(string? path = null)
    {
        path ??= LocalPath;
        try { names = Read(path).ToFrozenDictionary(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            names = FrozenDictionary<int, string>.Empty;
            LoadError = ex.Message;
        }
    }

    public static Dictionary<int, string> Read(string path, string catalog = "Skill names")
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 16 * 1024 * 1024) throw new InvalidDataException($"{catalog} exceed the 16 MB limit.");
        using var json = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 4 });
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{catalog}: expected an object mapping IDs to text.");
        var result = new Dictionary<int, string>();
        foreach (var item in json.RootElement.EnumerateObject())
        {
            if (!int.TryParse(item.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id <= 0 ||
                item.Value.ValueKind != JsonValueKind.String) throw new InvalidDataException($"{catalog} must map positive integer IDs to text.");
            var name = item.Value.GetString()!.Trim();
            if (name.Length is 0 or > 240 || name.Any(char.IsControl) || !result.TryAdd(id, name) || result.Count > 100000)
                throw new InvalidDataException($"{catalog}: invalid or duplicate entry, or more than 100,000 entries.");
        }
        if (result.Count == 0) throw new InvalidDataException($"{catalog}: the catalog is empty.");
        return result;
    }

    public SkillName Resolve(int code)
    {
        if (names.TryGetValue(code, out var exact)) return new(code, code, exact, "exact");
        // Only the documented player-skill range uses the last four digits for variants.
        // NPC attacks, items and effect IDs require an exact match; never round those.
        int family = code / 10000 * 10000;
        if (code is >= 11000000 and < 20000000 && names.TryGetValue(family, out var name))
            return new(code, family, name, "family");
        return new(code, null, $"Unknown skill {code}", "unknown");
    }

    public string GetSkillName(int code) => Resolve(code).Name;

    public static SkillNames Import(string path)
    {
        var valid = Read(path); // Validate fully before touching the last working catalog.
        Directory.CreateDirectory(Path.GetDirectoryName(LocalPath)!);
        var temp = LocalPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(valid));
        File.Move(temp, LocalPath, true);
        return new SkillNames();
    }
}
