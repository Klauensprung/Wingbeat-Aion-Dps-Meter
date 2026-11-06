using System.IO;
using System.Windows;
using System.Windows.Controls;
namespace AionDpsMeter.Local;
internal static class NavigationUiCheck
{
    internal static int Run(Application app){
        app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        MainWindow? window=null;
        try{
            window=new MainWindow(true){Left=-32000,Top=-32000,ShowActivated=false};window.CheckNavigationRefresh();
            File.WriteAllText("navigation-ui-results.txt","PASS grouped sidebar, all overlay controls, tool search, encounter views and compact layout.");return 0;
        }catch(Exception ex){File.WriteAllText("navigation-ui-error.txt",ex.ToString());return 1;}
        finally{window?.Close();}
    }
}
public sealed partial class MainWindow
{
    internal void CheckNavigationRefresh(){
        Show();timer.Stop();Width=1280;Height=900;
        foreach(string page in new[]{"Encounter","Overlays","Tools","Encounter log"}){
            NavigateTo(page);Refresh();UpdateLayout();
            if(page=="Overlays"){
                var content=(tabs.SelectedItem as TabItem)!.Content as DependencyObject;
                if(LayoutVisuals<Button>(content!).Count()<16)throw new Exception("Overlay controls missing from the hub.");
                if(currentEncounterSummary!.Visibility!=Visibility.Collapsed||character.Visibility==Visibility.Visible)throw new Exception("Unrelated combat controls on setup page.");
            }
            if(page=="Encounter")encounterView.CheckPresentation(Current);
            Render(this,"navigation-"+page.Replace(' ','-')+".png");
        }
        NavigateTo("Tools");UpdateLayout();
        var search=LayoutVisuals<TextBox>(this).Single(t=>t.Tag?.ToString()=="Search tools");search.Text="__no_matching_tool__";UpdateLayout();
        if(!LayoutVisuals<TextBlock>(this).Any(t=>t.Text==Localization.T("No matching tools. Try another search.")&&t.IsVisible))throw new Exception("Tool search empty state missing.");search.Text="";
        Width=980;Height=650;NavigateTo("Overlays");Refresh();UpdateLayout();Render(this,"navigation-compact.png");
        NavigateTo("Encounter");Refresh();UpdateLayout();Render(this,"navigation-encounter-compact.png");
        foreach(string page in MainViews){NavigateTo(page);Refresh();UpdateLayout();}
    }
}
