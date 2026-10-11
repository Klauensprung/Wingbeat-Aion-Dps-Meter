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
    readonly string folder;
    public static string LocalPath => Path.Combine(AppContext.BaseDirectory, "local-data", "skill-icons.json");
    public int Count => files.Count;
    public string? LoadError { get; }
    public static ImageSource Placeholder { get; } = CreatePlaceholder();

    public SkillIcons(string? manifest = null)
    {
        manifest = Path.GetFullPath(manifest ?? LocalPath);
        folder = Path.Combine(Path.GetDirectoryName(manifest)!, "skill-icons");
        try
        {
            var map = SkillNames.Read(manifest, "Skill icons");
            if (map.Count > 10000 || map.Values.Any(file => file.Length > 160 || !file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                file[..^4].Length == 0 || file[..^4].Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-'))))
                throw new InvalidDataException("Skill icons must reference PNG filenames in the local skill-icons folder.");
            files = map.ToFrozenDictionary();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            files = FrozenDictionary<int, string>.Empty; LoadError = ex.Message;
        }
    }

    public ImageSource? Find(int skill)
    {
        // The manifest explicitly lists icon families from game data. Low-range NPC
        // skills need an exact entry; arbitrary neighbouring IDs must not share icons.
        if (!files.TryGetValue(skill, out var file) &&
            !(skill >= 10000000 && files.TryGetValue(skill / 10000 * 10000, out file))) return null;
        if (images.TryGetValue(file, out var cached)) return cached;
        ImageSource? result = null;
        try
        {
            using var stream = File.OpenRead(Path.Combine(folder, file));
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
