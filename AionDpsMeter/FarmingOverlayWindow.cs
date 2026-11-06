using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace AionDpsMeter.Local;
internal sealed class FarmingOverlayWindow : Window
{
    readonly StackPanel rows=new();
    sealed record Bar(Border Card,TextBlock Name,TextBlock Count,Border Flash){internal decimal? Previous;}
    readonly Dictionary<(string Kind,int Id),Bar> bars=new();
    internal FarmingOverlayWindow(Preferences settings,Action save,Action targets) {
        Title=global::AionDpsMeter.Local.Localization.T("Wingbeat farming");Width=360;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStyle=WindowStyle.None;
        AllowsTransparency=true;Topmost=true;ShowInTaskbar=false;ShowActivated=false;UiTheme.Apply(this,settings);Background=Brushes.Transparent;
        var body=new StackPanel {Margin=new(12)};
        body.Children.Add(rows);
        body.MouseLeftButtonDown+=(_,e)=>{if(!settings.FarmingOverlayLocked){DragMove();e.Handled=true;}};
        Content=new Border {Child=body,Background=Brushes.Transparent};
        if(settings.FarmingOverlayLeft is double left&&settings.FarmingOverlayTop is double top&&double.IsFinite(left)&&double.IsFinite(top)){
            Left=Math.Clamp(left,SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+Math.Max(0,SystemParameters.VirtualScreenWidth-Width));
            Top=Math.Clamp(top,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+Math.Max(0,SystemParameters.VirtualScreenHeight-400));}
        var menu=UiTheme.Menu(this);var locked=new MenuItem {Header="Lock position",IsCheckable=true,IsChecked=settings.FarmingOverlayLocked};
        locked.Click+=(_,_)=>{settings.FarmingOverlayLocked=locked.IsChecked;save();};menu.Items.Add(locked);
        var open=new MenuItem{Header="Targets"};open.Click+=(_,_)=>targets();menu.Items.Add(open);
        var close=new MenuItem {Header="Close farming overlay"};close.Click+=(_,_)=>Close();menu.Items.Add(close);ContextMenu=menu;
        OverlayPlacement.Track(this,()=>{settings.FarmingOverlayLeft=Left;settings.FarmingOverlayTop=Top;save();});
    }
    internal void Update(FarmingViewSnapshot view,bool capturing,int petGoal) {
        var shown=view.OverlayRows??view.Rows;
        var keys=shown.Select(r=>(r.Target.Kind,r.Target.Id)).ToHashSet();
        foreach(var key in bars.Keys.Where(k=>!keys.Contains(k)).ToArray()){rows.Children.Remove(bars[key].Card);bars.Remove(key);}
        int index=0;
        foreach(var item in shown){
            var key=(item.Target.Kind,item.Target.Id);
            if(!bars.TryGetValue(key,out var bar)){
                var gauge=new Grid{Height=32,Background=UiTheme.Brush("#60232D3B")};RoundedBar.Clip(gauge);
                gauge.Children.Add(new Border{Background=UiTheme.Brush("#55D6BE"),Opacity=.35,CornerRadius=new(6)});
                var flash=new Border{Background=UiTheme.Brush("#B0FFF1"),Opacity=0,IsHitTestVisible=false};gauge.Children.Add(flash);
                var labels=new Grid{Margin=new(8,0,8,0)};labels.ColumnDefinitions.Add(new());labels.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
                var name=new TextBlock{Foreground=Brushes.White,FontSize=12,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new(0,0,8,0)};
                var count=new TextBlock{Foreground=Brushes.White,FontSize=12,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center,Tag="Farming overlay count"};
                labels.Children.Add(name);Grid.SetColumn(count,1);labels.Children.Add(count);gauge.Children.Add(labels);
                var card=new Border{Child=gauge,CornerRadius=new(6),Margin=new(0,2,0,2)};
                bar=new(card,name,count,flash);bars[key]=bar;
            }
            if(bar.Name.Text!=item.Name)bar.Name.Text=item.Name;
            string text=(item.Progress.CountIsPartial?">=":"")+(item.Progress.Count?.ToString("N0")??"?");
            if(bar.Count.Text!=text)bar.Count.Text=text;
            if(!view.SceneChanged&&bar.Previous is decimal before&&item.Progress.Count is decimal after&&after>before&&IsVisible)
                bar.Flash.BeginAnimation(OpacityProperty,new DoubleAnimation(.7,0,TimeSpan.FromMilliseconds(550)){EasingFunction=new QuadraticEase{EasingMode=EasingMode.EaseOut}},HandoffBehavior.SnapshotAndReplace);
            else if(view.SceneChanged||item.Progress.Count<bar.Previous)bar.Flash.BeginAnimation(OpacityProperty,null);
            bar.Previous=item.Progress.Count;
            bar.Card.ToolTip=item.Name;
            if(rows.Children.IndexOf(bar.Card)!=index){rows.Children.Remove(bar.Card);rows.Children.Insert(index,bar.Card);}index++;
        }
    }
}
