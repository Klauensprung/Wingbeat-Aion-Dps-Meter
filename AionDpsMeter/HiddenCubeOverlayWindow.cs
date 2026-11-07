using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
namespace AionDpsMeter.Local;

internal sealed class HiddenCubeOverlayWindow : Window
{
    readonly RotateTransform rotation=new();
    readonly Path arrow=new(){Data=Geometry.Parse("M 16,0 L 30,30 L 16,23 L 2,30 Z"),Fill=UiTheme.Brush("#FFE29B"),Width=32,Height=32,RenderTransformOrigin=new(.5,.5)};
    readonly TextBlock direction=new(){Foreground=Brushes.White,FontSize=18,FontWeight=FontWeights.Bold};
    internal HiddenCubeOverlayWindow(Preferences settings,Action save){
        Title=Localization.T("Hidden cubes");Width=235;SizeToContent=SizeToContent.Height;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
        AllowsTransparency=true;Topmost=true;ShowActivated=false;ShowInTaskbar=false;UiTheme.Apply(this,settings);Background=Brushes.Transparent;
        var body=new StackPanel{Margin=new(12,30,12,12)};
        body.Children.Add(new TextBlock{Text=Localization.T("Hidden cube nearby"),Foreground=Brushes.White,FontSize=13,FontWeight=FontWeights.Bold});
        var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,8,0,0)};arrow.RenderTransform=rotation;row.Children.Add(arrow);direction.Margin=new(12,4,0,0);row.Children.Add(direction);body.Children.Add(row);Content=body;
        ToolTip=Localization.T("Compass direction from your character. North is up on the map. Only detected cubes are shown.");
        if(settings.HiddenCubeLeft is double x&&settings.HiddenCubeTop is double y&&double.IsFinite(x)&&double.IsFinite(y)){
            Left=Math.Clamp(x,SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+Math.Max(0,SystemParameters.VirtualScreenWidth-Width));
            Top=Math.Clamp(y,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+Math.Max(0,SystemParameters.VirtualScreenHeight-130));
        }
        body.MouseLeftButtonDown+=(_,e)=>{if(!settings.HiddenCubeLocked){DragMove();e.Handled=true;}};
        var menu=UiTheme.Menu(this);var locked=new MenuItem{Header="Lock position",IsCheckable=true,IsChecked=settings.HiddenCubeLocked};
        locked.Click+=(_,_)=>{settings.HiddenCubeLocked=locked.IsChecked;save();};menu.Items.Add(locked);
        var close=new MenuItem{Header="Close overlay"};close.Click+=(_,_)=>Close();menu.Items.Add(close);ContextMenu=menu;
        OverlayPlacement.Track(this,()=>{settings.HiddenCubeLeft=Left;settings.HiddenCubeTop=Top;save();});
    }
    internal void Update(HiddenCubeDirection? cube){
        arrow.Visibility=cube==null?Visibility.Collapsed:Visibility.Visible;
        direction.FontSize=cube==null?12:18;
        direction.Text=cube==null?Localization.T("Waiting for player position"):Localization.T(cube.Cardinal);
        if(cube!=null)rotation.Angle=cube.Bearing;
    }
}
