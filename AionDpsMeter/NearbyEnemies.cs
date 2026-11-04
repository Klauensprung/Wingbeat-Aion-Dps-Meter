using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace AionDpsMeter.Local;

internal sealed record NearbyEnemy(int Id, string Name, int Cp, double? Distance)
{
    internal double? HeightDifference { get; init; }
    internal string HeightArrow => HeightDifference is >200 ? "\u2191" : HeightDifference is < -200 ? "\u2193" : "";
    internal bool Tagged { get; init; }
    internal string Job { get; init; } = "Unknown";
    internal int LocalCp { get; init; }
    internal double? HealthPercent { get; init; }
}

internal static class NearbyEnemies
{
    internal static string? EffectiveRace(Actor? actor)=>actor?.OverrideRace switch {1=>"light",2=>"dark",null or 0=>actor?.Race,_=>null};
    internal static NearbyEnemy[] Build(Fight fight, SpatialEvidence spatial, CapturedPosition? local, long now)
    {
        string? race=EffectiveRace(fight.Actors.GetValueOrDefault(fight.LocalPlayer));
        if(race is not ("light" or "dark"))return [];
        var health=CapturedHealth.Build(fight);
        int localCp=fight.Actors.GetValueOrDefault(fight.LocalPlayer)?.Cp??0;
        var reused=fight.Actors.Values.Where(a=>a.OriginalEntityId>0).Select(a=>a.OriginalEntityId).ToHashSet();
        var tags=fight.Damage.Where(h=>h.ActorId==fight.LocalPlayer&&!h.IsHeal&&h.Damage>0&&h.Timestamp>=spatial.Boundary&&h.Timestamp<=now&&!reused.Contains(h.TargetId))
            .GroupBy(h=>h.TargetId).ToDictionary(g=>g.Key,g=>g.Max(h=>h.Timestamp));
        foreach(var death in fight.Deaths)if(tags.TryGetValue(death.EntityId,out var hitAt)&&death.Timestamp>=hitAt)tags.Remove(death.EntityId);
        foreach(var revival in fight.Network.Where(e=>e.Kind=="entity-revived"))
            if(revival.Fields.TryGetProperty("EntityId",out var id)&&id.TryGetInt32(out int entity)&&tags.TryGetValue(entity,out var hitAt)&&revival.Timestamp>=hitAt)tags.Remove(entity);
        bool Fresh(long at)=>at>=0&&at<=now&&now-at<=5000;
        return spatial.Entities.Where(e=>e.Visible&&e.Kind=="player").Select(e=>{
            var actor=fight.Actors.GetValueOrDefault(e.Id);
            if(actor is not {Player:true,OriginalEntityId:0}||actor.Id==fight.LocalPlayer||actor.Party||EffectiveRace(actor) is not ("light" or "dark")||EffectiveRace(actor)==race)return null;
            double? distance=null,heightDifference=null;
            if(local!=null&&local.MapId==spatial.MapId&&Fresh(local.Timestamp)&&Fresh(e.PositionAt)&&e.X is double x&&e.Y is double y&&e.Z is double z){
                double d=Math.Sqrt(Math.Pow(x-local.X,2)+Math.Pow(y-local.Y,2)+Math.Pow(z-local.Z,2));
                if(double.IsFinite(d)){distance=d;heightDifference=z-local.Z;}
            }
            var hp=health.GetValueOrDefault(actor.Id);
            double? percent=hp?.Value is long current&&hp.Maximum is >0&&current>=0&&current<=hp.Maximum
                ? current*100d/hp.Maximum.Value:null;
            return new NearbyEnemy(actor.Id,actor.Name,actor.Cp,distance){HeightDifference=heightDifference,Job=string.IsNullOrWhiteSpace(actor.Job)?"Unknown":actor.Job,LocalCp=localCp,HealthPercent=percent,Tagged=tags.ContainsKey(actor.Id)};
        }).OfType<NearbyEnemy>().OrderBy(e=>e.Distance??double.MaxValue).ThenBy(e=>e.Name).Take(32).ToArray();
    }
}

internal sealed class NearbyEnemiesOverlayWindow : Window
{
    readonly StackPanel list=new();
    readonly EncounterView.ClassIconConverter classIcons=new();
    NearbyEnemy[] previous=[];
    string previousEmpty="";
    internal NearbyEnemiesOverlayWindow(Preferences settings,Action save)
    {
        Title="Wingbeat nearby enemies";MinWidth=240;MinHeight=130;
        Width=double.IsFinite(settings.NearbyEnemiesWidth)?Math.Clamp(settings.NearbyEnemiesWidth,240,1200):330;
        Height=double.IsFinite(settings.NearbyEnemiesHeight)?Math.Clamp(settings.NearbyEnemiesHeight,130,1200):400;
        ResizeMode=settings.NearbyEnemiesLocked?ResizeMode.NoResize:ResizeMode.CanResizeWithGrip;
        WindowStyle=WindowStyle.None;AllowsTransparency=true;Topmost=true;ShowInTaskbar=false;ShowActivated=false;
        UiTheme.Apply(this,settings);Background=Brushes.Transparent;
        var content=new DockPanel{Margin=new(6)};
        var header=new StackPanel{Background=Brushes.Transparent};DockPanel.SetDock(header,Dock.Top);content.Children.Add(header);
        header.Children.Add(new TextBlock{Text="Nearby enemies",FontWeight=FontWeights.Bold,Foreground=UiTheme.Brush("#FFADB6"),Margin=new(0,0,0,6)});
        var resizeGrip=new ResizeGrip{Width=14,Height=14,HorizontalAlignment=HorizontalAlignment.Right,Opacity=.5,
            Visibility=settings.NearbyEnemiesLocked?Visibility.Collapsed:Visibility.Visible};
        DockPanel.SetDock(resizeGrip,Dock.Bottom);content.Children.Add(resizeGrip);
        content.Children.Add(new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        Content=new Border{Child=content,Background=Brushes.Transparent,CornerRadius=new(7)};
        if(settings.NearbyEnemiesLeft is double left&&settings.NearbyEnemiesTop is double top&&double.IsFinite(left)&&double.IsFinite(top)){
            Left=Math.Clamp(left,SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+Math.Max(0,SystemParameters.VirtualScreenWidth-Width));
            Top=Math.Clamp(top,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+Math.Max(0,SystemParameters.VirtualScreenHeight-Height));
        }
        header.MouseLeftButtonDown+=(_,e)=>{if(!settings.NearbyEnemiesLocked){DragMove();e.Handled=true;}};
        var menu=UiTheme.Menu(this);
        var locked=new MenuItem{Header="Lock position",IsCheckable=true,IsChecked=settings.NearbyEnemiesLocked};
        locked.Click+=(_,_)=>{settings.NearbyEnemiesLocked=locked.IsChecked;ResizeMode=locked.IsChecked?ResizeMode.NoResize:ResizeMode.CanResizeWithGrip;resizeGrip.Visibility=locked.IsChecked?Visibility.Collapsed:Visibility.Visible;save();};menu.Items.Add(locked);
        var close=new MenuItem{Header="Close nearby enemies"};close.Click+=(_,_)=>Close();menu.Items.Add(close);ContextMenu=menu;
        OverlayPlacement.Track(this,()=>{settings.NearbyEnemiesLeft=Left;settings.NearbyEnemiesTop=Top;settings.NearbyEnemiesWidth=Width;settings.NearbyEnemiesHeight=Height;save();});
    }
    internal void Update(NearbyEnemy[] enemies,string empty)
    {
        if(previous.SequenceEqual(enemies)&&previousEmpty==empty)return;
        previous=enemies;previousEmpty=empty;list.Children.Clear();
        if(enemies.Length==0)list.Children.Add(new TextBlock{Text=empty,Foreground=Brushes.White,TextWrapping=TextWrapping.Wrap});
        foreach(var enemy in enemies){
            var cpColor=UiTheme.Brush(MeterPresentation.CombatPowerColor(enemy.Cp,enemy.LocalCp));
            var classColor=UiTheme.Brush(MeterPresentation.ClassColorFor(enemy.Job,"#8392A5"));
            var row=new StackPanel{Margin=new(0,1,0,3)};
            var gauge=new Grid{Height=36,Background=UiTheme.Brush("#80232D3B")};
            double percent=enemy.HealthPercent??0;
            gauge.ColumnDefinitions.Add(new(){Width=new(percent,GridUnitType.Star)});
            gauge.ColumnDefinitions.Add(new(){Width=new(100-percent,GridUnitType.Star)});
            gauge.Children.Add(new Border{Background=classColor,Opacity=.55});
            var heading=new Grid{Margin=new(7,2,7,2)};Grid.SetColumnSpan(heading,2);
            heading.RowDefinitions.Add(new(){Height=new(18)});heading.RowDefinitions.Add(new(){Height=new(14)});
            heading.ColumnDefinitions.Add(new());heading.ColumnDefinitions.Add(new(){Width=GridLength.Auto});heading.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var identity=new DockPanel();
            if(ClassEvidence.Known(enemy.Job)){
                var icon=new Image{Source=classIcons.Convert(enemy.Job,typeof(ImageSource),null!,System.Globalization.CultureInfo.InvariantCulture) as ImageSource,
                    Width=16,Height=16,Margin=new(0,0,5,0),ToolTip=enemy.Job,VerticalAlignment=VerticalAlignment.Center};
                DockPanel.SetDock(icon,Dock.Left);identity.Children.Add(icon);
            }
            identity.Children.Add(new TextBlock{Text=enemy.Name,Foreground=Brushes.White,FontSize=12,FontWeight=FontWeights.SemiBold,Margin=new(0,0,8,0),TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center});
            heading.Children.Add(identity);
            var hpText=new TextBlock{Text=enemy.HealthPercent is double hp?$"{hp:0.#}%":"HP unknown",Foreground=Brushes.White,FontSize=12,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center};
            if(enemy.Tagged){
                var tagged=new TextBlock{Text="Tagged",Foreground=Brushes.White,FontSize=10,FontWeight=FontWeights.Bold,Margin=new(0,0,8,0),VerticalAlignment=VerticalAlignment.Center};
                Grid.SetColumn(tagged,1);heading.Children.Add(tagged);
            }
            Grid.SetColumn(hpText,2);heading.Children.Add(hpText);gauge.Children.Add(heading);
            row.Children.Add(gauge);
            string cpText=enemy.Cp>0?$"{enemy.Cp:N0} CP":"CP unknown";
            string distanceText=enemy.Distance is double d?$"{d:N0} units{(enemy.HeightArrow.Length>0?"  "+enemy.HeightArrow:"")}":"Distance unknown";
            var details=new TextBlock{Foreground=Brushes.White,FontSize=10,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};
            details.Inlines.Add(new System.Windows.Documents.Run(cpText){Foreground=cpColor,FontWeight=FontWeights.Bold});
            details.Inlines.Add(new System.Windows.Documents.Run("  |  "+distanceText));
            details.ToolTip=cpText+"  |  "+distanceText+(enemy.HeightDifference is double dz?$"\nHeight difference: {dz:+0;-0;0} world units. Arrows appear beyond 200 units above/below; positions must be fresh.":"");
            Grid.SetRow(details,1);Grid.SetColumnSpan(details,3);heading.Children.Add(details);
            row.ToolTip="Tagged means your damage was captured in this fight since the last observed death or revival; it does not confirm server assist credit. Last captured HP."+MeterPresentation.CombatPowerComparison(enemy.Cp,enemy.LocalCp);
            list.Children.Add(row);
        }
        ToolTip="Opposing-faction players currently in captured view. Distance is in game-world units and requires positions updated within five seconds. Faction does not establish attackability in safe zones or arena teams.";
    }
}

public sealed partial class MainWindow
{
    NearbyEnemiesOverlayWindow? nearbyEnemiesOverlay;
    void ToggleNearbyEnemies()
    {
        if(nearbyEnemiesOverlay!=null){nearbyEnemiesOverlay.Close();return;}
        var window=new NearbyEnemiesOverlayWindow(preferences,SavePreferences);nearbyEnemiesOverlay=window;
        window.Closed+=(_,_)=>{if(ReferenceEquals(nearbyEnemiesOverlay,window))nearbyEnemiesOverlay=null;};
        RefreshCooldownVisibility();
    }
    void RefreshNearbyEnemies(bool allowed)
    {
        if(nearbyEnemiesOverlay==null)return;
        NearbyEnemy[] rows=[];string empty="Waiting for capture";
        lock(engine.Gate){
            if(capture!=null&&engine.LiveIdentityReady){
                rows=NearbyEnemies.Build(engine.Fight,engine.Spatial,engine.PartyPosition.RelativeLatest,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                empty=NearbyEnemies.EffectiveRace(engine.Fight.Actors.GetValueOrDefault(engine.Fight.LocalPlayer)) is "light" or "dark"?"No enemy players in captured view":"Waiting for your faction";
            }
        }
        nearbyEnemiesOverlay.Update(rows,empty);
        if(allowed&&!nearbyEnemiesOverlay.IsVisible)nearbyEnemiesOverlay.Show();
        else if(!allowed&&nearbyEnemiesOverlay.IsVisible)nearbyEnemiesOverlay.Hide();
    }
}
