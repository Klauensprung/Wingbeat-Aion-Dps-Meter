using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal sealed record SavedAccountCharacter(int Server, LoginCharacter Character, long Timestamp)
{
    public CharacterPresets? LiveAppearance { get; init; }
    public string? CapturedJob { get; init; }
}
public sealed class AccountCharacterCache
{
    readonly Dictionary<(int Server, long Id), SavedAccountCharacter> entries = new();
    readonly Dictionary<(int Server, long Id), int> sizes = new();
    int totalBytes;
    const int ByteLimit = 2 * 1024 * 1024 - 1024;
    readonly object gate = new(), saveGate = new();
    readonly string? path;
    long revision, savedRevision;
    public AccountCharacterCache(string? path = null)
    {
        this.path = path;
        if (path == null || !File.Exists(path)) return;
        try {
            if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException();
            var rows = JsonSerializer.Deserialize<SavedAccountCharacter[]>(File.ReadAllText(path)) ?? throw new InvalidDataException();
            if (rows.Length > 512 || rows.Any(r => r == null || !Valid(r)) || rows.Select(r => (r.Server, r.Character.Id)).Distinct().Count() != rows.Length)
                throw new InvalidDataException();
            foreach (var row in rows) Put(row);
        } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException) {
            LocalLog.Write("Saved character roster could not be loaded; original file retained.");
        }
    }
    static bool Valid(SavedAccountCharacter row) => row.Server > 0 && row.Timestamp is >= 0 and <= 253402300799999 && row.Character is { } c &&
        c.Id > 0 && c.Level >= 0 && c.Template >= 0 && c.CombatPower >= 0 && !(c.GearScore < 0) && !string.IsNullOrWhiteSpace(c.Name) && c.Name.Length <= 128 && c.Guild != null && c.Guild.Length <= 256 && ValidAppearance(c.Appearance) && (row.CapturedJob == null || row.CapturedJob.Length <= 64) && (row.LiveAppearance == null || SavedAppearance.Valid(row.LiveAppearance, row.Server, c.Id));
    static bool ValidAppearance(LoginAppearance? appearance)
    {
        if (appearance == null) return true;
        if (appearance.Race is not ("light" or "dark") || appearance.Gender is not ("male" or "female") || appearance.Wing < 0 ||
            appearance.Equipment == null || appearance.Equipment.Length > 128 || appearance.Equipment.Any(e => e == null || e.Item < 0 || e.Skin < 0 ||
                e.Dyes == null || e.Dyes.Length > 32 || e.Dyes.Any(d => d == null || d.Slot is < 1 or > 256))) return false;
        if (appearance.Data == null) return true;
        if (appearance.Data.Length > 87384) return false;
        try { var data = Convert.FromBase64String(appearance.Data); return data.Length is > 0 and <= 65536 && data[0] == 12; }
        catch (FormatException) { return false; }
    }
    static SavedAccountCharacter Copy(SavedAccountCharacter row) => row with {
        LiveAppearance = row.LiveAppearance == null ? null : SavedAppearance.Copy(row.LiveAppearance),
        Character = row.Character.Appearance is not { } a ? row.Character : row.Character with { Appearance = a with { Equipment = a.Equipment.Select(e => e with { Dyes = e.Dyes.ToArray() }).ToArray() } }
    };
    void Put(SavedAccountCharacter row)
    {
        var key = (row.Server, row.Character.Id);
        int size = JsonSerializer.SerializeToUtf8Bytes(row).Length;
        if (size > ByteLimit) return; // An oversized entry must not evict the entire library.
        totalBytes -= sizes.GetValueOrDefault(key); totalBytes += size;
        entries[key] = Copy(row); sizes[key] = size;
        while (entries.Count > 512 || totalBytes > ByteLimit) {
            var oldest = entries.MinBy(e => Math.Max(e.Value.Timestamp, e.Value.LiveAppearance?.Timestamp ?? -1)).Key;
            totalBytes -= sizes[oldest]; sizes.Remove(oldest); entries.Remove(oldest);
        }
    }
    internal void Apply(LoginReading login, long timestamp)
    {
        if (login.Result != 0 || login.Server <= 0) return;
        lock (gate) foreach (var character in login.Characters) {
            var row = new SavedAccountCharacter(login.Server, character, timestamp);
            if (!Valid(row)) continue;
            var key = (login.Server, character.Id);
            if (entries.TryGetValue(key, out var previous) && (previous.Timestamp > timestamp || previous == row)) continue;
            row = row with { LiveAppearance = previous?.LiveAppearance };
            Put(row); revision++;
        }
    }
    internal void ApplyAppearance(Actor owner, CharacterPresets? appearance)
    {
        if (owner.PersistentId <= 0 || owner.Server <= 0 || appearance == null || !SavedAppearance.Valid(appearance, owner.Server, owner.PersistentId)) return;
        lock (gate) {
            var key = (owner.Server, owner.PersistentId);
            entries.TryGetValue(key, out var old);
            var before = old?.LiveAppearance;
            if (before != null && appearance.CurrentAt <= before.CurrentAt && appearance.WingsAt <= before.WingsAt &&
                appearance.EquipmentAt <= before.EquipmentAt && appearance.PresetsAt <= before.PresetsAt && before.Name == appearance.Name) return;
            var merged = SavedAppearance.Merge(before, appearance);
            var row = old ?? new SavedAccountCharacter(owner.Server, new(owner.PersistentId, owner.Name, 0, owner.Metadata?.Level ?? 0,
                owner.Metadata?.Guild ?? "", owner.Cp) { GearScore = owner.Metadata?.GearScore }, appearance.Timestamp) { CapturedJob = owner.Job };
            row = row with { LiveAppearance = merged, Character = row.Character with { Name = appearance.Timestamp >= row.Timestamp ? owner.Name : row.Character.Name } };
            if (!Valid(row)) return;
            Put(row); revision++;
        }
    }
    internal SavedAccountCharacter[] Snapshot() { lock (gate) return entries.Values.OrderBy(r => r.Server).ThenBy(r => r.Character.Name).Select(Copy).ToArray(); }
    internal void Save()
    {
        if (path == null) return;
        lock (saveGate) {
            SavedAccountCharacter[] rows; long version;
            lock (gate) { if (revision == savedRevision) return; rows = entries.Values.ToArray(); version = revision; }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temporary = path + ".tmp"; File.WriteAllText(temporary, JsonSerializer.Serialize(rows)); File.Move(temporary, path, true);
            lock (gate) savedRevision = version;
        }
    }
}
