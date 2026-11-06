using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AionDpsMeter.Local;

internal sealed class CooldownSweep : FrameworkElement
{
    internal static readonly DependencyProperty FractionProperty = DependencyProperty.Register(nameof(Fraction),typeof(double),typeof(CooldownSweep),
        new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
    internal double Fraction { get=>(double)GetValue(FractionProperty); set=>SetValue(FractionProperty,value); }
    internal ImageSource? Icon {get;init;}
    internal CooldownSweep(){IsHitTestVisible=false;Unloaded+=(_,_)=>BeginAnimation(FractionProperty,null);}
    internal void Update(double fraction,double seconds){
        BeginAnimation(FractionProperty,null);Fraction=fraction;
        if(IsVisible&&fraction<1&&seconds>0&&double.IsFinite(seconds)){
            Fraction=1;
            BeginAnimation(FractionProperty,new DoubleAnimation(fraction,1,TimeSpan.FromSeconds(seconds)){FillBehavior=FillBehavior.Stop});
        }
    }
    protected override void OnRender(DrawingContext dc){
        double fraction=Math.Clamp(Fraction,0,1);
        if(fraction<=0||ActualWidth<=0||ActualHeight<=0)return;
        var bounds=new Rect(0,0,ActualWidth,ActualHeight);
        if(Icon==null)return;
        if(fraction>=1){dc.DrawImage(Icon,bounds);return;}
        var center=new Point(ActualWidth/2,ActualHeight/2);
        // Extend to the corners: the same clockwise pie sweep fills a square
        // icon, while the round tile's existing clip produces a circular sweep.
        double radius=Math.Sqrt(ActualWidth*ActualWidth+ActualHeight*ActualHeight)/2;
        double angle=fraction*2*Math.PI;
        var geometry=new StreamGeometry();
        using(var path=geometry.Open()){
            path.BeginFigure(center,true,true);
            path.LineTo(new Point(center.X,center.Y-radius),true,false);
            path.ArcTo(new Point(center.X+radius*Math.Sin(angle),center.Y-radius*Math.Cos(angle)),new Size(radius,radius),0,
                fraction>.5,SweepDirection.Clockwise,true,false);
        }
        geometry.Freeze();dc.PushClip(new RectangleGeometry(bounds));dc.PushClip(geometry);dc.DrawImage(Icon,bounds);dc.Pop();dc.Pop();
    }
}
