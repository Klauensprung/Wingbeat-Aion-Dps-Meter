using System.Collections.Frozen;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AionDpsMeter.Local;

// UI-thread cache: each local PNG (including a missing/broken file) is opened at most once.
// No URLs, network requests, file locks or mutable bitmaps in a cell binding.
public sealed class SkillIcons : IValueConverter
{
    readonly FrozenDictionary<int, string> files;
    readonly Dictionary<string, ImageSource?> images = new(StringComparer.OrdinalIgnoreCase);
    readonly FrozenDictionary<int, string> bundled;
    public static string BundledPath => Path.Combine(AppContext.BaseDirectory, "Assets", "skill-icons.json");
    public static string LocalPath => Path.Combine(AppContext.BaseDirectory, "local-data", "skill-icons.json");
    public int Count => files.Count;
    public string? LoadError { get; }
    public static ImageSource Placeholder { get; } = CreatePlaceholder();

    public SkillIcons(string? manifest = null) : this(manifest ?? LocalPath, manifest == null) { }

    internal SkillIcons(string manifest, bool includeBundled)
    {
        var errors = new List<string>();
        bundled = includeBundled ? LoadManifest(BundledPath, optional: false) : FrozenDictionary<int, string>.Empty;
        var combined = bundled.ToDictionary();
        foreach (var pair in LoadManifest(manifest, optional: includeBundled)) combined[pair.Key] = pair.Value;
        files = combined.ToFrozenDictionary();
        LoadError = errors.Count == 0 ? null : string.Join("; ", errors);

        FrozenDictionary<int, string> LoadManifest(string path, bool optional)
        {
            try
            {
                path = Path.GetFullPath(path);
                var map = SkillNames.Read(path, "Skill icons");
                if (map.Count > 10000 || map.Values.Any(file => file.Length > 160 || !file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                    file[..^4].Length == 0 || file[..^4].Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-'))))
                    throw new InvalidDataException("Skill icons must reference PNG filenames in the skill-icons folder.");
                var folder = Path.Combine(Path.GetDirectoryName(path)!, "skill-icons");
                return map.ToFrozenDictionary(pair => pair.Key, pair => Path.Combine(folder, pair.Value));
            }
            catch (Exception ex) when (optional && (ex is FileNotFoundException or DirectoryNotFoundException)) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                errors.Add(ex.Message);
            }
            return FrozenDictionary<int, string>.Empty;
        }
    }

    public ImageSource? Find(int skill) => Find(files, skill) ?? Find(bundled, skill);

    ImageSource? Find(FrozenDictionary<int, string> map, int skill)
    {
        // The manifest explicitly lists icon families from game data. Low-range NPC
        // skills need an exact entry; arbitrary neighbouring IDs must not share icons.
        if (!map.TryGetValue(skill, out var file) &&
            !(skill >= 10000000 && map.TryGetValue(skill / 10000 * 10000, out file))) return null;
        if (images.TryGetValue(file, out var cached)) return cached;
        ImageSource? result = null;
        try
        {
            using var stream = File.OpenRead(file);
            if (stream.Length is < 33 or > 262144) throw new InvalidDataException("Unexpected icon file size.");
            Span<byte> header = stackalloc byte[24]; stream.ReadExactly(header);
            if (!header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) || !header[12..16].SequenceEqual("IHDR"u8))
                throw new InvalidDataException("Expected PNG icon.");
            uint width = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header[16..20]);
            uint height = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header[20..24]);
            if (width is < 1 or > 512 || height is < 1 or > 512) throw new InvalidDataException("Unexpected icon dimensions.");
            stream.Position = 0;
            var bitmap = new BitmapImage(); bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 48; bitmap.StreamSource = stream;
            bitmap.EndInit(); bitmap.Freeze(); result = bitmap;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or
            InvalidOperationException or System.Runtime.InteropServices.COMException)
        { /* A damaged/missing optional icon must not interrupt capture or table rendering. */ }
        images[file] = result;
        return result;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is int id ? Find(id) : null) ?? Placeholder;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;

    static ImageSource CreatePlaceholder()
    {
        var group = new DrawingGroup();
        using (var drawing = group.Open())
        {
            drawing.DrawRoundedRectangle(UiTheme.Brush("#253145"), null, new Rect(0, 0, 24, 24), 4, 4);
            var diamond = Geometry.Parse("M 12,6 L 18,12 12,18 6,12 Z");
            drawing.DrawGeometry(null, new Pen(UiTheme.Brush("#77879C"), 1.5), diamond);
        }
        var image = new DrawingImage(group); image.Freeze(); return image;
    }
}
