using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace AionDpsMeter.Local;

internal sealed class PartyAttackWarningWindow:Window
{
    readonly TextBlock message=new(){Foreground=UiTheme.Brush("#FFFF8793"),FontSize=23,FontWeight=FontWeights.Bold,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,Margin=new(14,28,14,12)};
    internal PartyAttackWarningWindow(Preferences settings,Action save){
        Title=Localization.T("Party attack warnings");Width=420;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;
        WindowStyle=WindowStyle.None;AllowsTransparency=true;Topmost=true;ShowInTaskbar=false;ShowActivated=false;
        UiTheme.Apply(this,settings);Background=Brushes.Transparent;
        Content=new Border{Child=message,Background=UiTheme.Brush("#CB28151D"),CornerRadius=new(10)};
        Left=settings.PartyAttackLeft??SystemParameters.WorkArea.Left+(SystemParameters.WorkArea.Width-Width)/2;
        Top=settings.PartyAttackTop??SystemParameters.WorkArea.Top+SystemParameters.WorkArea.Height*.2;
        MouseLeftButtonDown+=(_,e)=>{DragMove();e.Handled=true;};
        OverlayPlacement.Track(this,()=>{settings.PartyAttackLeft=Left;settings.PartyAttackTop=Top;save();});
    }
    internal void Update(string[] names){
        string text=string.Join("\n",names.Select(name=>Localization.F($"{name} gets attacked!")));
        if(message.Text!=text)message.Text=text;
    }
}
