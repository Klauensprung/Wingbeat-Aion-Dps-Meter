using System.Windows;
using System.Windows.Controls;
namespace AionDpsMeter.Local;
public sealed partial class MainWindow
{
    Window? radarTrackingWindow;
    void ShowRadarTracking()
    {
        if (radarTrackingWindow != null) { radarTrackingWindow.Activate(); return; }
        var window = new Window { Title = Localization.T("3D radar tracking"), Owner = this, Width = 660, Height = 650,
            MinWidth = 560, MinHeight = 440, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        radarTrackingWindow = window; window.Closed += (_,_) => radarTrackingWindow = null; UiTheme.Apply(window,preferences);
        var root = new DockPanel { Margin = new(20) }; window.Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        header.Children.Add(new TextBlock { Text=Localization.T("What should the radar show?"),FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,8) });
        header.Children.Add(new TextBlock { Text=Localization.T("Only detected objects in your current area are shown. Possible spawn locations are never plotted."),TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,16) });
        var categories = new WrapPanel { Margin=new(0,0,0,14) };header.Children.Add(categories);
        var enemies = new CheckBox { Content=Localization.T("Enemy players"),IsChecked=preferences.RadarShowEnemies,Margin=new(0,0,24,0) };
        var materials = new CheckBox { Content=Localization.T("Gathering materials"),IsChecked=preferences.RadarShowMaterials };
        categories.Children.Add(enemies);categories.Children.Add(materials);
        var mode = new ComboBox { Margin=new(0,0,0,12) };
        string[] modes=["Tracked","Custom","All"];
        foreach(string text in new[]{"Use farming selections","Choose individual materials","All detected nearby materials"})mode.Items.Add(Localization.T(text));
        mode.SelectedIndex=Math.Max(0,Array.IndexOf(modes,preferences.RadarMaterialMode));header.Children.Add(mode);
        var custom = new DockPanel();root.Children.Add(custom);
        var tools = new StackPanel();DockPanel.SetDock(tools,Dock.Top);custom.Children.Add(tools);
        var search = new TextBox { Margin=new(0,0,0,10),ToolTip=Localization.T("Search materials") };tools.Children.Add(new TextBlock{Text=Localization.T("Search materials"),Margin=new(0,0,0,5)});tools.Children.Add(search);
        var actions = new WrapPanel { Margin=new(0,0,0,10) };tools.Children.Add(actions);
        var select = new Button { Content=Localization.T("Select matching"),Margin=new(0,0,8,0) };
        var clear = new Button { Content=Localization.T("Clear matching") };actions.Children.Add(select);actions.Children.Add(clear);
        var grid = new WrapPanel();custom.Children.Add(new ScrollViewer {Content=grid,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        var rows = MapMarkers.Default.Groups.Where(g=>g.DatabaseType=="gatherable"&&g.ObjectId>0)
            .GroupBy(g=>g.Name,StringComparer.OrdinalIgnoreCase).OrderBy(g=>g.Key).Select(g=>(Name:g.Key,Ids:g.Select(x=>x.ObjectId).Distinct().ToArray())).ToArray();
        var chosen=(preferences.RadarMaterialObjects??[]).ToHashSet();
        void Save(){preferences.RadarMaterialObjects=chosen.Order().ToArray();radarMaterialKey=null;SavePreferences();RefreshCooldownVisibility();}
        bool Matches(string name)=>name.Contains(search.Text.Trim(),StringComparison.OrdinalIgnoreCase);
        void Fill(){
            grid.Children.Clear();
            foreach(var row in rows.Where(r=>Matches(r.Name))){
                var box=new CheckBox {Content=new TextBlock{Text=row.Name,TextTrimming=TextTrimming.CharacterEllipsis},ToolTip=row.Name,
                    IsChecked=row.Ids.All(chosen.Contains),Width=265,Margin=new(0,0,12,12),Padding=new(5)};
                box.Click+=(_,_)=>{foreach(int id in row.Ids)if(box.IsChecked==true)chosen.Add(id);else chosen.Remove(id);Save();};grid.Children.Add(box);
            }
            if(grid.Children.Count==0)grid.Children.Add(new TextBlock{Text=Localization.T("No matching materials"),Margin=new(4)});
        }
        void State(){mode.IsEnabled=materials.IsChecked==true;custom.IsEnabled=materials.IsChecked==true&&mode.SelectedIndex==1;custom.Opacity=custom.IsEnabled?1:.4;}
        enemies.Click+=(_,_)=>{preferences.RadarShowEnemies=enemies.IsChecked==true;Save();};
        materials.Click+=(_,_)=>{preferences.RadarShowMaterials=materials.IsChecked==true;State();Save();};
        mode.SelectionChanged+=(_,_)=>{if(mode.SelectedIndex<0)return;preferences.RadarMaterialMode=modes[mode.SelectedIndex];State();Save();};
        search.TextChanged+=(_,_)=>Fill();
        select.Click+=(_,_)=>{chosen.UnionWith(rows.Where(r=>Matches(r.Name)).SelectMany(r=>r.Ids));Fill();Save();};
        clear.Click+=(_,_)=>{chosen.ExceptWith(rows.Where(r=>Matches(r.Name)).SelectMany(r=>r.Ids));Fill();Save();};
        State();Fill();window.Show();
    }
}
