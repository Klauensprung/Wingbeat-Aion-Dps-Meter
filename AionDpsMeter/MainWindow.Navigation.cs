using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void NavigateTo(string name)=>tabs.SelectedItem=tabs.Items.Cast<TabItem>().Single(t=>t.Header?.ToString()==name);
    FrameworkElement BuildOverlayHub(WrapPanel original){
        var buttons=original.Children.Cast<Button>().ToArray();original.Children.Clear();
        var page=new StackPanel{Margin=new(4,8,4,12)};
        page.Children.Add(UiSections.Heading("Your game, your layout","Choose an overlay below. Highlighted buttons are enabled."));
        var cards=new WrapPanel();page.Children.Add(cards);
        void Section(string title,string hint,params int[] indices){
            var body=new StackPanel();
            body.Children.Add(new TextBlock{Text=Localization.T(title),FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,8)});
            var description=new TextBlock{Text=Localization.T(hint),TextWrapping=TextWrapping.Wrap,MinHeight=44,Margin=new(0,0,0,14)};description.SetResourceReference(TextBlock.ForegroundProperty,"Muted");body.Children.Add(description);
            foreach(int i in indices){var button=buttons[i];button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Margin=new(0,0,0,8);button.Padding=new(12,10,12,10);body.Children.Add(button);}
            var card=UiTheme.Card(body,new Thickness(20));card.Width=310;card.Margin=new(0,0,16,16);cards.Children.Add(card);
        }
        Section("Combat & party","Damage, party health and nearby opponents.",1,3,2,10);
        Section("Gathering & loot","Track materials, collect pets and follow a route.",8,9,16,5,7,6,15);
        Section("Timers & alerts","Boss mechanics, reminders and connection status.",0,12,13,11,14);
        var actions=new WrapPanel{Margin=new(0,4,0,0)};page.Children.Add(actions);
        actions.Children.Add(buttons[4]);
        actions.Children.Add(IconButton(UiIcon.Settings,Localization.T("Overlay settings"),()=>ShowSettings("Overlays")));
        actions.Children.Add(IconButton(UiIcon.Cooldowns,Localization.T("Choose tracked skills"),ConfigureCooldowns));
        return new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    }
}
