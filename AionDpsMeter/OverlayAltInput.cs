using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AionDpsMeter.Local;

internal sealed class AltHeldState
{
    bool left,right;
    internal bool Down => left||right;
    internal bool ApplyHook(int key,int flags) => (flags&0x12)==0 && Apply(key,(flags&1)!=0,(flags&0x80)!=0);
    internal bool Apply(int key,bool extended,bool released)
    {
        bool before=Down;
        if(key==0xA5 || key==0x12&&extended)right=!released;
        else if(key is 0xA4 or 0x12)left=!released;
        return before!=Down;
    }
}

// Observe physical Alt transitions even when the game suppresses legacy key
// state. Other keys are ignored; no characters or keyboard history are retained.
internal sealed class OverlayAltInput : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct Device { public ushort Page,Usage; public uint Flags; public nint Target; }
    [StructLayout(LayoutKind.Sequential)] struct Header { public uint Type,Size; public nint Device,WParam; }
    [DllImport("user32.dll",SetLastError=true)] static extern bool RegisterRawInputDevices(Device[] devices,uint count,uint size);
    [DllImport("user32.dll")] static extern uint GetRawInputData(nint input,uint command,nint data,ref uint size,uint headerSize);
    delegate nint KeyboardHook(int code,nint wParam,nint lParam);
    [DllImport("user32.dll",SetLastError=true)] static extern nint SetWindowsHookExW(int id,KeyboardHook callback,nint module,uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] static extern nint CallNextHookEx(nint hook,int code,nint wParam,nint lParam);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern nint GetModuleHandleW(string? name);
    readonly HwndSource source;
    readonly AltHeldState state=new();
    readonly AltHeldState physicalState=new();
    readonly KeyboardHook keyboardHook;
    readonly nint hook;
    readonly Action changed;
    readonly nint buffer=Marshal.AllocHGlobal(128);
    bool disposed,registered;
    internal bool Down=>state.Down||physicalState.Down;
    internal OverlayAltInput(Window owner,Action changed)
    {
        this.changed=changed;
        source=HwndSource.FromHwnd(new WindowInteropHelper(owner).Handle)!;
        source.AddHook(Message);
        registered=RegisterRawInputDevices([new(){Page=1,Usage=6,Flags=0x100,Target=source.Handle}],1,(uint)Marshal.SizeOf<Device>());
        if(!registered)LocalLog.Write("Alt override raw input registration failed: "+Marshal.GetLastWin32Error());
        keyboardHook=ObserveKeyboard;
        hook=SetWindowsHookExW(13,keyboardHook,GetModuleHandleW(null),0);
        LocalLog.Write($"Alt override listeners: raw={registered}; physical={hook!=0}"+(hook==0?$"; error={Marshal.GetLastWin32Error()}":""));
    }
    nint ObserveKeyboard(int code,nint wParam,nint lParam)
    {
        if(code>=0&&!disposed&&Marshal.ReadInt32(lParam) is 0x12 or 0xA4 or 0xA5){
            int key=Marshal.ReadInt32(lParam),flags=Marshal.ReadInt32(lParam,8);
            source.Dispatcher.BeginInvoke(new Action(()=>OverlayInputTrace.Write($"hook alt key={key:X} flags={flags:X}")));
        }
        // Keep the hook fast and never consume input. Synthetic Alt-up events
        // must not clear a key that the player is physically still holding.
        if(code>=0&&!disposed&&physicalState.ApplyHook(Marshal.ReadInt32(lParam),Marshal.ReadInt32(lParam,8)))
            source.Dispatcher.BeginInvoke(new Action(()=>{if(!disposed)changed();}));
        return CallNextHookEx(hook,code,wParam,lParam);
    }
    nint Message(nint hwnd,int message,nint wParam,nint lParam,ref bool handled)
    {
        if(message!=0x00FF||disposed)return 0;
        uint size=128;int headerSize=Marshal.SizeOf<Header>();
        uint read=GetRawInputData(lParam,0x10000003,buffer,ref size,(uint)headerSize);
        if(read==uint.MaxValue||read<headerSize+16||Marshal.ReadInt32(buffer)!=1)return 0;
        int key=(ushort)Marshal.ReadInt16(buffer,headerSize+6);
        if(key is not (0x12 or 0xA4 or 0xA5))return 0;
        int flags=(ushort)Marshal.ReadInt16(buffer,headerSize+2);
        OverlayInputTrace.Write($"raw alt key={key:X} flags={flags:X}");
        if(state.Apply(key,(flags&2)!=0,(flags&1)!=0))changed();
        // Leave WM_INPUT to the default window procedure for native cleanup.
        return 0;
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        if(hook!=0)UnhookWindowsHookEx(hook);
        if(registered)RegisterRawInputDevices([new(){Page=1,Usage=6,Flags=1,Target=0}],1,(uint)Marshal.SizeOf<Device>());
        if(!source.IsDisposed)source.RemoveHook(Message);
        Marshal.FreeHGlobal(buffer);
    }
}

internal static class OverlayInputTrace
{
    static readonly long until=Environment.GetEnvironmentVariable("WINGBEAT_ALT_TRACE")=="1"?Environment.TickCount64+180000:0;
    static int remaining=400;
    internal static void Write(string message)
    {
        if(Environment.TickCount64<until&&remaining-->0)LocalLog.Write("Overlay input trace: "+message);
    }
}
