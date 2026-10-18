using System.IO;
using System.Runtime.InteropServices;

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
        string original = Path.Combine(temp, "Release one", "Wingbeat.exe");
        string updated = Path.Combine(temp, "Release two", "Wingbeat.exe");
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
        object? shell = null, loaded = null;
        try {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true)!);
            loaded = ((dynamic)shell!).CreateShortcut(shortcut);
            dynamic link = loaded;
            check(string.Equals((string)link.TargetPath, updated, StringComparison.OrdinalIgnoreCase) &&
                string.Equals((string)link.WorkingDirectory, Path.GetDirectoryName(updated), StringComparison.OrdinalIgnoreCase) &&
                string.Equals((string)link.IconLocation, updated + ",0", StringComparison.OrdinalIgnoreCase) &&
                (string)link.Arguments == "" && ((string)link.Description).Contains("Aion 2"),
                "Windows reads the correct executable, working folder, icon and normal launch arguments");
        }
        finally {
            if (loaded != null) Marshal.FinalReleaseComObject(loaded);
            if (shell != null) Marshal.FinalReleaseComObject(shell);
        }
    }
}
