using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace AionDpsMeter.Local;

internal sealed class BossAlertOverlayWindow:Window
{
    readonly StackPanel rows=new();
    readonly (Border Card,TextBlock Title,TextBlock Detail,ProgressBar Bar)[] slots;
    long previewUntil;
    internal bool PreviewActive=>Environment.TickCount64<previewUntil;
    internal void Preview()=>previewUntil=Environment.TickCount64+30000;
    internal void EndPreview()=>previewUntil=0;
    internal BossAlertOverlayWindow(Preferences settings,Action save){
        Title=global::AionDpsMeter.Local.Localization.T("Wingbeat boss alerts");Width=400;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;
        WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=false;ShowActivated=false;
        UiTheme.Apply(this,settings);Background=Brushes.Transparent;Content=rows;
        slots=Enumerable.Range(0,3).Select(_=>{
            var title=new TextBlock{Foreground=UiTheme.Brush("#FFD982"),FontSize=18,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};
            var detail=new TextBlock{Foreground=Brushes.White,FontSize=13,TextWrapping=TextWrapping.Wrap,Margin=new(0,4,0,6)};
            // The countdown fills the card behind its text, with no themed track,
            // shine or opaque strip competing with the warning itself.
            var track=new FrameworkElementFactory(typeof(Grid),"PART_Track");
            var fill=new FrameworkElementFactory(typeof(Border),"PART_Indicator");
            fill.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Left);
            fill.SetValue(Border.BackgroundProperty,UiTheme.Brush("#40FFB454"));
            track.AppendChild(fill);
            var bar=new ProgressBar{Minimum=0,Maximum=1,IsHitTestVisible=false,Focusable=false,
                Template=new ControlTemplate(typeof(ProgressBar)){VisualTree=track}};
            var content=new StackPanel{Margin=new(12)};content.Children.Add(title);content.Children.Add(detail);
            var surface=new Grid();surface.Children.Add(bar);surface.Children.Add(content);
            surface.SizeChanged+=(_,_)=>surface.Clip=new RectangleGeometry(new Rect(0,0,surface.ActualWidth,surface.ActualHeight),7,7);
            var card=new Border{Child=surface,Background=UiTheme.Brush("#E610151F"),BorderBrush=UiTheme.Brush("#C0FFB454"),BorderThickness=new(1),CornerRadius=new(8),Margin=new(0,0,0,5)};
            rows.Children.Add(card);return(card,title,detail,bar);
        }).ToArray();
        Left=SystemParameters.WorkArea.Left+(SystemParameters.WorkArea.Width-Width)/2;Top=SystemParameters.WorkArea.Top+SystemParameters.WorkArea.Height*.22;
        if(settings.BossAlertsLeft is double left&&settings.BossAlertsTop is double top&&double.IsFinite(left)&&double.IsFinite(top)){
            Left=Math.Clamp(left,SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+Math.Max(0,SystemParameters.VirtualScreenWidth-Width));
            Top=Math.Clamp(top,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+Math.Max(0,SystemParameters.VirtualScreenHeight-150));
        }
        MouseLeftButtonDown+=(_,e)=>{if(!settings.BossAlertsLocked){DragMove();e.Handled=true;}};
        var menu=UiTheme.Menu(this);var locked=new MenuItem{Header="Lock position",IsCheckable=true,IsChecked=settings.BossAlertsLocked};
        locked.Click+=(_,_)=>{settings.BossAlertsLocked=locked.IsChecked;save();};menu.Items.Add(locked);
        var preview=new MenuItem{Header="Preview placement for 30 seconds"};preview.Click+=(_,_)=>Preview();menu.Items.Add(preview);
        var done=new MenuItem{Header="Finish setup"};done.Click+=(_,_)=>EndPreview();menu.Items.Add(done);
        var close=new MenuItem{Header="Disable boss alerts"};close.Click+=(_,_)=>Close();menu.Items.Add(close);ContextMenu=menu;
        ToolTip=global::AionDpsMeter.Local.Localization.T("Live boss signals and approximate HP preparation hints. Countdown only for observed casts. Right-click for placement options.");
        OverlayPlacement.Track(this,()=>{settings.BossAlertsLeft=Left;settings.BossAlertsTop=Top;save();});
    }
    internal void Update(BossAlert[] alerts,long now,Func<int,string> skillName){
        if(alerts.Length>0)EndPreview();
        if(alerts.Length==0&&PreviewActive)alerts=[new(1,"Boss alerts",0,"Drag here to position warnings",now,now+30000,0,false,"30-second setup preview · Right-click to finish")];
        if(alerts.Length==0&&IsVisible)Hide();
        for(int i=0;i<slots.Length;i++){
            var slot=slots[i];slot.Card.Visibility=i<alerts.Length?Visibility.Visible:Visibility.Collapsed;if(i>=alerts.Length){CountdownBar.Update(slot.Bar,0,0);continue;}
            var alert=alerts[i];slot.Title.Text=alert.State+(alert.TargetsYou?global::AionDpsMeter.Local.Localization.T(" - targeting you"):"");
            slot.Detail.Text=alert.Boss+(alert.Detail!=null?" | "+alert.Detail:"")+(alert.Skill>0?" | "+skillName(alert.Skill):"")+(alert.Duration>0?global::AionDpsMeter.Local.Localization.F($" | {Math.Max(0,alert.Until-now)/1000d:0.0}s"):"");
            slot.Bar.Visibility=alert.Duration>0?Visibility.Visible:Visibility.Collapsed;
            CountdownBar.Update(slot.Bar,alert.Duration>0?Math.Clamp((alert.Until-now)/(double)alert.Duration,0,1):0,
                alert.Duration>0?Math.Max(0,alert.Until-now)/1000d:0);
        }
    }
}
