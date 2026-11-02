using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AionDpsMeter.Local;

internal sealed record PartyHealthRow(int Entity, string Name, double? Percent, string Health, string Debuffs)
{
    internal bool Critical => Percent is <= 25;
}

internal sealed class PartyHealthView : StackPanel
{
    sealed record Row(Border Card, Grid Gauge, Border Fill, TextBlock Name, TextBlock Health, TextBlock Debuffs);
    readonly Dictionary<int, Row> rows = new();
    readonly TextBlock empty = new() { Text="Waiting for your character and party…", Margin=new(12), TextWrapping=TextWrapping.Wrap };
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
    internal static PartyHealthRow[] Build(Fight fight, IReadOnlyDictionary<int,HealthReading> health, IEnumerable<PartyMemberDebuff> effects) {
        var debuffs=effects.GroupBy(e=>e.Target).ToDictionary(g=>g.Key,g=>string.Join("  ·  ",g.Select(e=>e.Name+(e.Seconds is double seconds?$" {Math.Ceiling(seconds):0}s":""))));
        bool roster=HasRoster(fight);
        // As in the DPS meter, use observed combat participants while the party
        // roster is unavailable. Do not populate this with every nearby spawn.
        var participants=roster?null:fight.Damage.Concat(fight.Healing).Select(e=>e.ActorId).ToHashSet();
        return fight.Actors.Values.Where(a=>a.Player&&a.OriginalEntityId==0&&(a.Id==fight.LocalPlayer||a.Party||!roster&&participants!.Contains(a.Id)))
            .OrderByDescending(a=>a.Id==fight.LocalPlayer).ThenBy(a=>a.Id).Take(64).Select(a=>{
                var hp=health.GetValueOrDefault(a.Id);
                double? percent=hp?.Value is long value && value>=0 && hp.Maximum is > 0 && value<=hp.Maximum ? value*100d/hp.Maximum.Value : hp?.Value==0?0:null;
                string label=percent is double p?(p==0?"Dead":$"{p:0.#}%"):"HP unknown";
                return new PartyHealthRow(a.Id,a.Name+(a.Id==fight.LocalPlayer?" (you)":""),percent,label,debuffs.GetValueOrDefault(a.Id,""));
            }).ToArray();
    }
    internal void Update(PartyHealthRow[] data,bool compact) {
        var ids=data.Select(r=>r.Entity).ToHashSet();
        foreach(int id in rows.Keys.Where(id=>!ids.Contains(id)).ToArray()){Children.Remove(rows[id].Card);rows.Remove(id);}
        Children.Remove(empty);
        for(int i=0;i<data.Length;i++){
            var item=data[i];
            if(!rows.TryGetValue(item.Entity,out var row)){
                var gauge=new Grid{MinHeight=34,Background=UiTheme.Brush("#80232D3B")};
                gauge.ColumnDefinitions.Add(new());gauge.ColumnDefinitions.Add(new());
                var fill=new Border();gauge.Children.Add(fill);
                var labels=new Grid{Margin=new(10,6,10,6)};Grid.SetColumnSpan(labels,2);
                labels.ColumnDefinitions.Add(new());labels.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
                var name=new TextBlock{Foreground=Brushes.White,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new(0,0,10,0)};
                var hp=new TextBlock{Foreground=Brushes.White,FontWeight=FontWeights.Bold};Grid.SetColumn(hp,1);
                labels.Children.Add(name);labels.Children.Add(hp);gauge.Children.Add(labels);
                var debuffs=new TextBlock{Foreground=UiTheme.Brush("#FFADB6"),TextWrapping=TextWrapping.Wrap,Margin=new(10,4,10,6),FontSize=11};
                var content=new StackPanel();content.Children.Add(gauge);content.Children.Add(debuffs);
                var card=new Border{Child=content,Background=UiTheme.Brush("#B010151F"),Margin=new(7,3,7,3),CornerRadius=new(4)};
                rows[item.Entity]=row=new(card,gauge,fill,name,hp,debuffs);Children.Add(card);
            }
            if(Children.IndexOf(row.Card)!=i){Children.Remove(row.Card);Children.Insert(i,row.Card);}
            double fraction=(item.Percent??0)/100;
            row.Gauge.ColumnDefinitions[0].Width=new(fraction,GridUnitType.Star);
            row.Gauge.ColumnDefinitions[1].Width=new(1-fraction,GridUnitType.Star);
            var color=HealthColor(item.Percent);
            var fillColor=Color.FromArgb(184,color.R,color.G,color.B);
            if(row.Fill.Background is not SolidColorBrush existing||existing.Color!=fillColor){
                var brush=new SolidColorBrush(fillColor);brush.Freeze();row.Fill.Background=brush;
            }
            row.Card.BorderThickness=new(1);row.Card.BorderBrush=item.Critical?UiTheme.Brush("#FF6975"):Brushes.Transparent;
            row.Health.Foreground=item.Critical?UiTheme.Brush("#FFADB6"):item.Percent is <=50?UiTheme.Brush("#F0C84B"):Brushes.White;
            row.Name.Text=item.Name;row.Health.Text=item.Health;row.Name.FontSize=row.Health.FontSize=compact?12:14;
            row.Debuffs.Text=item.Debuffs;row.Debuffs.Visibility=item.Debuffs.Length>0?Visibility.Visible:Visibility.Collapsed;
            row.Card.ToolTip="Last captured health. Debuffs appear only when observed; no text does not guarantee no debuffs.";
        }
        if(data.Length==0)Children.Add(empty);
    }
}
