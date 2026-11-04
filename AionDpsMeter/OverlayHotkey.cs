using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace AionDpsMeter.Local;

internal sealed class OverlayHotkey : IDisposable
{
    [DllImport("user32.dll",SetLastError=true)] static extern bool RegisterHotKey(nint hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(nint hwnd,int id);
    const int Id=0x5742;
    readonly HwndSource source;
    readonly Action toggle;
    internal OverlayHotkey(Window owner,Action toggle)
    {
        this.toggle=toggle;
        source=HwndSource.FromHwnd(new WindowInteropHelper(owner).Handle)!;
        source.AddHook(Message);
    }
    internal string? Register(int key,int modifiers)
    {
        UnregisterHotKey(source.Handle,Id);
        if(key==0)return null;
        if(key is < 1 or > 254 || (modifiers & ~15)!=0 || modifiers==0)return "Choose a key with Ctrl, Alt, Shift, or Windows.";
        return RegisterHotKey(source.Handle,Id,(uint)modifiers|0x4000,(uint)key) ? null : "Hotkey unavailable. Choose another combination; another app may be using it.";
    }
    nint Message(nint hwnd,int message,nint wParam,nint lParam,ref bool handled)
    {
        if(message==0x0312 && wParam==Id){handled=true;toggle();}return 0;
    }
    internal static string Label(int key,int modifiers) => key==0 ? "Disabled" :
        ((modifiers&2)!=0?"Ctrl + ":"")+((modifiers&1)!=0?"Alt + ":"")+((modifiers&4)!=0?"Shift + ":"")+((modifiers&8)!=0?"Win + ":"")+KeyInterop.KeyFromVirtualKey(key);
    public void Dispose(){if(source.IsDisposed)return;UnregisterHotKey(source.Handle,Id);source.RemoveHook(Message);}
}
