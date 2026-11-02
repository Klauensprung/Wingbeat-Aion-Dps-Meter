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

internal sealed class SkillBreakdownView(Func<int, string> name, SkillIcons? icons) : StackPanel
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
        readonly Image icon = new() { Width = 20, Height = 20, Margin = new(0, 0, 6, 0) };
        readonly TextBlock title = new() { FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = UiTheme.Brush("#EFF4FC"), TextTrimming = TextTrimming.CharacterEllipsis };
        readonly TextBlock rate = new() { FontSize = 11, FontWeight = FontWeights.Bold, Foreground = UiTheme.Brush("#EFF4FC"), Margin = new(6, 0, 0, 0) };
        readonly TextBlock amount = new() { FontSize = 9, Foreground = UiTheme.Brush("#A5B2C5") };
        readonly ProgressBar progress = new() { Height = 2, Minimum = 0, Maximum = 1, BorderThickness = new(0), Background = UiTheme.Brush("#243044"), Margin = new(0, 1, 0, 0) };
        internal SkillBar()
        {
            CornerRadius = new(3); Padding = new(6, 3, 6, 3); Margin = new(0, 0, 0, 2);
            Background = UiTheme.Brush("#D916202E");
            var layout = new DockPanel(); DockPanel.SetDock(icon, Dock.Left); layout.Children.Add(icon);
            var body = new StackPanel(); var heading = new DockPanel(); DockPanel.SetDock(rate, Dock.Right);
            heading.Children.Add(rate); heading.Children.Add(title); body.Children.Add(heading); body.Children.Add(amount); body.Children.Add(progress);
            layout.Children.Add(body); Child = layout;
        }
        internal void Update(PersonalSkill row, string name, ImageSource? source, bool healing, bool compact, string accent)
        {
            title.Text = row.SummonAmount > 0 && row.SummonAmount == row.Amount ? name + " (summon)" : name; icon.Source = source ?? SkillIcons.Placeholder;
            rate.Text = $"{MeterPresentation.Number(row.Rate)} {(healing ? "HPS" : "DPS")}";
            amount.Text = $"{MeterPresentation.Number(row.Amount)} {(healing ? "healing" : "damage")}   ·   {row.Share:0.0}%   ·   {row.Hits:N0} hits";
            progress.Value = row.Fraction; progress.Foreground = UiTheme.Brush(accent);
            if (row.SummonAmount > 0 && row.SummonAmount < row.Amount) amount.Text += $"   ({MeterPresentation.Number(row.SummonAmount)} via summons)";
            Padding = compact ? new(6, 1, 6, 1) : new(6, 3, 6, 3);
            ToolTip = $"{name}\n{row.Amount:N0} total · {row.Rate:N1} {(healing ? "HPS" : "DPS")}\n{row.Hits:N0} hits · {row.Crit:0.0}% critical\n{row.Share:0.0}% of your {(healing ? "healing" : "damage")}";
            if (row.SummonSources.Length > 0) ToolTip += $"\nSummons: {row.SummonSources}\n{row.SummonAmount:N0} from summons; {row.Amount - row.SummonAmount:N0} directly";
            System.Windows.Automation.AutomationProperties.SetName(this, $"{title.Text}, {rate.Text}, {amount.Text}");
        }
    }
}
