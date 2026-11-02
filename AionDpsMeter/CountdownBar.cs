using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;

namespace AionDpsMeter.Local;

internal static class CountdownBar
{
    static readonly ConditionalWeakTable<ProgressBar, object> attached = new();

    // Animate toward the observed deadline, not toward the previous UI sample.
    // Replacement clocks keep corrections immediate without accumulating animations.
    internal static void Update(ProgressBar bar, double fraction, double remainingSeconds)
    {
        attached.GetValue(bar, control => {
            control.IsVisibleChanged += (_, _) => {
                if (!control.IsVisible) control.BeginAnimation(RangeBase.ValueProperty, null);
            };
            control.Unloaded += (_, _) => control.BeginAnimation(RangeBase.ValueProperty, null);
            return new object();
        });
        fraction = double.IsFinite(fraction) ? Math.Clamp(fraction, 0, 1) : 0;
        bar.BeginAnimation(RangeBase.ValueProperty, null);
        bar.Value = fraction;
        if (!bar.IsVisible || fraction <= 0 || !double.IsFinite(remainingSeconds) || remainingSeconds <= 0) return;
        bar.Value = 0;
        bar.BeginAnimation(RangeBase.ValueProperty,
            new DoubleAnimation(fraction, 0, TimeSpan.FromSeconds(remainingSeconds)) { FillBehavior = FillBehavior.Stop },
            HandoffBehavior.SnapshotAndReplace);
    }
}
