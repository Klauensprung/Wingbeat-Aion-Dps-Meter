using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AionDpsMeter.Local;

internal sealed class WindowsNotifications : IDisposable
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct IconData
    {
        internal uint Size;
        internal nint Window;
        internal uint Id, Flags, Callback;
        internal nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Tip;
        internal uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Text;
        internal uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string Title;
        internal uint InfoFlags;
        internal Guid Guid;
        internal nint BalloonIcon;
    }
    internal delegate bool Send(uint command, ref IconData data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool Notify(uint command, ref IconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern nint LoadIcon(nint instance, nint name);
    const uint CallbackMessage = 0x8000 + 137;
    readonly Send send;
    readonly Action? activated, displayed;
    readonly HwndSource? source;
    readonly uint taskbarCreated;
    IconData data;
    bool added, disposed;
    internal WindowsNotifications(Window owner, Action displayed) : this(new WindowInteropHelper(owner).Handle, Notify,
        () => { owner.Show(); if (owner.WindowState == WindowState.Minimized) owner.WindowState = WindowState.Normal; owner.Activate(); }, displayed)
    {
        source = HwndSource.FromHwnd(data.Window); source?.AddHook(Hook);
        taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        data.Icon = LoadIcon(0, 32512); // Shared system icon; do not destroy it.
    }
    internal WindowsNotifications(nint window, Send send, Action? activated = null, Action? displayed = null)
    {
        this.send = send; this.activated = activated; this.displayed = displayed;
        data = new() { Size = (uint)Marshal.SizeOf<IconData>(), Window = window, Id = 1, Callback = CallbackMessage,
            Tip = "Wingbeat event reminders", Text = "", Title = "", Version = 4 };
    }
    internal static string Truncate(string text, int limit)
    {
        text = text.Replace('\0', ' ');
        if (text.Length <= limit) return text;
        if (char.IsHighSurrogate(text[limit - 1])) limit--;
        return text[..limit];
    }
    internal bool Show(string title, string text)
    {
        if (disposed || data.Window == 0) return false;
        if (!added) {
            data.Flags = 1 | 2 | 4 | 128; // callback, icon, tooltip, version-4 tooltip
            if (!send(0, ref data)) return false;
            added = true;
            if (!send(4, ref data)) { Dispose(); return false; }
        }
        data.Flags = 16 | 64; // info + realtime: never queue a late event alert
        data.Title = Truncate(title, 63); data.Text = Truncate(text, 255);
        data.InfoFlags = 1 | 16 | 128; // info, no OS sound, respect quiet time
        return send(1, ref data);
    }
    nint Hook(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (taskbarCreated != 0 && message == taskbarCreated) added = false;
        if (message != CallbackMessage) return 0;
        uint value = unchecked((uint)lParam.ToInt64());
        if ((value >> 16) != data.Id) return 0;
        int action = (int)(value & 0xffff);
        if (action is 0x400 or 0x401 or 0x405) activated?.Invoke();
        if (action == 0x402) displayed?.Invoke();
        handled = true; return 0;
    }
    public void Dispose()
    {
        if (disposed) return;
        if (added) send(2, ref data);
        added = false; disposed = true; source?.RemoveHook(Hook);
    }
}
