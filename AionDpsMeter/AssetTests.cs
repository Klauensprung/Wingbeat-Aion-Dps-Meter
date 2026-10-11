using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AionDpsMeter.Local;

internal static class AssetTests
{
    public static void Run(Action<bool, string> check, string temp)
    {
        var freshNpc = new NpcNames(Path.Combine(temp, "no-npc-data", "npc-names.json"), includeBundled: true);
        check(freshNpc.Count == 12587 && freshNpc.LoadError == null && freshNpc.GetName(2000001) == "Punching Bag",
            "Fresh published installs resolve bundled NPC template names offline");
        var npcOverride = Path.Combine(temp, "npc-overrides.json");
        File.WriteAllText(npcOverride, """{"2000001":"Custom dummy"}""");
        var customNpcs = new NpcNames(npcOverride, includeBundled: true);
        check(customNpcs.GetName(2000001) == "Custom dummy" && customNpcs.Count == freshNpc.Count && customNpcs.GetName(2000002) == freshNpc.GetName(2000002),
            "Local NPC overrides retain the rest of the bundled catalog");
        File.WriteAllText(npcOverride, "{");
        var fallbackNpcs = new NpcNames(npcOverride, includeBundled: true);
        check(fallbackNpcs.GetName(2000001) == "Punching Bag" && fallbackNpcs.LoadError != null,
            "A damaged local NPC catalog preserves bundled target names");
        var oldNpcFight = new Fight();
        oldNpcFight.Actors[7] = new(7, "NPC 2000001", false, false, 0, "Unknown", 0) { NpcId = 2000001 };
        check(oldNpcFight.Name(7) == "Punching Bag" && oldNpcFight.Name(2000001) == "Entity 2000001",
            "Old captured NPC IDs resolve without confusing template IDs with temporary combat entity IDs");

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
        var bundled = new SkillIcons(Path.Combine(root, "fresh", "skill-icons.json"), includeBundled: true);
        var bundledMap = SkillNames.Read(SkillIcons.BundledPath);
        check(bundled.Count == 670 && bundled.LoadError == null && bundledMap.Keys.All(id => bundled.Find(id) is BitmapImage { IsFrozen: true }),
            "Fresh published installs load all 670 skill families and all bundled PNGs offline");
        var layered = new SkillIcons(manifest, includeBundled: true);
        check(layered.Find(17010001) is BitmapImage { IsFrozen: true } &&
            layered.Find(17100001) != null && layered.Find(17030001) != null && layered.Find(17040001) != null,
            "Local icon overrides preserve defaults and missing or broken PNGs fall back to bundled images");
        File.WriteAllText(manifest, "{\"17010000\":\"../outside.png\"}");
        var invalidOverride = new SkillIcons(manifest, includeBundled: true);
        check(invalidOverride.Count == bundled.Count && invalidOverride.LoadError != null && invalidOverride.Find(17010001) != null,
            "Invalid override manifests cannot hide bundled icons or escape their folder");
        var unsafeIcons = new SkillIcons(manifest);
        check(unsafeIcons.Count == 0 && unsafeIcons.LoadError != null, "Icon manifest rejects paths outside the local icon folder");
        File.WriteAllText(manifest, "{\"17010000\":\"https://example.invalid/icon.png\"}");
        check(new SkillIcons(manifest).Count == 0, "Icon cells never fetch remote image URLs");
        check(new SkillIcons(Path.Combine(root, "absent.json")).Count == 0 && SkillIcons.Placeholder.IsFrozen,
            "Meter remains usable offline without an icon catalog");
    }
}
