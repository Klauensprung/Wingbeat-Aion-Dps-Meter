using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AionDpsMeter.Local;

internal static class OverlayMotion
{
    internal static readonly TimeSpan ValueDuration=TimeSpan.FromSeconds(.25);
    static readonly ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty,double>> targets=new();
    internal static double Lerp(double from,double to,double progress)=>from+(to-from)*Math.Clamp(progress,0,1);
    static bool Changed(DependencyObject item,DependencyProperty property,double target,out bool first){
        var values=targets.GetOrCreateValue(item);first=!values.TryGetValue(property,out double old);
        if(!first&&old==target)return false;values[property]=target;return true;
    }
    internal static void Number(FrameworkElement item,DependencyProperty property,double target){
        if(!double.IsFinite(target)||!Changed(item,property,target,out bool first))return;
        double current=(double)item.GetValue(property);
        item.BeginAnimation(property,null);item.SetValue(property,target);
        if(!first&&item.IsVisible&&double.IsFinite(current))
            item.BeginAnimation(property,new DoubleAnimation(current,target,ValueDuration){FillBehavior=FillBehavior.Stop},HandoffBehavior.SnapshotAndReplace);
    }
    internal static void Column(ColumnDefinition column,double target,bool visible){
        target=Math.Max(0,target);
        if(!Changed(column,ColumnDefinition.WidthProperty,target,out bool first))return;
        double current=column.Width.Value;
        column.BeginAnimation(ColumnDefinition.WidthProperty,null);column.Width=new(target,GridUnitType.Star);
        if(!first&&visible)
            column.BeginAnimation(ColumnDefinition.WidthProperty,new StarAnimation(current,target),HandoffBehavior.SnapshotAndReplace);
    }
    sealed class StarAnimation : AnimationTimeline
    {
        readonly double from,to;
        public StarAnimation(){Duration=ValueDuration;FillBehavior=FillBehavior.Stop;}
        public StarAnimation(double from,double to):this(){this.from=from;this.to=to;}
        public override Type TargetPropertyType=>typeof(GridLength);
        protected override Freezable CreateInstanceCore()=>new StarAnimation(from,to);
        public override object GetCurrentValue(object origin,object destination,AnimationClock clock)=>new GridLength(Lerp(from,to,clock.CurrentProgress??1),GridUnitType.Star);
    }
}

// Keep layout/hit targets in their new order, animating only the visual offset.
public class AnimatedStackPanel : StackPanel
{
    readonly Dictionary<UIElement,Point> positions=new();
    protected override Size ArrangeOverride(Size size){
        var result=base.ArrangeOverride(size);
        var active=Children.Cast<UIElement>().ToHashSet();
        foreach(var old in positions.Keys.Where(k=>!active.Contains(k)).ToArray())positions.Remove(old);
        foreach(UIElement child in Children){
            var point=VisualTreeHelper.GetOffset(child);var next=new Point(point.X,point.Y);
            if(positions.TryGetValue(child,out var before)&&before!=next&&child.IsVisible&&SystemParameters.ClientAreaAnimation){
                var transform=child.RenderTransform as TranslateTransform;
                if(transform==null||transform.IsFrozen)child.RenderTransform=transform=new TranslateTransform();
                double offset=before.Y+transform.Y-next.Y;
                transform.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(offset,0,TimeSpan.FromMilliseconds(220)){
                    EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop},HandoffBehavior.SnapshotAndReplace);
            }
            positions[child]=next;
        }
        return result;
    }
}
