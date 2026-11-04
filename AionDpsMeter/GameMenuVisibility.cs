using System.Runtime.InteropServices;

namespace AionDpsMeter.Local;

// Cursor visibility is a UI heuristic, not a decoded game-menu event. It is
// armed only after observing normal cursor-hidden gameplay in this session.
internal sealed class GameMenuVisibility
{
    long? shownAt, hiddenAt;
    long clickedUntil;
    internal bool Armed { get; private set; }
    internal bool Hidden { get; private set; }
    internal void Reset(){Armed=false;ClearTiming();}
    void ClearTiming(){shownAt=hiddenAt=null;clickedUntil=0;Hidden=false;}
    internal bool Observe(long now,bool game,bool valid,bool cursor,bool alt,bool mouseDown,bool classic=false,bool overOverlay=false)
    {
        if(classic){Reset();return false;}
        if(!game||!valid||alt||overOverlay){ClearTiming();return false;}
        if(mouseDown)clickedUntil=now+900;
        if(!cursor){
            Armed=true;shownAt=null;hiddenAt??=now;
            if(now-hiddenAt>=400 && now>=clickedUntil)Hidden=false;
        }else{
            hiddenAt=null;
            if(Armed){shownAt??=now;if(now-shownAt>=2000)Hidden=true;}
        }
        return Hidden;
    }
}

internal static class GameCursorState
{
    [StructLayout(LayoutKind.Sequential)] struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] struct CursorInfo { public int Size,Flags; public nint Cursor; public Point Position; }
    [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    internal static (bool Valid,bool Visible,bool Alt,bool MouseDown) Read()
    {
        var info=new CursorInfo {Size=Marshal.SizeOf<CursorInfo>()};
        bool valid=GetCursorInfo(ref info);
        bool Down(int key)=>(GetAsyncKeyState(key)&0x8000)!=0;
        return(valid,valid&&(info.Flags&1)!=0&&info.Cursor!=0,Down(0x12)||Down(0xA4)||Down(0xA5),Down(1)||Down(2));
    }
}
