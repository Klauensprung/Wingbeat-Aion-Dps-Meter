namespace AionDpsMeter.Local;

internal static class SavedAppearance
{
    internal static CharacterPresets Copy(CharacterPresets value) => value with {
        Presets = value.Presets.ToArray(), Equipment = value.Equipment?.Select(e => e with { Dyes = e.Dyes.ToArray() }).ToArray()
    };
    internal static CharacterPresets Merge(CharacterPresets? old, CharacterPresets value)
    {
        if (old == null) return Copy(value);
        var current = value.CurrentAt >= old.CurrentAt && value.Current != null ? value.Current : old.Current;
        var wings = value.WingsAt >= old.WingsAt && value.Current != null ? value.Current : old.Current;
        if (current != null && wings != null) current = current with { WingId = wings.WingId, WingSkinId = wings.WingSkinId };
        bool equipment = value.Equipment != null && value.EquipmentAt >= old.EquipmentAt;
        bool presets = value.PresetsCaptured && value.PresetsAt >= old.PresetsAt;
        return Copy(value with {
            Timestamp = Math.Max(old.Timestamp, value.Timestamp), Current = current,
            CurrentAt = Math.Max(old.CurrentAt, value.CurrentAt), WingsAt = Math.Max(old.WingsAt, value.WingsAt),
            Equipment = equipment ? value.Equipment : old.Equipment, EquipmentAt = Math.Max(old.EquipmentAt, value.EquipmentAt),
            EquipmentComplete = equipment ? value.EquipmentComplete : old.EquipmentComplete,
            Presets = presets ? value.Presets : old.Presets, PresetsCaptured = presets || old.PresetsCaptured,
            PresetsAt = Math.Max(old.PresetsAt, value.PresetsAt)
        });
    }
    internal static bool Valid(CharacterPresets value, int server, long id)
    {
        if (value.Server != server || value.CharacterId != id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            string.IsNullOrWhiteSpace(value.Name) || value.Name.Length > 128 || value.Timestamp is < 0 or > 253402300799999 ||
            new[] { value.CurrentAt, value.WingsAt, value.EquipmentAt, value.PresetsAt }.Any(t => t < -1 || t > value.Timestamp) ||
            value.Presets == null || value.Presets.Length > 256 || value.PresetsCaptured != (value.PresetsAt >= 0) ||
            !value.PresetsCaptured && value.Presets.Length != 0 || (value.Current != null) != (value.CurrentAt >= 0) ||
            (value.Equipment != null) != (value.EquipmentAt >= 0)) return false;
        int bytes = 0;
        foreach (var p in value.Presets) {
            if (p == null || p.Name?.Length > 4096 || p.Data == null || !Data(p.Data, out int size)) return false;
            bytes += size;
        }
        if (bytes > AppearancePresetWire.MaxBytes) return false;
        if (value.Current is { } c && (c.Race is not (null or "light" or "dark") || c.Gender is not (null or "male" or "female") ||
            c.WingId < 0 || c.WingSkinId < 0 || !Data(c.Data, out _))) return false;
        return value.Equipment == null || value.Equipment.Length <= 128 && value.Equipment.All(e => e != null && e.Item >= 0 && e.Skin >= 0 &&
            e.Dyes != null && e.Dyes.Length <= 32 && e.Dyes.All(d => d != null && d.Slot is >= 1 and <= 256));
    }
    static bool Data(string? text, out int size)
    {
        size = 0; if (text == null) return true;
        if (text.Length > 87384) return false;
        try { size = Convert.FromBase64String(text).Length; return size <= 65536; } catch (FormatException) { return false; }
    }
    internal static CharacterPresets? View(SavedAccountCharacter row)
    {
        CharacterPresets? baseline = row.Character.Appearance is not { } data ? null : new(row.Character.Name, row.Server,
            row.Character.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), row.Timestamp, []) {
                Current = new(0, data.Race, data.Gender, data.Data, data.Wing, 0), CurrentAt = row.Timestamp, WingsAt = row.Timestamp,
                Equipment = data.Equipment, EquipmentAt = row.Timestamp, EquipmentComplete = true, PresetsCaptured = false
            };
        var merged = row.LiveAppearance == null ? baseline : Merge(baseline, row.LiveAppearance);
        return merged == null ? null : Copy(merged with { Name = row.Character.Name });
    }
}
