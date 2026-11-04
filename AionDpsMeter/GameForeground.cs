using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AionDpsMeter.Local;

internal static class GameForeground
{
    [DllImport("user32.dll")]
    static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    internal static bool IsAllowedProcess(int processId, int ownProcessId, string? processName) =>
        processId > 0 && (processId == ownProcessId ||
        string.Equals(processName, "AION2", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(processName, "AION2-Win64-Shipping", StringComparison.OrdinalIgnoreCase));
    internal static bool AllowsOverlays() => CheckForeground(true);
    internal static bool IsGameFocused() => CheckForeground(false);
    static bool CheckForeground(bool includeOwn)
    {
        var window = GetForegroundWindow();
        if (window == 0 || GetWindowThreadProcessId(window, out uint id) == 0 || id == 0 || id > int.MaxValue) return false;
        if (id == Environment.ProcessId) return includeOwn;
        try {
            using var process = Process.GetProcessById((int)id);
            return IsAllowedProcess((int)id, Environment.ProcessId, process.ProcessName);
        } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { return false; }
    }
}
