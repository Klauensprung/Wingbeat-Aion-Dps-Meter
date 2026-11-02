using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace AionDpsMeter.Local;
internal sealed class FarmingOverlayWindow : Window
{
    readonly TextBlock summary=new(){FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new(0,6,0,8),Tag="Farming overlay summary"};
    readonly TextBlock notice=new(){FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,0),Tag="Farming overlay notice"};
    readonly StackPanel rows=new();FarmingViewRow[] previous=[];
    readonly FarmingNoticeQueue notices=new();
    internal FarmingOverlayWindow(Preferences settings,Action save,Action targets) {
        Title="Wingbeat farming";Width=360;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStyle=WindowStyle.None;
        AllowsTransparency=true;Topmost=true;ShowInTaskbar=false;ShowActivated=false;UiTheme.Apply(this,settings);Background=Brushes.Transparent;
        var body=new StackPanel {Margin=new(12)};var header=new DockPanel();
        var open=new Button {Content="Targets",FontSize=11,Padding=new(8,3,8,3)};DockPanel.SetDock(open,Dock.Right);open.Click+=(_,_)=>targets();header.Children.Add(open);
        var title=new TextBlock {Text="FARMING",FontSize=11,FontWeight=FontWeights.SemiBold,Foreground=UiTheme.Brush(settings.Accent),VerticalAlignment=VerticalAlignment.Center};
        title.MouseLeftButtonDown+=(_,e)=>{if(!settings.FarmingOverlayLocked){DragMove();e.Handled=true;}};header.Children.Add(title);body.Children.Add(header);
        body.Children.Add(summary);body.Children.Add(rows);body.Children.Add(notice);
        Content=new Border {Child=body,Background=UiTheme.Brush("#ED10151F"),BorderBrush=UiTheme.Brush("#293443"),BorderThickness=new(1),CornerRadius=new(8)};
        if(settings.FarmingOverlayLeft is double left&&settings.FarmingOverlayTop is double top&&double.IsFinite(left)&&double.IsFinite(top)){
            Left=Math.Clamp(left,SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+Math.Max(0,SystemParameters.VirtualScreenWidth-Width));
            Top=Math.Clamp(top,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+Math.Max(0,SystemParameters.VirtualScreenHeight-400));}
        var menu=UiTheme.Menu(this);var locked=new MenuItem {Header="Lock position",IsCheckable=true,IsChecked=settings.FarmingOverlayLocked};
        locked.Click+=(_,_)=>{settings.FarmingOverlayLocked=locked.IsChecked;save();};menu.Items.Add(locked);
        var close=new MenuItem {Header="Close farming overlay"};close.Click+=(_,_)=>Close();menu.Items.Add(close);ContextMenu=menu;
        OverlayPlacement.Track(this,()=>{settings.FarmingOverlayLeft=Left;settings.FarmingOverlayTop=Top;save();});
    }
    internal void Update(FarmingViewSnapshot view,bool capturing,int petGoal) {
        var s=view.Session;
        summary.Text=$"{(capturing?"Live capture":"Capture stopped")} / Pet goal: level {petGoal}\nKills: {s.Kills:N0} ({s.KillsPerHour:N0}/h) / Souls gained: {s.Souls:N0}\nShowing {Math.Min(5,view.Rows.Length)} of {view.Rows.Length} targets";
        summary.ToolTip="Totals start at each Farming reset; nearby counts use captured scene membership, not distance. Open Targets for reset timestamps and all entries.";
        if(s.HasOmissions)summary.Text+=" / partial totals";
        notice.Text=string.Join("\n",notices.Update(view.Notices,Environment.TickCount64,view.SceneChanged).Select(FarmingTargetView.Notice));
        notice.Visibility=string.IsNullOrEmpty(notice.Text)?Visibility.Collapsed:Visibility.Visible;
        var shown=view.Rows.Take(5).ToArray();if(previous.SequenceEqual(shown))return;previous=shown;rows.Children.Clear();
        foreach(var row in shown){var p=row.Progress;var block=new StackPanel {Margin=new(0,4,0,4)};
            block.Children.Add(new TextBlock {Text=row.Name,TextTrimming=TextTrimming.CharacterEllipsis,ToolTip=$"{row.Name} ({row.Target.Id})",FontSize=12});
            string progress=p.Done==true?"Complete":p.Done==null?"Unknown progress":"In progress";
            string goal=row.Target.Kind=="pet"?$"level {petGoal}":p.Goal?.ToString("N0")??"-";
            block.Children.Add(new TextBlock {Text=$"{(p.CountIsPartial?">=":"")}{p.Count?.ToString("N0")??"?"} / goal {goal} / near {row.Nearby?.ToString("N0")??"?"} / {progress}",FontSize=10,TextWrapping=TextWrapping.Wrap});rows.Children.Add(block);
        }
    }
}
