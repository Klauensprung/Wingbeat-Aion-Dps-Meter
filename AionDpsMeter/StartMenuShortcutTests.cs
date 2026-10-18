using System.IO;

namespace AionDpsMeter.Local;

internal static class StartMenuShortcutTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        check(StartMenuShortcut.Eligible(false, @"C:\Apps\Wingbeat.exe") &&
            !StartMenuShortcut.Eligible(true, @"C:\Apps\Wingbeat.exe") &&
            !StartMenuShortcut.Eligible(false, @"C:\dotnet\dotnet.exe") &&
            !StartMenuShortcut.Eligible(false, null),
            "Only normal Wingbeat launches register Start menu shortcuts; isolated tests and dotnet hosts do not");
        string programs = Path.Combine(temp, "Start menu programs");
        string original = Path.Combine(temp, "Release one ♥", "Wingbeat.exe");
        string updated = Path.Combine(temp, "Release two ♥", "Wingbeat.exe");
        foreach (var path in new[] { original, updated }) {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Wingbeat.exe"), path);
        }
        string shortcut = Path.Combine(programs, StartMenuShortcut.FileName);
        check(StartMenuShortcut.Ensure(original, programs) && File.Exists(shortcut), "First launch creates a per-user Wingbeat Start menu shortcut");
        DateTime written = File.GetLastWriteTimeUtc(shortcut);
        check(!StartMenuShortcut.Ensure(original, programs) && File.GetLastWriteTimeUtc(shortcut) == written,
            "Repeated launches leave a correct shortcut untouched");
        check(StartMenuShortcut.Ensure(updated, programs) && Directory.GetFiles(programs, "*.lnk").Length == 1,
            "Launching an updated or moved release repairs the same shortcut without duplicates");
        var link = StartMenuShortcut.Read(shortcut);
        check(string.Equals(link.Target, updated, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(link.WorkingDirectory, Path.GetDirectoryName(updated), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(link.Icon, updated, StringComparison.OrdinalIgnoreCase) && link.IconIndex == 0 &&
            link.Arguments == "" && link.Description.Contains("Aion 2"),
            "Windows reads Unicode executable paths, working folders, icons and normal launch arguments");
    }
}
