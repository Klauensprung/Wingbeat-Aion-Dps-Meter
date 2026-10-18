using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

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
        object shortcut = CreateLink();
        try {
            var link = (IShellLinkW)shortcut;
            var file = (IPersistFile)shortcut;
            if (File.Exists(path)) {
                try {
                    file.Load(path, 0);
                    var current = Read(link);
                    if (string.Equals(current.Target, executable, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(current.WorkingDirectory, workingDirectory, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(current.Icon, executable, StringComparison.OrdinalIgnoreCase) && current.IconIndex == 0 &&
                        current.Arguments == "" && current.Description == Description) return false;
                } catch (COMException) { /* Replace an unreadable shortcut with a working one. */ }
            }
            link.SetPath(executable);
            link.SetWorkingDirectory(workingDirectory);
            link.SetIconLocation(executable, 0);
            link.SetDescription(Description);
            link.SetArguments("");
            link.SetShowCmd(1);
            file.Save(path, true);
            return true;
        }
        finally {
            Marshal.FinalReleaseComObject(shortcut);
        }
    }

    // WScript.Shell rejects some Unicode target paths, including the release
    // folder's heart character. Use the explicit wide-character shell interface.
    static object CreateLink() => Activator.CreateInstance(Type.GetTypeFromCLSID(new("00021401-0000-0000-C000-000000000046"), true)!)!;
    internal sealed record Details(string Target, string WorkingDirectory, string Icon, int IconIndex, string Arguments, string Description);
    internal static Details Read(string path)
    {
        object shortcut = CreateLink();
        try { ((IPersistFile)shortcut).Load(path, 0); return Read((IShellLinkW)shortcut); }
        finally { Marshal.FinalReleaseComObject(shortcut); }
    }
    static Details Read(IShellLinkW link)
    {
        var target = new StringBuilder(32768); var directory = new StringBuilder(32768); var icon = new StringBuilder(32768);
        var arguments = new StringBuilder(32768); var description = new StringBuilder(1024);
        link.GetPath(target, target.Capacity, IntPtr.Zero, 4);
        link.GetWorkingDirectory(directory, directory.Capacity); link.GetIconLocation(icon, icon.Capacity, out int index);
        link.GetArguments(arguments, arguments.Capacity); link.GetDescription(description, description.Capacity);
        return new(target.ToString(), directory.ToString(), icon.ToString(), index, arguments.ToString(), description.ToString());
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, IntPtr findData, uint flags);
        void GetIDList(out IntPtr list);
        void SetIDList(IntPtr list);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string path);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
