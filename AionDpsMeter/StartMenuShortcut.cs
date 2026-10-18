using System.IO;
using System.Runtime.InteropServices;

namespace AionDpsMeter.Local;

internal static class StartMenuShortcut
{
    internal const string FileName = "Wingbeat.lnk";
    const string Description = "Wingbeat - Aion 2 DPS Meter";

    internal static bool Eligible(bool isolated, string? executable) => !isolated &&
        string.Equals(Path.GetFileName(executable), "Wingbeat.exe", StringComparison.OrdinalIgnoreCase);

    internal static void EnsureForCurrentProcess(bool isolated)
    {
        if (!Eligible(isolated, Environment.ProcessPath)) return;
        try {
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            if (string.IsNullOrWhiteSpace(programs)) throw new IOException("The current user's Start menu folder is unavailable.");
            Ensure(Environment.ProcessPath!, programs);
        }
        catch (Exception ex) {
            // Shortcut registration is optional; locked-down Windows profiles
            // must still be able to open the meter normally.
            LocalLog.Write("Start menu shortcut could not be updated: " + ex.Message);
        }
    }

    // A per-user Programs shortcut needs no elevation. Reusing one filename
    // repairs its target when a new release is extracted to a different folder.
    internal static bool Ensure(string executable, string programs)
    {
        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable)) throw new FileNotFoundException("Wingbeat executable is missing.", executable);
        Directory.CreateDirectory(programs);
        string path = Path.Combine(programs, FileName);
        string workingDirectory = Path.GetDirectoryName(executable)!;
        string icon = executable + ",0";
        object? shell = null, shortcut = null;
        try {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!);
            shortcut = ((dynamic)shell!).CreateShortcut(path);
            dynamic link = shortcut;
            if (File.Exists(path) && string.Equals((string)link.TargetPath, executable, StringComparison.OrdinalIgnoreCase) &&
                string.Equals((string)link.WorkingDirectory, workingDirectory, StringComparison.OrdinalIgnoreCase) &&
                string.Equals((string)link.IconLocation, icon, StringComparison.OrdinalIgnoreCase) &&
                (string)link.Arguments == "" && (string)link.Description == Description)
                return false;
            link.TargetPath = executable;
            link.WorkingDirectory = workingDirectory;
            link.IconLocation = icon;
            link.Description = Description;
            link.Arguments = "";
            link.WindowStyle = 1;
            link.Save();
            return true;
        }
        finally {
            if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
            if (shell != null) Marshal.FinalReleaseComObject(shell);
        }
    }
}
