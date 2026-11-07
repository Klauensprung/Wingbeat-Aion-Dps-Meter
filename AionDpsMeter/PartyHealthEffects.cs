using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
namespace AionDpsMeter.Local;

// One small drawing per active health bar. No particles or animation clocks accumulate.
internal sealed class PartyHealthEffects : FrameworkElement
{
    static readonly Brush pulse=UiTheme.Brush("#FFFF344D");
    static readonly Brush bubble=UiTheme.Brush("#703ACC58");
    static readonly Pen rim=new(UiTheme.Brush("#C075F47F"),1.2);
    static readonly Brush shine=UiTheme.Brush("#B8D5FFB0");
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(33)};
    bool critical,poison;
    internal Func<double>? HealthWidth {get;init;}
    internal static bool ShouldBubble(PartyHealthRow row)=>row.Debuffs.Length>0&&!row.Unavailable&&row.Percent is >0;
    internal bool Animating=>timer.IsEnabled;
    internal static bool ShouldPulse(PartyHealthRow row)=>!row.Unavailable&&row.Percent is >0 and <25;
    internal static bool IsPoison(PartyMemberDebuff effect){
        var definition=EffectCatalogue.Default.Find(effect.Effect);
        return definition is {Kind:"Debuff"}&&definition.Name.Contains("Poison",StringComparison.OrdinalIgnoreCase);
    }
    internal PartyHealthEffects(){
        IsHitTestVisible=false;ClipToBounds=true;
        timer.Tick+=(_,_)=>InvalidateVisual();
        Loaded+=(_,_)=>RefreshTimer();Unloaded+=(_,_)=>timer.Stop();
        IsVisibleChanged+=(_,_)=>RefreshTimer();
    }
    internal void Update(PartyHealthRow row){
        bool nextCritical=ShouldPulse(row),nextPoison=ShouldBubble(row);
        if(critical==nextCritical&&poison==nextPoison)return;
        critical=nextCritical;poison=nextPoison;RefreshTimer();InvalidateVisual();
    }
    void RefreshTimer(){if(IsLoaded&&IsVisible&&(critical||poison))timer.Start();else timer.Stop();}
    protected override void OnRender(DrawingContext drawing){
        base.OnRender(drawing);
        double width=ActualWidth,height=ActualHeight;if(width<=0||height<=0)return;
        double seconds=Environment.TickCount64/1000d;
        if(critical){
            // Pulse across the whole row, below its labels, so very low HP is still noticeable.
            drawing.PushOpacity(.12+.5*(.5+.5*Math.Sin(seconds*Math.PI*2/1.1)));
            drawing.DrawRoundedRectangle(pulse,null,new Rect(0,0,width,height),6,6);drawing.Pop();
        }
        if(!poison)return;
        width=Math.Clamp(HealthWidth?.Invoke()??width,0,width);if(width<=0)return;
        drawing.PushClip(new RectangleGeometry(new Rect(0,0,width,height),6,6));
        int count=Math.Clamp((int)(width/30),2,10);
        for(int i=0;i<count;i++){
            double phase=(seconds/(1.8+i%3*.35)+i*.173)%1;
            double radius=3+i%3;
            double x=(i+.5)*width/count+Math.Sin(seconds*1.7+i)*2;
            double y=height+radius-phase*(height+radius*2);
            drawing.PushOpacity(Math.Sin(phase*Math.PI)*.85);
            drawing.DrawEllipse(bubble,rim,new Point(x,y),radius,radius);
            drawing.DrawEllipse(shine,null,new Point(x-radius*.3,y-radius*.3),.8,.8);
            drawing.Pop();
        }
        drawing.Pop();
    }
}
