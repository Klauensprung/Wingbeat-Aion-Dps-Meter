using System.Collections.Frozen;
using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

// Keys are static NPC/template IDs, never the temporary entity IDs used in combat.
public sealed class NpcNames
{
    readonly FrozenDictionary<int, string> names;
    public static string LocalPath => AppDataPaths.FilePath("npc-names.json");
    public static NpcNames Default { get; } = new();
    public int Count => names.Count;
    public string? LoadError { get; }

    public NpcNames(string? path = null) : this(path ?? LocalPath, path == null) { }

    internal NpcNames(string path, bool includeBundled)
    {
        var loaded = new Dictionary<int, string>(); var errors = new List<string>();
        if (includeBundled) {
            try {
                using var stream = typeof(NpcNames).Assembly.GetManifestResourceStream("Wingbeat.NpcNames.json")
                    ?? throw new InvalidDataException("Bundled NPC names are missing.");
                loaded = SkillNames.Read(stream, "Bundled NPC names");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { errors.Add(ex.Message); }
        }
        try {
            foreach (var entry in SkillNames.Read(path, "NPC names")) loaded[entry.Key] = entry.Value;
        }
        catch (Exception ex) when (includeBundled && (ex is FileNotFoundException or DirectoryNotFoundException)) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { errors.Add(ex.Message); }
        names = loaded.ToFrozenDictionary(); LoadError = errors.Count == 0 ? null : string.Join("; ", errors);
    }

    public string GetName(int npcId) => names.GetValueOrDefault(npcId) ?? BossCatalogue.Default.Find(npcId)?.Name ?? $"NPC {npcId}";

    internal Actor Spawn(ref WireReader reader)
    {
        int entity = reader.Var();
        if (entity <= 0) throw new InvalidDataException("Invalid NPC entity ID.");
        reader.Take(2); // Appearance/type flags. They are not the NPC template ID.
        int named = reader.Byte();
        if (named is not (0 or 1)) throw new InvalidDataException("Unknown NPC name layout.");
        // A summon can carry its owner's name here, not the creature's name.
        if (named == 1) PlayerIdentity.Name(ref reader);
        uint rawNpcId = reader.U32();
        if (rawNpcId is 0 or > int.MaxValue) throw new InvalidDataException("Invalid NPC template ID.");
        int npcId = (int)rawNpcId;
        return new(entity, GetName(npcId), false, false, 0, "Unknown", 0) { NpcId = npcId };
    }
}
