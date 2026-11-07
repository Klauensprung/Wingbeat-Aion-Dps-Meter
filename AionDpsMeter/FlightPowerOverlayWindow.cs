using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace AionDpsMeter.Local;

internal sealed class FlightPowerOverlayWindow : Window
{
    const double BarHeight=200;
    readonly Border fill=new(){VerticalAlignment=VerticalAlignment.Bottom,Height=0,Background=UiTheme.Brush("#C055D6BE")};
    readonly TextBlock percent=new(){Foreground=Brushes.White,FontWeight=FontWeights.Bold,FontSize=14,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    readonly TextBlock state=new(){Foreground=UiTheme.Brush("#BBC8D8"),FontSize=10,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,Margin=new(0,6,0,0)};
    internal static double? Fraction(ResourceSnapshot? sample)=>sample is {Fp:>=0,MaximumFp:>0}&&sample.Fp<=sample.MaximumFp?(double)sample.Fp.Value/sample.MaximumFp.Value:null;
    internal FlightPowerOverlayWindow(Preferences settings,Action save){
        Title=Localization.T("Flight power");Width=130;SizeToContent=SizeToContent.Height;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
        AllowsTransparency=true;Topmost=true;ShowActivated=false;ShowInTaskbar=false;UiTheme.Apply(this,settings);Background=Brushes.Transparent;
        var body=new StackPanel{Margin=new(10,28,10,10),Width=68,HorizontalAlignment=HorizontalAlignment.Center};
        body.Children.Add(new TextBlock{Text=Localization.T("Flight power"),Foreground=Brushes.White,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,0,0,8)});
        var gauge=new Grid{Height=BarHeight,Width=48,Background=UiTheme.Brush("#80232D3B")};RoundedBar.Clip(gauge);gauge.Children.Add(fill);gauge.Children.Add(percent);
        body.Children.Add(gauge);body.Children.Add(state);Content=body;
        if(settings.FlightPowerLeft is double x&&settings.FlightPowerTop is double y&&double.IsFinite(x)&&double.IsFinite(y)){
            Left=Math.Clamp(x,SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+Math.Max(0,SystemParameters.VirtualScreenWidth-Width));
            Top=Math.Clamp(y,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+Math.Max(0,SystemParameters.VirtualScreenHeight-300));
        }
        body.MouseLeftButtonDown+=(_,e)=>{if(!settings.FlightPowerLocked){DragMove();e.Handled=true;}};
        var menu=UiTheme.Menu(this);var locked=new MenuItem{Header="Lock position",IsCheckable=true,IsChecked=settings.FlightPowerLocked};
        locked.Click+=(_,_)=>{settings.FlightPowerLocked=locked.IsChecked;save();};menu.Items.Add(locked);
        var close=new MenuItem{Header="Close overlay"};close.Click+=(_,_)=>Close();menu.Items.Add(close);ContextMenu=menu;
        OverlayPlacement.Track(this,()=>{settings.FlightPowerLeft=Left;settings.FlightPowerTop=Top;save();});Update(null,false);
    }
    internal void Update(ResourceSnapshot? sample,bool capturing){
        double? fraction=capturing?Fraction(sample):null;
        fill.Visibility=fraction.HasValue?Visibility.Visible:Visibility.Collapsed;
        if(fraction is double value)OverlayMotion.Number(fill,HeightProperty,BarHeight*value);
        string label=fraction is double f?$"{Math.Floor(f*100):0}%":"?";
        if(percent.Text!=label)percent.Text=label;
        string status=!capturing?Localization.T("Capture stopped"):fraction==null?Localization.T("Waiting for flight power"):fraction==1?Localization.T("Fully recharged"):Localization.T("Flight power");
        if(state.Text!=status)state.Text=status;
        ToolTip=sample is {Fp: not null,MaximumFp:>0}?Localization.F($"Flight power: {sample.Fp.Value/100d:N1} / {sample.MaximumFp.Value/100d:N1} (last captured)"):status;
    }
}
