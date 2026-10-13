using System.Windows;
using System.Windows.Threading;

namespace AionDpsMeter.Local;

internal static class OverlayPlacement
{
    internal static void Track(Window window, Action save)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        void Save() { timer.Stop(); if (window.IsLoaded && double.IsFinite(window.Left) && double.IsFinite(window.Top)) save(); }
        void Schedule() { if (!window.IsLoaded) return; timer.Stop(); timer.Start(); }
        timer.Tick += (_, _) => Save();
        window.Loaded += (_, _) => Save();
        window.LocationChanged += (_, _) => Schedule();
        window.SizeChanged += (_, _) => Schedule();
        window.Closed += (_, _) => timer.Stop();
    }
}
