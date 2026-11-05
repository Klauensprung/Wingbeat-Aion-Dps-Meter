using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AionDpsMeter.Local;

internal sealed record PersonalSkill(int Code, long Amount, double Rate, double Share, double Fraction, int Hits, double Crit)
{
    public long SummonAmount { get; init; }
    public string SummonSources { get; init; } = "";
}
internal static class PersonalSkills
{
    internal static PersonalSkill[] Build(Fight fight, bool healing)
    {
        if (fight.LocalPlayer <= 0) return [];
        var groups = (healing ? fight.Healing : fight.Damage).Where(h => h.ActorId == fight.LocalPlayer)
            .GroupBy(h => h.SkillCode).Select(g => new { Code = g.Key, Amount = g.Sum(h => (long)h.Damage), Hits = g.Count(), Crits = g.Count(h => h.IsCritical),
                SummonAmount = g.Where(h => h.SourceActorId > 0).Sum(h => h.Damage),
                SummonSources = string.Join(", ", g.Where(h => h.SourceActorId > 0).Select(h => fight.Name(h.SourceActorId)).Distinct().Order()) })
            .OrderByDescending(g => g.Amount).ThenBy(g => g.Code).ToArray();
        long total = groups.Sum(g => g.Amount), maximum = groups.FirstOrDefault()?.Amount ?? 0;
        double seconds = fight.Seconds;
        return groups.Select(g => new PersonalSkill(g.Code, g.Amount, g.Amount / seconds, total == 0 ? 0 : 100d * g.Amount / total,
            maximum == 0 ? 0 : (double)g.Amount / maximum, g.Hits, 100d * g.Crits / g.Hits)
                { SummonAmount = g.SummonAmount, SummonSources = g.SummonSources }).ToArray();
    }
}

internal sealed class SkillBreakdownView(Func<int, string> name, SkillIcons? icons) : AnimatedStackPanel
{
    readonly Dictionary<int, SkillBar> bars = new();
    readonly TextBlock empty = new() { Foreground = UiTheme.Brush("#A5B2C5"), TextWrapping = TextWrapping.Wrap, Margin = new(16, 26, 16, 26) };
    internal int RowCount => bars.Count;
    internal void Update(PersonalSkill[] rows, bool healing, bool compact, string accent, bool knownLocal, string? emptyText = null)
    {
        if (rows.Length == 0) {
            Children.Clear(); bars.Clear();
            empty.Text = !knownLocal ? "Waiting for your character identity. Enter a zone while capturing to identify your character."
                : emptyText ?? (healing ? "Your healing skills will appear here when you heal." : "Your skills will appear here when you deal damage.");
            Children.Add(empty); return;
        }
        Children.Remove(empty);
        var active = rows.Select(r => r.Code).ToHashSet();
        foreach (int code in bars.Keys.Where(k => !active.Contains(k)).ToArray()) { Children.Remove(bars[code]); bars.Remove(code); }
        for (int i = 0; i < rows.Length; i++) {
            var row = rows[i];
            if (!bars.TryGetValue(row.Code, out var bar)) { bars[row.Code] = bar = new SkillBar(); }
            if (Children.IndexOf(bar) != i) { Children.Remove(bar); Children.Insert(i, bar); }
            bar.Update(row, name(row.Code), icons?.Find(row.Code), healing, compact, accent);
        }
    }
    sealed class SkillBar : Border
    {
        readonly Image icon=new(){Width=28,Height=28,Margin=new(4,2,8,2),Stretch=Stretch.Uniform};
        readonly TextBlock title=new(){FontSize=11,FontWeight=FontWeights.SemiBold,Foreground=Brushes.White,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};
        readonly TextBlock values=new(){FontSize=11,FontWeight=FontWeights.Bold,Foreground=Brushes.White,Margin=new(8,0,8,0),VerticalAlignment=VerticalAlignment.Center};
        readonly Grid gauge=new(){Background=UiTheme.Brush("#80243044")};
        readonly Border fill=new(){Opacity=.45,CornerRadius=new(6)};
        (PersonalSkill Row,string Name,ImageSource? Icon,bool Healing,bool Compact,string Accent)? previous;
        internal SkillBar()
        {
            CornerRadius=new(7);Margin=new(0,0,0,3);Background=Brushes.Transparent;
            var layout=new Grid();layout.ColumnDefinitions.Add(new(){Width=GridLength.Auto});layout.ColumnDefinitions.Add(new());
            layout.Children.Add(icon);Grid.SetColumn(gauge,1);layout.Children.Add(gauge);Child=layout;
            RoundedBar.Clip(gauge);gauge.ColumnDefinitions.Add(new());gauge.ColumnDefinitions.Add(new());gauge.Children.Add(fill);
            var labels=new Grid{Margin=new(7,0,0,0)};Grid.SetColumnSpan(labels,2);
            labels.ColumnDefinitions.Add(new());labels.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            labels.Children.Add(title);Grid.SetColumn(values,1);labels.Children.Add(values);gauge.Children.Add(labels);
        }
        internal void Update(PersonalSkill row,string name,ImageSource? source,bool healing,bool compact,string accent)
        {
            var next=(row,name,source,healing,compact,accent);if(previous==next)return;
            string label=row.SummonAmount>0&&row.SummonAmount==row.Amount?name+" (summon)":name;
            if(title.Text!=label)title.Text=label;
            var image=source??SkillIcons.Placeholder;if(icon.Source!=image)icon.Source=image;
            string text=$"{MeterPresentation.Number(row.Amount)} | {row.Share:0.0}% | {MeterPresentation.Number(row.Rate)} {(healing?"HPS":"DPS")}";
            if(values.Text!=text)values.Text=text;
            double fraction=Math.Clamp(row.Share/100,0,1);
            OverlayMotion.Column(gauge.ColumnDefinitions[0],fraction,IsVisible);
            OverlayMotion.Column(gauge.ColumnDefinitions[1],1-fraction,IsVisible);
            if(previous==null||previous.Value.Accent!=accent)fill.Background=UiTheme.Brush(accent);
            Height=compact?34:38;
            ToolTip=$"{name}\n{row.Amount:N0} total | {row.Rate:N1} {(healing?"HPS":"DPS")}\n{row.Hits:N0} hits | {row.Crit:0.0}% critical\n{row.Share:0.0}% of your {(healing?"healing":"damage")}";
            if(row.SummonSources.Length>0)ToolTip+=$"\nSummons: {row.SummonSources}\n{row.SummonAmount:N0} from summons; {row.Amount-row.SummonAmount:N0} directly";
            System.Windows.Automation.AutomationProperties.SetName(this,$"{label}, {text}");
            previous=next;
        }
    }
}
