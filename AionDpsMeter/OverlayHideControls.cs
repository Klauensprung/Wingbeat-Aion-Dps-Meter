using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace AionDpsMeter.Local;

internal sealed class OverlaySnooze
{
    internal Panel? Controls { get; set; }
    internal long Until { get; private set; }
    internal void HideForMinute(long now) => Until = now + 60000;
    internal bool Allows(long now) => now >= Until;
}

internal static class OverlayHideControls
{
    [StructLayout(LayoutKind.Sequential)] struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint window,out Rect rect);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint window);
    static readonly ConditionalWeakTable<Window,OverlaySnooze> states = new();
    static readonly DispatcherTimer hoverTimer=new(){Interval=TimeSpan.FromMilliseconds(100)};
    static OverlayHideControls(){hoverTimer.Tick+=(_,_)=>RefreshHover();}
    static void RefreshHover()
    {
        bool visibleCursor=GameCursorState.Read().Visible;
        bool valid=GetCursorPos(out var point);
        bool anyVisible=false;
        foreach(var pair in states){
            if(pair.Value.Controls is not {} controls)continue;
            bool hover=false;
            if(pair.Key.IsVisible){
                anyVisible=true;
                var handle=new WindowInteropHelper(pair.Key).Handle;
                hover=visibleCursor&&valid&&GetWindowRect(handle,out var rect)&&point.X>=rect.Left&&point.X<rect.Right&&point.Y>=rect.Top&&point.Y<rect.Bottom;
                // Keep the launcher available while its scale popup is open.
                hover|=controls.Children.OfType<Button>().Any(b=>b.ContextMenu?.IsOpen==true);
            }
            controls.Visibility=hover?Visibility.Visible:Visibility.Hidden;
        }
        if(anyVisible)hoverTimer.Start();else hoverTimer.Stop();
    }
    internal static bool PointerOverOverlay()
    {
        if(!GetCursorPos(out var point))return false;
        // Native screen bounds also work for click-through overlays and use
        // the same coordinate space as the cursor on mixed-DPI monitors.
        foreach(PresentationSource presentation in PresentationSource.CurrentSources){
            if(presentation is not HwndSource source||source.IsDisposed||source.RootVisual==null)continue;
            if(source.RootVisual is Window window&&!states.TryGetValue(window,out _))continue;
            // Non-Window roots include WPF context-menu and submenu popups.
            if(IsWindowVisible(source.Handle)&&GetWindowRect(source.Handle,out var rect)&&
                point.X>=rect.Left&&point.X<rect.Right&&point.Y>=rect.Top&&point.Y<rect.Bottom)return true;
        }
        return false;
    }
    internal static bool Allows(Window? window) => window == null || !states.TryGetValue(window,out var state) || state.Allows(Environment.TickCount64);
    internal static void Attach(Window window)
    {
        if (states.TryGetValue(window,out _)) return;
        var state = new OverlaySnooze(); states.Add(window,state);
        var content = window.Content as UIElement;
        if (content == null) return;
        window.Content = null;
        var shell = new DockPanel();
        var hide = new Button { Content="Hide 60s", FontSize=10, Padding=new(5,1,5,1), Margin=new(2),
            HorizontalAlignment=HorizontalAlignment.Right, Focusable=false, ToolTip="Hide this overlay for 60 seconds. Its position and tracking are preserved." };
        hide.Click += (_,_) => { state.HideForMinute(Environment.TickCount64); window.Hide(); };
        var controls=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Visibility=Visibility.Hidden};
        state.Controls=controls;
        controls.Children.Add(hide);DockPanel.SetDock(controls,Dock.Top);
        shell.Children.Add(controls);shell.Children.Add(content);window.Content=shell;
        OverlayScale.Attach(window,shell,controls);
        window.IsVisibleChanged+=(_,_)=>RefreshHover();
        window.MouseEnter+=(_,_)=>RefreshHover();
        window.MouseLeave+=(_,_)=>RefreshHover();
        window.Closed+=(_,_)=>{states.Remove(window);RefreshHover();};
    }
}
