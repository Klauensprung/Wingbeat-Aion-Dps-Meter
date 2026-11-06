using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AionDpsMeter.Local;

internal sealed record PartyHealthRow(int Entity, string Name, double? Percent, string Health, string Debuffs)
{
    internal string Job { get; init; } = "Unknown";
    internal double RecoveryPercent { get; init; }
    internal PartyRange Range { get; init; }
    internal bool Unavailable => Range is PartyRange.Far or PartyRange.OutOfView;
    internal string Proximity { get; init; } = "";
    internal bool Critical => Percent is <= 25;
}

internal sealed class PartyHealthView : AnimatedStackPanel
{
    sealed record Row(Border Card, Grid Gauge, Border Fill, Border RecoveryFill, TextBlock Name, Image ClassIcon, TextBlock Distance, TextBlock Health, TextBlock Debuffs);
    static readonly Brush NormalRecovery = UiTheme.Brush("#7095F5A6");
    static readonly Brush DebuffedRecovery = UiTheme.Brush("#7055A66F");
    readonly EncounterView.ClassIconConverter classIcons=new();
    readonly Dictionary<int, Row> rows = new();
    readonly Dictionary<int,(PartyHealthRow Row,bool Compact)> presented=new();
    readonly TextBlock empty = new() { Text=global::AionDpsMeter.Local.Localization.T("Waiting for your character and party…"), Margin=new(12), TextWrapping=TextWrapping.Wrap };
    internal int RowCount => rows.Count;
    internal static Color HealthColor(double? percent) {
        if (percent is not double hp || !double.IsFinite(hp)) return Color.FromRgb(131,146,165);
        var green=Color.FromRgb(85,191,160);
        var yellow=Color.FromRgb(240,200,75);
        var red=Color.FromRgb(229,72,77);
        if(hp>=75)return green;
        if(hp<=25)return red;
        var from=hp>=50?yellow:red;var to=hp>=50?green:yellow;
        double weight=hp>=50?(hp-50)/25:(hp-25)/25;
        byte Mix(byte a,byte b)=>(byte)Math.Round(a+(b-a)*weight);
        return Color.FromRgb(Mix(from.R,to.R),Mix(from.G,to.G),Mix(from.B,to.B));
    }
    internal static bool HasRoster(Fight fight) => fight.PartyKnown || fight.Actors.Values.Any(a=>a.Player&&a.OriginalEntityId==0&&a.Party&&a.Id!=fight.LocalPlayer);
    internal static PartyHealthRow[] Build(Fight fight, IReadOnlyDictionary<int,HealthReading> health, IEnumerable<PartyMemberDebuff> effects, IReadOnlyDictionary<int,double>? recovery=null) {
        var debuffs=effects.GroupBy(e=>e.Target).ToDictionary(g=>g.Key,g=>string.Join("  ·  ",g.Select(e=>e.Name+(e.Seconds is double seconds?$" {Math.Ceiling(seconds):0}s":""))));
        // Combat proximity is not evidence of party membership.
        return fight.Actors.Values.Where(a=>a.Player&&a.OriginalEntityId==0&&(a.Id==fight.LocalPlayer||a.Party))
            .OrderByDescending(a=>a.Id==fight.LocalPlayer).ThenBy(a=>a.Id).Take(64).Select(a=>{
                var hp=health.GetValueOrDefault(a.Id);
                double? percent=hp?.Value is long value && value>=0 && hp.Maximum is > 0 && value<=hp.Maximum ? value*100d/hp.Maximum.Value : hp?.Value==0?0:null;
                string label=percent is double p?(p==0?"Dead":$"{p:0.#}%"):"HP unknown";
                return new PartyHealthRow(a.Id,a.Name+(a.Id==fight.LocalPlayer?" (you)":""),percent,label,debuffs.GetValueOrDefault(a.Id,"")) { Job=a.Job, RecoveryPercent=percent is >0 and <100 && hp?.Maximum is >0 ? Math.Min(100-percent.Value,Math.Max(0,recovery?.GetValueOrDefault(a.Id)??0)*100/hp.Maximum.Value):0 };
            }).ToArray();
    }
    internal void Update(PartyHealthRow[] data,bool compact) {
        data=data.OrderBy(row=>row.Unavailable).ToArray();
        var ids=data.Select(r=>r.Entity).ToHashSet();
        foreach(int id in rows.Keys.Where(id=>!ids.Contains(id)).ToArray()){Children.Remove(rows[id].Card);rows.Remove(id);presented.Remove(id);}
        Children.Remove(empty);
        for(int i=0;i<data.Length;i++){
            var item=data[i];
            if(!rows.TryGetValue(item.Entity,out var row)){
                var gauge=new Grid{Height=34,Background=UiTheme.Brush("#80232D3B")};
                RoundedBar.Clip(gauge);
                gauge.ColumnDefinitions.Add(new());gauge.ColumnDefinitions.Add(new());gauge.ColumnDefinitions.Add(new());
                var fill=new Border{CornerRadius=new(6)};gauge.Children.Add(fill);
                var recoveryFill=new Border { Background=NormalRecovery,CornerRadius=new(0,6,6,0) };Grid.SetColumn(recoveryFill,1);gauge.Children.Add(recoveryFill);
                var labels=new Grid{Margin=new(10,6,10,6)};Grid.SetColumnSpan(labels,3);
                labels.ColumnDefinitions.Add(new());labels.ColumnDefinitions.Add(new(){Width=GridLength.Auto});labels.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
                var name=new TextBlock{Foreground=Brushes.White,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new(0,0,10,0)};
                var distance=new TextBlock{Foreground=UiTheme.Brush("#BCC3CC"),FontSize=10,VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,10,0)};Grid.SetColumn(distance,1);
                var hp=new TextBlock{Foreground=Brushes.White,FontWeight=FontWeights.Bold};Grid.SetColumn(hp,2);
                var identity=new DockPanel();
                var classIcon=new Image{Width=18,Height=18,Margin=new(0,0,6,0),VerticalAlignment=VerticalAlignment.Center};
                DockPanel.SetDock(classIcon,Dock.Left);identity.Children.Add(classIcon);identity.Children.Add(name);
                labels.Children.Add(identity);labels.Children.Add(distance);labels.Children.Add(hp);gauge.Children.Add(labels);
                var debuffs=new TextBlock{Foreground=UiTheme.Brush("#FFADB6"),TextWrapping=TextWrapping.Wrap,Margin=new(10,4,10,6),FontSize=11};
                var content=new StackPanel();content.Children.Add(gauge);content.Children.Add(debuffs);
                var card=new Border{Child=content,Background=UiTheme.Brush("#B010151F"),Margin=new(7,3,7,3),CornerRadius=new(8)};
                rows[item.Entity]=row=new(card,gauge,fill,recoveryFill,name,classIcon,distance,hp,debuffs);Children.Add(card);
            }
            if(Children.IndexOf(row.Card)!=i){Children.Remove(row.Card);Children.Insert(i,row.Card);}
            if(presented.TryGetValue(item.Entity,out var previous)&&previous==(item,compact))continue;
            presented[item.Entity]=(item,compact);
            if(previous.Row?.Job!=item.Job){
                row.ClassIcon.Source=classIcons.Convert(item.Job,typeof(ImageSource),null!,System.Globalization.CultureInfo.InvariantCulture) as ImageSource;
                row.ClassIcon.Visibility=row.ClassIcon.Source==null?Visibility.Collapsed:Visibility.Visible;
                row.ClassIcon.ToolTip=item.Job;
            }
            double fraction=(item.Percent??0)/100;
            OverlayMotion.Column(row.Gauge.ColumnDefinitions[0],fraction,IsVisible);
            double recoveryFraction=item.Unavailable?0:Math.Clamp(item.RecoveryPercent/100,0,1-fraction);
            OverlayMotion.Column(row.Gauge.ColumnDefinitions[1],recoveryFraction,IsVisible);
            OverlayMotion.Column(row.Gauge.ColumnDefinitions[2],Math.Max(0,1-fraction-recoveryFraction),IsVisible);
            bool debuffed=item.Debuffs.Length>0;
            var color=item.Unavailable?Color.FromRgb(112,121,133):debuffed?Color.FromRgb(32,112,65):HealthColor(item.Percent);
            row.RecoveryFill.Background=debuffed?DebuffedRecovery:NormalRecovery;
            var fillColor=Color.FromArgb(184,color.R,color.G,color.B);
            if(row.Fill.Background is not SolidColorBrush existing||existing.Color!=fillColor){
                var brush=new SolidColorBrush(fillColor);brush.Freeze();row.Fill.Background=brush;
            }
            row.Card.Opacity=item.Unavailable?.65:1;
            row.Card.BorderThickness=new(1);row.Card.BorderBrush=item.Critical&&!item.Unavailable?UiTheme.Brush("#FF6975"):Brushes.Transparent;
            row.Health.Foreground=item.Unavailable?UiTheme.Brush("#BCC3CC"):item.Critical?UiTheme.Brush("#FFADB6"):item.Percent is <=50?UiTheme.Brush("#F0C84B"):Brushes.White;
            row.Name.Text=item.Name;row.Health.Text=item.Health+(!item.Unavailable&&item.RecoveryPercent>0?$"  +{item.RecoveryPercent:0.#}%":"");row.Name.FontSize=row.Health.FontSize=compact?12:14;
            string distanceText=item.Range!=PartyRange.OutOfView&&item.Proximity.EndsWith(" units away",StringComparison.Ordinal)?item.Proximity.Replace(" units away"," units"):"";
            if(row.Distance.Text!=distanceText)row.Distance.Text=distanceText;
            row.Distance.Visibility=distanceText.Length>0?Visibility.Visible:Visibility.Collapsed;
            row.Debuffs.Text=item.Debuffs;row.Debuffs.Visibility=item.Debuffs.Length>0?Visibility.Visible:Visibility.Collapsed;
            row.Card.ToolTip=(item.Proximity.Length>0?item.Proximity+"\n":"")+global::AionDpsMeter.Local.Localization.T("Last captured health. Gray means the member left captured visibility or fresh positions exceed 4,000 world units; this is not a verified game UI flag or a guarantee of healing range/line of sight. Light green shows remaining regeneration reported by the server (up to missing HP), not guaranteed future healing. Hidden after five seconds without an update or when the buff ends. Distance uses 3D captured map coordinates, in game-world units, shows the age after 5 seconds and expires after 30 seconds. It does not confirm line of sight or skill range. Debuffs appear only when observed.");
        }
        if(data.Length==0)Children.Add(empty);
    }
}
