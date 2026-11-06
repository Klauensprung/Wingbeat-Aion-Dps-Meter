using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace AionDpsMeter.Local;
internal static class EncounterLogUiCheck
{
    internal static int Run(Application app){
        app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        try{
            var view=new EncounterLogView();var window=new Window{Content=view,Width=1440,Height=960,Left=-32000,Top=-32000,ShowActivated=false,ShowInTaskbar=false};
            UiTheme.Apply(window,new Preferences());
            try{
                view.Update(Enumerable.Range(0,52).Select(i=>FightArchiveSummary.Create(EncounterLogTests.Example(i))),true);
                window.Show();window.UpdateLayout();
                if(view.VisibleFightCount!=4)throw new Exception("Latest date did not select its four encounters.");
                Render("encounter-log-date.png");view.CheckBossMode();window.UpdateLayout();
                if(view.VisibleFightCount!=18)throw new Exception("Boss view did not collect matching attempts across dates.");
                Render("encounter-log-boss.png");
                window.Width=1000;window.UpdateLayout();Render("encounter-log-compact.png");
                view.Update([],true);window.UpdateLayout();if(view.VisibleFightCount!=0)throw new Exception("Empty history retained old rows.");Render("encounter-log-empty.png");
                void Render(string name){var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(name);encoder.Save(file);}
            }finally{window.Close();}
            var main=new MainWindow(true){Width=1400,Height=1000,Left=-32000,Top=-32000,ShowActivated=false};
            try{main.CheckEncounterLogPage();}finally{main.Close();}
            File.WriteAllText("encounter-log-ui-results.txt","PASS date groups, exact boss groups, desktop and compact layouts, empty history.");return 0;
        }catch(Exception ex){File.WriteAllText("encounter-log-ui-error.txt",ex.ToString());return 1;}
    }
}

public sealed partial class MainWindow
{
    internal void CheckEncounterLogPage(){
        archive.Clear();archive.AddRange(Enumerable.Range(0,52).Select(i=>FightArchiveSummary.Create(EncounterLogTests.Example(i))));archiveChanged=true;
        tabs.SelectedItem=tabs.Items.Cast<System.Windows.Controls.TabItem>().Single(t=>t.Header?.ToString()=="Encounter log");
        Show();timer.Stop();Refresh();UpdateLayout();
        if(encounterLog.VisibleFightCount!=4||character.Visibility==Visibility.Visible)throw new Exception("Encounter log integration or dedicated filter visibility failed.");
        Render(this,"encounter-log-main.png");
    }
}
