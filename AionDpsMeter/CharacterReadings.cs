using System.Globalization;
using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

public sealed record PlayerStatReading(long Timestamp, Dictionary<ushort, int> Values);
public sealed record CharacterLink(int EntityId, uint CharacterId);
public sealed record CooldownRow(string Skill, int SkillCode, int RemainingMs, long Timestamp);
public sealed record PlayerStatRow(int StatId, string Stat, string Value, int RawValue)
{
    public string Detail => $"ID {StatId}  /  Raw {RawValue:N0}";
}

internal static class CharacterReadings
{
    // Display labels and units are protocol metadata; unknown IDs remain visible.
    static readonly Dictionary<ushort, (string Label, bool Percent)> Labels = new() {
        [19] = ("Base attack", false), [317] = ("Equipment attack", false), [31] = ("Maximum attack", false),
        [33] = ("Minimum attack", false), [425] = ("Attack increase", true), [56] = ("PvE attack", false),
        [50] = ("Boss attack", false), [28] = ("Damage boost", true), [32] = ("Weapon damage boost", true),
        [379] = ("PvE damage boost", true), [520] = ("Boss damage boost", true), [44] = ("Critical damage boost", true),
        [442] = ("Perfect", true), [443] = ("Smite", true), [589] = ("Front damage boost", true),
        [102] = ("Back damage boost", true), [483] = ("Congni damage boost", true), [485] = ("Fera damage boost", true),
        [487] = ("Nature damage boost", true), [489] = ("Varien damage boost", true), [282] = ("Combat speed", true)
    };
    internal static PlayerStatReading Stats(ref WireReader reader, long time)
    {
        reader.Take(2);
        int count = reader.Var();
        if (count is < 1 or > 2048 || reader.Remaining < count * 6) throw new InvalidDataException("Invalid player stat count.");
        var values = new Dictionary<ushort, int>();
        for (int i = 0; i < count; i++)
            if (!values.TryAdd(reader.U16(), unchecked((int)reader.U32()))) throw new InvalidDataException("Duplicate player stat ID.");
        // EU captures append an opaque uint64. Its meaning is not a stat or CP.
        if (reader.Remaining == 8) reader.U64();
        if (reader.Remaining != 0) throw new InvalidDataException("Unknown player stat trailer.");
        return new(time, values);
    }
    internal static void Validate(PlayerStatReading stats)
    {
        if (stats.Timestamp < 0 || stats.Values == null || stats.Values.Count is < 1 or > 2048)
            throw new InvalidDataException("Invalid player stats.");
    }
    internal static PlayerStatReading? Copy(PlayerStatReading? stats) => stats == null ? null : stats with { Values = new(stats.Values) };
    internal static CooldownRow[] Cooldowns(IEnumerable<NetworkEvent> events, SkillNames names)
    {
        var latest = new Dictionary<int, CooldownRow>();
        foreach (var e in events) {
            if (e.Kind != "skill-cooldown" || e.Fields.ValueKind != JsonValueKind.Object ||
                !e.Fields.TryGetProperty("SkillCode", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out int skill) || skill <= 0 ||
                !e.Fields.TryGetProperty("RemainingMs", out var ms) || ms.ValueKind != JsonValueKind.Number || !ms.TryGetInt32(out int remaining) || remaining is < 0 or > 86400000) continue;
            if (!latest.TryGetValue(skill, out var previous) || e.Timestamp >= previous.Timestamp)
                latest[skill] = new(names.GetSkillName(skill), skill, remaining, e.Timestamp);
        }
        return latest.Values.OrderByDescending(r => r.Timestamp).ThenBy(r => r.SkillCode).ToArray();
    }
    internal static PlayerStatRow[] Rows(PlayerStatReading? stats) => stats == null ? [] : stats.Values.OrderBy(p => !Labels.ContainsKey(p.Key)).ThenBy(p => p.Key).Select(p => {
        var label = Labels.GetValueOrDefault(p.Key, ($"Stat {p.Key}", false));
        return new PlayerStatRow(p.Key, label.Item1, label.Item2 ? (p.Value / 100d).ToString("0.##", CultureInfo.CurrentCulture) + "%" : p.Value.ToString("N0"), p.Value);
    }).ToArray();
}
