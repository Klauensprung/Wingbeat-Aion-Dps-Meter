using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AionDpsMeter.Local;

internal sealed class PetTrackerOverlayWindow : Window
{
    readonly StackPanel collected=new();
    readonly Dictionary<int,(Border Card,Grid Gauge,TextBlock Name,TextBlock Count)> bars=new();
    PetGatheringRow[] lastCollected=[];
    internal bool HasPets=>lastCollected.Length>0;
    internal PetTrackerOverlayWindow(Preferences settings, Action save, Action<int> map, Action details)
    {
        Title = global::AionDpsMeter.Local.Localization.T("Wingbeat pet tracker"); Width = 350; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        UiTheme.Apply(this, settings); Background = Brushes.Transparent;
        var body = new StackPanel { Margin = new(12) };
        body.Children.Add(collected);
        body.MouseLeftButtonDown+=(_,e)=>{if(!settings.PetTrackerLocked){DragMove();e.Handled=true;}};
        Content=new Border{Child=body,Background=Brushes.Transparent};
        if (settings.PetTrackerLeft is double left && settings.PetTrackerTop is double top && double.IsFinite(left) && double.IsFinite(top)) {
            Left = Math.Clamp(left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + Math.Max(0, SystemParameters.VirtualScreenWidth - Width));
            Top = Math.Clamp(top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + Math.Max(0, SystemParameters.VirtualScreenHeight - 400));
        }
        var menu = UiTheme.Menu(this);
        var automatic = new MenuItem { Header = Localization.T("Pet farming overlay"), IsCheckable = true, IsChecked = settings.PetGatheringAutoShow };
        automatic.Click += (_, _) => { settings.PetGatheringAutoShow = automatic.IsChecked; save(); }; menu.Items.Add(automatic);
        var locked = new MenuItem { Header = "Lock position", IsCheckable = true, IsChecked = settings.PetTrackerLocked };
        locked.Click += (_, _) => { settings.PetTrackerLocked = locked.IsChecked; save(); }; menu.Items.Add(locked);
        var phases = new List<MenuItem>();
        for (int goal = 1; goal <= 3; goal++) {
            int selectedGoal = goal;
            var item = new MenuItem { Header = goal == 1 ? "Phase 1: collect" : $"Phase {goal}: level {goal}", IsCheckable = true,
                IsChecked = Math.Clamp(settings.PetTrackerGoal, 1, 3) == goal };
            phases.Add(item); menu.Items.Add(item);
            item.Click += (_, _) => { settings.PetTrackerGoal = selectedGoal; foreach (var phase in phases) phase.IsChecked = ReferenceEquals(phase, item); save(); };
        }
        menu.Opened += (_, _) => { for (int i = 0; i < phases.Count; i++) phases[i].IsChecked = Math.Clamp(settings.PetTrackerGoal, 1, 3) == i + 1; };
        var all = new MenuItem { Header = "Open nearby pet farming…" }; all.Click += (_, _) => details(); menu.Items.Add(all);
        var close = new MenuItem { Header = "Close pet tracker" }; close.Click += (_, _) => Close(); menu.Items.Add(close); ContextMenu = menu;
        OverlayPlacement.Track(this, () => { settings.PetTrackerLeft = Left; settings.PetTrackerTop = Top; save(); });
    }
    internal void UpdateCollected(PetGatheringRow[] pets)
    {
        if(lastCollected.SequenceEqual(pets))return;lastCollected=pets;
        var ids=pets.Select(p=>p.Id).ToHashSet();
        foreach(int id in bars.Keys.Where(id=>!ids.Contains(id)).ToArray()){collected.Children.Remove(bars[id].Card);bars.Remove(id);}
        int index=0;
        foreach(var pet in pets){
            if(!bars.TryGetValue(pet.Id,out var row)){
                var gauge=new Grid{Height=32,Background=UiTheme.Brush("#60232D3B")};RoundedBar.Clip(gauge);
                gauge.ColumnDefinitions.Add(new());gauge.ColumnDefinitions.Add(new());
                gauge.Children.Add(new Border{Background=UiTheme.Brush("#55D6BE"),Opacity=.5,CornerRadius=new(6)});
                var labels=new Grid{Margin=new(8,0,8,0)};Grid.SetColumnSpan(labels,2);labels.ColumnDefinitions.Add(new());labels.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
                var name=new TextBlock{Foreground=Brushes.White,FontSize=12,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new(0,0,8,0)};
                var count=new TextBlock{Foreground=Brushes.White,FontSize=12,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center};
                labels.Children.Add(name);Grid.SetColumn(count,1);labels.Children.Add(count);gauge.Children.Add(labels);
                var card=new Border{Child=gauge,CornerRadius=new(6),Margin=new(0,2,0,2)};
                row=(card,gauge,name,count);bars[pet.Id]=row;
            }
            row.Name.Text=pet.Name;
            row.Count.Text=pet.MaxLevel?global::AionDpsMeter.Local.Localization.T("Max level"):$"{(pet.Held is int h?h.ToString("N0"):"?")} / {(pet.Required is int r?r.ToString("N0"):"?")}";
            double fraction=pet.MaxLevel?1:pet.Held is int held&&pet.Required is >0?Math.Clamp((double)held/pet.Required.Value,0,1):0;
            OverlayMotion.Column(row.Gauge.ColumnDefinitions[0],fraction,IsVisible);OverlayMotion.Column(row.Gauge.ColumnDefinitions[1],1-fraction,IsVisible);
            row.Card.ToolTip=pet.Progress;
            if(collected.Children.IndexOf(row.Card)!=index){collected.Children.Remove(row.Card);collected.Children.Insert(index,row.Card);}index++;
        }
    }
    internal void Update(IReadOnlyList<NearbyPet> pets,CapturedPosition? position,long now,bool capturing,int goal) { }
}
