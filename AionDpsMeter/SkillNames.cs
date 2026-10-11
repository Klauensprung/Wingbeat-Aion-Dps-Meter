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

    // Explicit paths stay isolated for catalog validation; normal startup layers local
    // overrides over the embedded defaults without requiring a writable app folder.
    public SkillNames(string? path = null) : this(path ?? LocalPath, path == null) { }

    internal SkillNames(string path, bool includeBundled)
    {
        var loaded = new Dictionary<int, string>();
        var errors = new List<string>();
        if (includeBundled)
        {
            try
            {
                using var stream = typeof(SkillNames).Assembly.GetManifestResourceStream("Wingbeat.SkillNames.json")
                    ?? throw new InvalidDataException("Bundled skill names are missing.");
                loaded = Read(stream, "Bundled skill names");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                errors.Add(ex.Message);
            }
        }
        try
        {
            // Read validates the complete override before merging any entries.
            foreach (var pair in Read(path)) loaded[pair.Key] = pair.Value;
        }
        catch (Exception ex) when (includeBundled && (ex is FileNotFoundException or DirectoryNotFoundException))
        {
            // A fresh installation needs no local catalog.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            errors.Add(ex.Message);
        }
        names = loaded.ToFrozenDictionary();
        LoadError = errors.Count == 0 ? null : string.Join("; ", errors);
    }

    public static Dictionary<int, string> Read(string path, string catalog = "Skill names")
    {
        using var stream = File.OpenRead(path);
        return Read(stream, catalog);
    }

    internal static Dictionary<int, string> Read(Stream stream, string catalog)
    {
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
