using System.IO;
using System.Windows;
using System.Windows.Controls;
namespace AionDpsMeter.Local;
public sealed partial class MainWindow
{
    FarmingOverlayWindow? farmingOverlay;FarmingTargetView? farmingOverlayView;long farmingOverlayTick;
    MapTargets? farmingOverlayMaterials;
    Window? farmingMaterialPicker;
    sealed record FarmingMaterial(int Id,string Name);
    void ToggleFarmingOverlay() {
        if(farmingOverlay!=null){farmingOverlay.Close();return;}
        ShowFarmingMaterialPicker();
    }
    void ShowFarmingMaterialPicker(){
        if(farmingMaterialPicker!=null){farmingMaterialPicker.Activate();return;}
        farmingOverlayMaterials??=new(demoMode?null:AppDataPaths.FilePath("farming-overlay-materials.json"));
        var selected=farmingOverlayMaterials.Snapshot().Select(t=>t.Id).ToHashSet();
        var materials=ItemFarmLinks.Default.Items.Where(i=>i.Gatherables.Length>0).Select(i=>new FarmingMaterial(i.Id,ItemNames.Default.Find(i.Id))).OrderBy(i=>i.Name).ToArray();
        var picker=new Window{Title=Localization.T("Choose farming materials"),Owner=this,Width=460,Height=560,MinWidth=360,MinHeight=400,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        farmingMaterialPicker=picker;picker.Closed+=(_,_)=>farmingMaterialPicker=null;UiTheme.Apply(picker,preferences);
        var root=new DockPanel{Margin=new(18)};picker.Content=root;
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock{Text=Localization.T("What do you want to track?"),FontSize=20,Margin=new(0,0,0,10)});
        top.Children.Add(new TextBlock{Text=Localization.T("Counts newly acquired items since your Farming reset. Existing inventory is not added."),TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)});
        var search=new TextBox{Tag="Search materials",Margin=new(0,0,0,10)};top.Children.Add(search);
        var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        var error=new TextBlock{TextWrapping=TextWrapping.Wrap};bottom.Children.Add(error);
        var start=new Button{Content=Localization.T("Start tracking"),Margin=new(0,12,0,0),IsEnabled=selected.Count>0};bottom.Children.Add(start);
        var list=new StackPanel();root.Children.Add(new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        void Populate(){
            list.Children.Clear();
            foreach(var material in materials.Where(m=>m.Name.Contains(search.Text.Trim(),StringComparison.OrdinalIgnoreCase))){
                var box=new CheckBox{Content=material.Name,IsChecked=selected.Contains(material.Id),Margin=new(0,5,0,5)};
                box.Click+=(_,_)=>{if(box.IsChecked==true)selected.Add(material.Id);else selected.Remove(material.Id);start.IsEnabled=selected.Count>0;};list.Children.Add(box);
            }
        }
        search.TextChanged+=(_,_)=>Populate();Populate();
        start.Click+=(_,_)=>{
            try{farmingOverlayMaterials.SetItems(selected);}
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){error.Text=ex.Message;return;}
            picker.Close();if(farmingOverlay==null)OpenFarmingOverlay();else farmingOverlayTick=long.MinValue;
        };
        picker.Show();
    }
    void OpenFarmingOverlay(){
        farmingOverlayMaterials??=new(demoMode?null:AppDataPaths.FilePath("farming-overlay-materials.json"));
        var window=new FarmingOverlayWindow(preferences,SavePreferences,ShowFarmingMaterialPicker);farmingOverlay=window;farmingOverlayView=new();farmingOverlayTick=long.MinValue;
        window.Closed+=(_,_)=>{if(ReferenceEquals(farmingOverlay,window)){farmingOverlay=null;farmingOverlayView=null;}};
        RefreshCooldownVisibility();
    }
    void RefreshFarmingOverlay(bool allowed) {
        if(farmingOverlay==null||farmingOverlayView==null)return;
        long tick=Environment.TickCount64;
        if(farmingOverlayTick==long.MinValue||tick-farmingOverlayTick>=1000){farmingOverlayTick=tick;
            var view=farmingOverlayView.Update(engine,farmingOverlayMaterials!.Snapshot(),preferences.FarmingTargetGoal,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            farmingOverlay.Update(view,capture!=null,preferences.FarmingTargetGoal);
        }
        if(allowed&&!farmingOverlay.IsVisible)farmingOverlay.Show();else if(!allowed&&farmingOverlay.IsVisible)farmingOverlay.Hide();
    }
}
