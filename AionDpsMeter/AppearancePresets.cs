using System.IO;
using System.Text;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal sealed record AppearancePreset(byte Slot, string? Name, string Data)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string Label => Name ?? $"Preset {Slot + 1}";
    [System.Text.Json.Serialization.JsonIgnore]
    public int Bytes => Data.Length == 0 ? 0 : Data.Length / 4 * 3 - (Data.EndsWith("==") ? 2 : Data.EndsWith('=') ? 1 : 0);
}
internal sealed record AppearancePresetReading(AppearancePreset[] Presets);
internal sealed record CurrentAppearance(int EntityId, string? Race, string? Gender, string? Data, int WingId, int WingSkinId);
internal sealed record AppearanceEquipment(int Item, int Skin, VisibleDye[] Dyes);
internal sealed record CharacterPresets(string Name, int Server, string CharacterId, long Timestamp, AppearancePreset[] Presets)
{
    public CurrentAppearance? Current { get; init; }
    public bool PresetsCaptured { get; init; } = true;
    public AppearanceEquipment[]? Equipment { get; init; }
    public bool EquipmentComplete { get; init; }
    public long CurrentAt { get; init; } = -1;
    public long WingsAt { get; init; } = -1;
    public long EquipmentAt { get; init; } = -1;
    public long PresetsAt { get; init; } = -1;
}

internal static class AppearancePresetWire
{
    internal const int MaxBytes = 262144;
    internal static AppearancePresetReading? Read(ref WireReader r)
    {
        ushort result = r.U16(); int count = r.Var();
        if (count > 256) throw new InvalidDataException("Too many appearance presets.");
        var presets = new AppearancePreset[count]; int total = 0;
        for (int i = 0; i < count; i++) {
            byte slot = r.Byte(); int size = r.Var(); total = checked(total + size);
            if (size > 65536 || total > MaxBytes) throw new InvalidDataException("Appearance preset data exceeds limit.");
            string data = Convert.ToBase64String(r.Take(size));
            int length = r.Var(); if (length > 4096) throw new InvalidDataException("Appearance preset name exceeds limit.");
            string name = new UTF8Encoding(false, true).GetString(r.Take(length)).TrimEnd('\0'); r.Take(4);
            presets[i] = new(slot, name.Length == 0 ? null : name, data);
        }
        if (r.Remaining != 0) throw new InvalidDataException("Unexpected appearance preset trailer.");
        return result == 0 ? new(presets) : null;
    }
}

// Current character only: appearance blobs never enter combat history or raw-event retention.
internal sealed class AppearancePresetTracker
{
    Actor? owner;
    AppearancePresetReading? reading;
    CurrentAppearance? current;
    VisibleEquipmentSnapshot? equipment;
    long currentAt = -1, wingAt = -1;
    long at = -1, boundary = -1;
    internal void Clear(long timestamp) { if (timestamp < boundary) return; owner = null; reading = null; current = null; equipment = null; currentAt = wingAt = at = -1; boundary = timestamp; }
    internal void Follow(Actor actor)
    {
        if (owner != null && (owner.Server != actor.Server || (owner.PersistentId > 0 && actor.PersistentId > 0 ? owner.PersistentId != actor.PersistentId : owner.Name != actor.Name))) {
            reading = null; current = null; equipment = null; currentAt = wingAt = -1;
        }
        owner = actor;
    }
    internal void Apply(AionDpsMeter.Models.NetworkEvent message)
    {
        if (message.Timestamp < 0 || message.Timestamp > 253402300799999 || message.Timestamp < boundary) return;
        if (message.Kind == "visible-equipment") {
            if (owner != null && message.Fields.GetProperty("ActorId").GetInt32() == owner.Id) equipment = VisibleEquipment.Apply(equipment, message);
            return;
        }
        if (message.Kind == "appearance-current") {
            if (owner == null || message.Timestamp < currentAt) return;
            var appearance = message.Fields.Deserialize<CurrentAppearance>();
            if (appearance == null || appearance.EntityId != owner.Id || appearance.Race is not (null or "light" or "dark") ||
                appearance.Gender is not (null or "male" or "female") || appearance.WingId < 0 || appearance.WingSkinId < 0 ||
                !ValidData(appearance.Data)) return;
            if (current != null && message.Timestamp < wingAt) appearance = appearance with { WingId = current.WingId, WingSkinId = current.WingSkinId };
            current = appearance; currentAt = message.Timestamp; wingAt = Math.Max(wingAt, message.Timestamp); return;
        }
        if (message.Kind == "wing-change") {
            if (current == null || owner == null || message.Timestamp < wingAt || message.Fields.GetProperty("EntityId").GetInt32() != owner.Id) return;
            int id = message.Fields.GetProperty("WingId").GetInt32(); if (id < 0) return;
            current = message.Fields.GetProperty("Skin").GetBoolean() ? current with { WingSkinId = id } : current with { WingId = id };
            wingAt = message.Timestamp; return;
        }
        if (message.Kind != "appearance-presets" || message.Timestamp < at) return;
        var value = message.Fields.Deserialize<AppearancePresetReading>();
        if (value?.Presets == null || value.Presets.Length > 256) return;
        int bytes = 0;
        foreach (var preset in value.Presets) {
            if (preset == null || preset.Name?.Length > 4096 || preset.Data == null || preset.Data.Length > 87384) return;
            try { int size = Convert.FromBase64String(preset.Data).Length; if (size > 65536) return; bytes += size; }
            catch (FormatException) { return; }
        }
        if (bytes > AppearancePresetWire.MaxBytes) return;
        reading = value; at = message.Timestamp;
    }
    static bool ValidData(string? data)
    {
        if (data == null) return true;
        if (data.Length > 87384) return false;
        try { return Convert.FromBase64String(data).Length <= 65536; } catch (FormatException) { return false; }
    }
    internal CharacterPresets? Snapshot() => owner == null || reading == null && current == null ? null : new(owner.Name, owner.Server,
        owner.PersistentId.ToString(System.Globalization.CultureInfo.InvariantCulture), Math.Max(equipment?.Timestamp ?? -1, Math.Max(at, Math.Max(currentAt, wingAt))), reading?.Presets.ToArray() ?? []) {
            Current = current, PresetsCaptured = reading != null,
            Equipment = equipment?.Items.Where(i => i.Display && (i.ItemId != 0 || i.SkinId != 0)).Select(i => new AppearanceEquipment(i.ItemId, i.SkinId, i.Dyes.ToArray())).ToArray(),
            EquipmentComplete = equipment?.Complete == true,
            CurrentAt = currentAt, WingsAt = wingAt, EquipmentAt = equipment?.Timestamp ?? -1, PresetsAt = at
        };
}
