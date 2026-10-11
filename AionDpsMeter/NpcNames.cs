using System.Collections.Frozen;
using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

// Keys are static NPC/template IDs, never the temporary entity IDs used in combat.
public sealed class NpcNames
{
    readonly FrozenDictionary<int, string> names;
    public static string LocalPath => Path.Combine(AppContext.BaseDirectory, "local-data", "npc-names.json");
    public static NpcNames Default { get; } = new();
    public int Count => names.Count;
    public string? LoadError { get; }

    public NpcNames(string? path = null)
    {
        try { names = SkillNames.Read(path ?? LocalPath, "NPC names").ToFrozenDictionary(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            names = FrozenDictionary<int, string>.Empty; LoadError = ex.Message;
        }
    }

    public string GetName(int npcId) => names.GetValueOrDefault(npcId) ?? $"NPC {npcId}";

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
