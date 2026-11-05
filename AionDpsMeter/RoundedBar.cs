using System.Windows;
using System.Windows.Media;

namespace AionDpsMeter.Local;

internal static class RoundedBar
{
    internal static void Clip(FrameworkElement element,double radius=6)
    {
        var geometry=new RectangleGeometry{RadiusX=radius,RadiusY=radius};
        element.Clip=geometry;
        void Resize(){
            var bounds=new Rect(0,0,Math.Max(0,element.ActualWidth),Math.Max(0,element.ActualHeight));
            if(geometry.Rect!=bounds)geometry.Rect=bounds;
        }
        element.SizeChanged+=(_,_)=>Resize();
        Resize();
    }
}
