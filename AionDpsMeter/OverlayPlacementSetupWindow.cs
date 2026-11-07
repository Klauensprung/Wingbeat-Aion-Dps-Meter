using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
namespace AionDpsMeter.Local;

internal sealed record OverlaySetupStep(string Name, string Hint, Rect Bounds, bool Resizable, Action<bool,Rect> Apply, Func<Window> CreatePreview);
internal sealed class OverlayPlacementSetupWindow : Window
{
    readonly OverlaySetupStep[] steps;
    readonly List<(OverlaySetupStep Step,bool Enabled,Rect Bounds,string Key,double Scale)> decisions=[];
    readonly Preferences settings;
    readonly Action finish;
    int index;
    double previewScale;
    bool completed;
    Window? preview;
    internal Window PreviewWindow=>preview!;
    internal OverlayPlacementSetupWindow(Preferences settings,OverlaySetupStep[] steps,Action finish){
        this.settings=settings;this.steps=steps;this.finish=finish;
        Title=Localization.T("Overlay setup");WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;
        Topmost=true;ShowInTaskbar=false;Width=340;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        UiTheme.Apply(this,settings);Background=Brushes.Transparent;
        Closed+=(_,_)=>{preview?.Close();if(completed){foreach(var choice in decisions){choice.Step.Apply(choice.Enabled,choice.Bounds);if(!choice.Step.Resizable)OverlayScale.Set(choice.Key,choice.Scale);}finish();}};
        ShowStep();
    }
    void Decide(bool enabled){
        decisions.Add((steps[index],enabled,new Rect(preview!.Left,preview.Top,preview.ActualWidth,preview.ActualHeight),OverlayScale.Key(preview),previewScale));
        if(++index==steps.Length){completed=true;Close();}else ShowStep();
    }
    void ShowStep(){
        var step=steps[index];string[] colors=["#55D6BE","#7C9EF7","#B087E9","#FFC857","#FF8484"];
        var color=UiTheme.Brush(colors[index%colors.Length]);
        preview?.Close();
        preview=step.CreatePreview();
        preview.Title=Localization.T(step.Name);preview.ShowActivated=false;
        OverlayHideControls.SetupPreview(preview);
        preview.Left=step.Bounds.X;preview.Top=step.Bounds.Y;
        if(step.Resizable){preview.SizeToContent=SizeToContent.Manual;preview.Width=Math.Max(preview.MinWidth,step.Bounds.Width);preview.Height=Math.Max(preview.MinHeight,step.Bounds.Height);}
        var content=(FrameworkElement)preview.Content;preview.Content=null;content.IsHitTestVisible=false;
        var scaled=new Border{Child=content};var layer=new Grid();layer.Children.Add(scaled);
        var drag=new Border{Background=Brushes.Transparent};drag.MouseLeftButtonDown+=(_,e)=>{preview.DragMove();e.Handled=true;};layer.Children.Add(drag);
        layer.Children.Add(new Border{BorderBrush=color,BorderThickness=new(1),CornerRadius=new(6),IsHitTestVisible=false});
        var handle=new Thumb{Width=28,Height=28,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Cursor=Cursors.SizeNWSE,ToolTip=Localization.T("Resize"),Tag="SetupResizeHandle"};
        var template=new ControlTemplate(typeof(Thumb));
        var square=new FrameworkElementFactory(typeof(Border));square.SetValue(Border.BackgroundProperty,color);square.SetValue(Border.CornerRadiusProperty,new CornerRadius(5));
        var glyph=new FrameworkElementFactory(typeof(TextBlock));glyph.SetValue(TextBlock.TextProperty,"\u2922");glyph.SetValue(TextBlock.FontSizeProperty,23d);glyph.SetValue(TextBlock.ForegroundProperty,Brushes.Black);glyph.SetValue(TextBlock.HorizontalAlignmentProperty,HorizontalAlignment.Center);glyph.SetValue(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center);square.AppendChild(glyph);template.VisualTree=square;handle.Template=template;
        layer.Children.Add(handle);preview.Content=layer;preview.ResizeMode=ResizeMode.NoResize;
        preview.Show();preview.UpdateLayout();
        double baseWidth=preview.ActualWidth,baseHeight=preview.ActualHeight,baseScale=OverlayScale.Get(OverlayScale.Key(preview));previewScale=baseScale;
        preview.Left=Math.Clamp(preview.Left,SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+Math.Max(0,SystemParameters.VirtualScreenWidth-baseWidth));
        preview.Top=Math.Clamp(preview.Top,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+Math.Max(0,SystemParameters.VirtualScreenHeight-baseHeight));
        handle.DragDelta+=(_,e)=>{
            preview.SizeToContent=SizeToContent.Manual;
            if(step.Resizable){preview.Width=Math.Clamp(preview.ActualWidth+e.HorizontalChange,Math.Max(180,preview.MinWidth),4000);preview.Height=Math.Clamp(preview.ActualHeight+e.VerticalChange,Math.Max(80,preview.MinHeight),4000);}
            else{
                double next=Math.Clamp(previewScale+Math.Max(Math.Abs(e.HorizontalChange),Math.Abs(e.VerticalChange))*Math.Sign(Math.Abs(e.HorizontalChange)>Math.Abs(e.VerticalChange)?e.HorizontalChange:e.VerticalChange)*baseScale/Math.Max(baseWidth,baseHeight),50,200);
                previewScale=next;double factor=next/baseScale;scaled.LayoutTransform=new ScaleTransform(factor,factor);preview.Width=baseWidth*factor;preview.Height=baseHeight*factor;
            }
        };
        var panel=new DockPanel{Margin=new(16)};
        Content=new Border{Background=UiTheme.Brush("#E0182030"),BorderBrush=color,BorderThickness=new(3),CornerRadius=new(12),Child=panel};
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);panel.Children.Add(top);
        top.Children.Add(new TextBlock{Text=Localization.T("Overlay setup")+$"  {index+1} / {steps.Length}",Foreground=color,FontSize=12});
        top.Children.Add(new TextBlock{Text=Localization.T(step.Name),Foreground=Brushes.White,FontWeight=FontWeights.Bold,FontSize=22,Margin=new(0,8,0,8)});
        top.MouseLeftButtonDown+=(_,e)=>{if(e.ChangedButton==MouseButton.Left){DragMove();e.Handled=true;}};
        var bottom=new StackPanel{Margin=new(0,10,0,0)};DockPanel.SetDock(bottom,Dock.Bottom);panel.Children.Add(bottom);
        var actions=new WrapPanel();bottom.Children.Add(actions);
        var yes=new Button{Content="\u2713 "+Localization.T("Enable"),Padding=new(12,8,12,8),Margin=new(0,0,8,0),Background=color,Foreground=Brushes.Black};yes.Click+=(_,_)=>Decide(true);actions.Children.Add(yes);
        var no=new Button{Content="\u2715 "+Localization.T("Skip"),Padding=new(12,8,12,8)};no.Click+=(_,_)=>Decide(false);actions.Children.Add(no);
        var cancel=new Button{Content=Localization.T("Cancel setup"),Margin=new(0,10,0,0),HorizontalAlignment=HorizontalAlignment.Left};cancel.Click+=(_,_)=>Close();bottom.Children.Add(cancel);
    }
}
