using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AionDpsMeter.Local;

// Opt-in integration check. Uses isolated data and one off-screen window;
// it never starts capture or changes the user's language preferences.
internal static class LocalizationPresentationCheck
{
    internal static void Run()
    {
        var window=new Window{Left=-32000,Top=-32000,Width=900,Height=700,
            ShowInTaskbar=false,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.Manual};
        var root=new StackPanel();window.Content=root;
        var settings=new Preferences();UiTheme.Apply(window,settings);UiTheme.Apply(window,settings);
        var combo=new ComboBox{ItemsSource=new[]{"Party","Gathering"},SelectedIndex=0};root.Children.Add(combo);
        var named=new ComboBox{ItemsSource=new[]{new Choice("Unstable Spider")},DisplayMemberPath="Name",SelectedIndex=0};root.Children.Add(named);
        var check=new CheckBox{Content="Show my skills below my DPS bar"};root.Children.Add(check);
        var button=new Button{Content="Farming overlay"};root.Children.Add(button);
        var icon=new Border{Width=18,Height=18,Background=Brushes.Red};var iconButton=new Button{Content=icon};root.Children.Add(iconButton);
        var input=new TextBox{Tag="Search name or ID..."};root.Children.Add(input);
        var grid=new DataGrid{AutoGenerateColumns=false,ItemsSource=new[]{new Choice("Unstable Spider")},Height=100};
        grid.Columns.Add(new DataGridTextColumn{Header="Count",Binding=new System.Windows.Data.Binding("Name")});root.Children.Add(grid);
        root.Children.Add(UiSections.Heading("Farming tracking","Saved on this PC"));
        try{
            window.Show();window.UpdateLayout();
            window.Dispatcher.Invoke(()=>{},DispatcherPriority.Loaded);window.UpdateLayout();
            var texts=Descendants(window).OfType<TextBlock>().Select(t=>t.Text).ToHashSet();
            foreach(var expected in new[]{"Gruppe","Meine Fertigkeiten unter meinem DPS-Balken anzeigen","Sammel-Overlay","Name oder ID suchen …","Anzahl","Farmstatistik","Auf diesem PC gespeichert","Unstable Spider"})
                if(!texts.Contains(expected))throw new InvalidOperationException("German presentation missing: "+expected+"; rendered: "+string.Join(" / ",texts));
            if((string)combo.SelectedItem!="Party"||input.Tag?.ToString()!="Search name or ID..."||!ReferenceEquals(iconButton.Content,icon))
                throw new InvalidOperationException("Localization changed an internal selection, tag or icon control.");
            combo.SelectedIndex=1;window.UpdateLayout();
            if((string)combo.SelectedItem!="Gathering"||!Descendants(combo).OfType<TextBlock>().Any(t=>t.Text=="Sammeln"))
                throw new InvalidOperationException("Localized combo did not preserve its key after selection change.");
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(900,700,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var output=File.Create("localization-presentation.png");encoder.Save(output);
        }finally{window.Close();}
    }
    static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){
            var child=VisualTreeHelper.GetChild(parent,i);yield return child;
            foreach(var descendant in Descendants(child))yield return descendant;
        }
    }
    sealed record Choice(string Name);
}
