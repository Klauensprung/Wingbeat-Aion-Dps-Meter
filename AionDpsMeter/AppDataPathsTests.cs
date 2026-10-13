using System.IO;

namespace AionDpsMeter.Local;

internal static class AppDataPathsTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        string source = Path.Combine(temp, "legacy"), target = Path.Combine(temp, "persistent");
        Directory.CreateDirectory(Path.Combine(source, "fights"));
        Directory.CreateDirectory(Path.Combine(source, "skill-icons"));
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(source, "preferences.json"), "old settings");
        File.WriteAllText(Path.Combine(target, "preferences.json"), "current settings");
        File.WriteAllText(Path.Combine(source, "player-profiles.json"), "cached profiles");
        File.WriteAllText(Path.Combine(source, "fights", "example.json"), "fight");
        File.WriteAllBytes(Path.Combine(source, "skill-icons", "example.png"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(source, "unfinished.tmp"), "partial");
        AppDataPaths.Migrate(source, target);
        check(File.ReadAllText(Path.Combine(target, "preferences.json")) == "current settings" &&
            File.ReadAllText(Path.Combine(target, "player-profiles.json")) == "cached profiles",
            "Migration preserves existing settings and imports cached profiles");
        check(File.ReadAllText(Path.Combine(target, "fights", "example.json")) == "fight" &&
            File.ReadAllBytes(Path.Combine(target, "skill-icons", "example.png")).SequenceEqual(new byte[] { 1, 2, 3 }) &&
            File.Exists(Path.Combine(source, "fights", "example.json")) && !File.Exists(Path.Combine(target, "unfinished.tmp")),
            "Migration copies nested fight and icon data, retains originals and skips incomplete writes");
        File.Delete(Path.Combine(target, "fights", "example.json"));
        AppDataPaths.Migrate(source, target);
        check(!File.Exists(Path.Combine(target, "fights", "example.json")), "Completed migration does not resurrect deleted fights on restart");
        AppDataPaths.Migrate(Path.Combine(temp, "missing-install"), target);
        check(File.Exists(Path.Combine(target, "player-profiles.json")), "A new installation without legacy data keeps the shared player cache");
    }
}
