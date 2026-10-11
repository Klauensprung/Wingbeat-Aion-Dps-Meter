using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AionDpsMeter.Local;

internal static class AssetTests
{
    public static void Run(Action<bool, string> check, string temp)
    {
        var root = Path.Combine(temp, "asset-tests"); var folder = Path.Combine(root, "skill-icons");
        Directory.CreateDirectory(folder);
        string manifest = Path.Combine(root, "skill-icons.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new Dictionary<int, string> {
            [17010000] = "test.png", [17020000] = "test.png", [1500000] = "test.png",
            [17030000] = "missing.png", [17040000] = "broken.png"
        }));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 255, 255, 255, 255 }, 8)));
        using (var file = File.Create(Path.Combine(folder, "test.png"))) encoder.Save(file);
        File.WriteAllBytes(Path.Combine(folder, "broken.png"), new byte[48]);
        var icons = new SkillIcons(manifest); var original = icons.Find(17010000);
        check(original is BitmapImage { IsFrozen: true } && ReferenceEquals(original, icons.Find(17019999)) &&
            ReferenceEquals(original, icons.Find(17020001)), "Skill icon variants share a frozen cached image by filename");
        using (File.Open(Path.Combine(folder, "test.png"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            check(true, "Decoded skill icons release their file handles");
        check(icons.Find(1500000) != null && icons.Find(1500001) == null && icons.Find(77777777) == null,
            "NPC skill icons require exact IDs; unknown skills do not borrow neighbouring images");
        check(icons.Find(17030001) == null && icons.Find(17040001) == null && icons.Find(17040001) == null,
            "Missing and malformed optional PNGs fall back without throwing or repeated disk reads");
        File.WriteAllText(manifest, "{\"17010000\":\"../outside.png\"}");
        var unsafeIcons = new SkillIcons(manifest);
        check(unsafeIcons.Count == 0 && unsafeIcons.LoadError != null, "Icon manifest rejects paths outside the local icon folder");
        File.WriteAllText(manifest, "{\"17010000\":\"https://example.invalid/icon.png\"}");
        check(new SkillIcons(manifest).Count == 0, "Icon cells never fetch remote image URLs");
        check(new SkillIcons(Path.Combine(root, "absent.json")).Count == 0 && SkillIcons.Placeholder.IsFrozen,
            "Meter remains usable offline without an icon catalog");
    }
}
