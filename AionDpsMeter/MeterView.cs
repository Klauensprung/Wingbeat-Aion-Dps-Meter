using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AionDpsMeter.Local;

public sealed record MeterRow(int Id, string Name, string Job, long Amount, double Rate, double Share, double Fraction, string Color, bool Local, bool Player)
{
    public string Detail { get; init; } = "";
    public int CombatPower { get; init; }
    public string CombatPowerSource { get; init; } = "Unknown";
    public int? LocalPingMs { get; init; }
}
public sealed record MeterData(MeterRow[] Rows, long Amount, double Rate, string Scope);

public static class MeterPresentation
{
    static readonly Dictionary<string, string> ClassColors = new(StringComparer.OrdinalIgnoreCase) {
        ["Gladiator"] = "#C79C6E", ["Templar"] = "#F0C75E", ["Assassin"] = "#AD8BF0", ["Ranger"] = "#91CC69",
        ["Sorcerer"] = "#69CCF0", ["Spiritmaster"] = "#E389C0", ["Cleric"] = "#DFE8F2", ["Chanter"] = "#56D5B1", ["Brawler"] = "#EF8B64"
    };
    static readonly string[] PlayerColors = ["#65CBE9", "#AE91EF", "#65D6AF", "#EABF70", "#EA8EA7", "#82B1F0", "#B5CE75", "#DA9CE9"];
    public static string ClassColorFor(string job, string fallback) => ClassColors.GetValueOrDefault(job, fallback);
    public static string ColorFor(Actor? actor)
    {
        if (actor != null && ClassColors.TryGetValue(actor.Job, out var color)) return color;
        if (actor?.Player != true) return "#8392A5";
        uint hash = 2166136261;
        foreach (char c in actor.Name) hash = unchecked((hash ^ c) * 16777619);
        return PlayerColors[hash % PlayerColors.Length];
    }
    public static MeterData Build(Fight fight, TotalRow[] totals, bool healing = false, bool allActors = false, int limit = 10, bool pinLocal = false)
    {
        IEnumerable<TotalRow> selected = totals;
        string scope = "All actors";
        bool party = fight.PartyKnown || fight.Actors.Values.Any(a => a.Party);
        if (!allActors)
        {
            selected = totals.Where(r => party ? fight.Actors.GetValueOrDefault(r.Id)?.Party == true || r.Id == fight.LocalPlayer : fight.Actors.GetValueOrDefault(r.Id)?.Player == true || r.Id == fight.LocalPlayer);
            scope = party ? "Party" : "Players";
        }
        long Value(TotalRow r) => healing ? r.Healing : r.Damage;
        var sorted = selected.Where(r => Value(r) > 0 || (!allActors && party && FightMetadata.InParty(fight, r.Id)))
            .OrderByDescending(Value).ThenBy(r => r.Id).ToArray();
        long total = sorted.Sum(Value), maximum = sorted.Length == 0 ? 0 : Value(sorted[0]);
        IEnumerable<TotalRow> visible = sorted;
        if (pinLocal && fight.LocalPlayer > 0)
        {
            var actor = fight.Actors.GetValueOrDefault(fight.LocalPlayer);
            var own = totals.FirstOrDefault(r => r.Id == fight.LocalPlayer)
                ?? new TotalRow(fight.LocalPlayer, fight.Name(fight.LocalPlayer), "Player", actor?.Cp ?? 0, 0, 0, 0, 0, 0);
            // Reserve the first slot before the row limit, including at zero.
            // Shares and bar lengths still use the entire scope and its leader.
            visible = new[] { own }.Concat(sorted.Where(r => r.Id != fight.LocalPlayer));
        }
        double seconds = fight.Seconds;
        var inferredClasses = ClassEvidence.Infer(fight);
        var rows = visible.Take(Math.Clamp(limit, 1, 50)).Select(r => {
            var actor = fight.Actors.GetValueOrDefault(r.Id); long amount = Value(r);
            string job = ClassEvidence.Resolve(actor, r.Id, inferredClasses);
            return new MeterRow(r.Id, r.Character, job, amount, amount / seconds,
                total == 0 ? 0 : 100d * amount / total, maximum == 0 ? 0 : (double)amount / maximum, ClassColorFor(job, ColorFor(actor)), r.Id == fight.LocalPlayer, actor?.Player == true) {
                    CombatPower = actor?.Cp ?? 0, CombatPowerSource = actor?.CpSource ?? "Unknown", LocalPingMs = PlayerTelemetry.PingFor(fight, r.Id)
                };
        }).ToArray();
        return new(rows, total, total / seconds, scope);
    }
    public static string Number(double value) => value switch {
        >= 1000000000 => (value / 1000000000).ToString("0.0", CultureInfo.CurrentCulture) + "B",
        >= 1000000 => (value / 1000000).ToString("0.00", CultureInfo.CurrentCulture) + "M",
        >= 1000 => (value / 1000).ToString("0.0", CultureInfo.CurrentCulture) + "K",
        _ => value.ToString("N0", CultureInfo.CurrentCulture)
    };
    public static string Duration(Fight fight) => Duration(fight.HasEvents ? fight.Seconds : 0);
    public static string Duration(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
    }
}

// The same ranked bars serve the overview and overlay. Existing rows are reused
// across capture ticks, retaining hover/click targets while their values change.
public sealed class MeterView : StackPanel
{
    readonly Dictionary<int, MeterBar> bars = new();
    readonly TextBlock empty = new() { Text = "Ready for your next fight", FontSize = 15, Foreground = UiTheme.Brush("#95A3B7"),
        TextAlignment = TextAlignment.Center, Margin = new(16, 42, 16, 42), TextWrapping = TextWrapping.Wrap };
    public event Action<int>? ActorSelected;
    public int RowCount => bars.Count;
    public Brush RowBackground { get; init; } = UiTheme.Brush("#1A2230");
    public void Update(IReadOnlyList<MeterRow> rows, bool compact = false, bool healing = false, bool hasEvents = false, string? emptyText = null, int startPosition = 1, bool showClassSymbols = false)
    {
        if (rows.Count == 0)
        {
            bars.Clear(); Children.Clear();
            empty.Text = emptyText ?? (hasEvents ? healing ? "No healing recorded" : "Waiting for player names\nCapture while entering a zone to refresh names." : "Ready for your next fight");
            Children.Add(empty); return;
        }
        Children.Remove(empty);
        var active = rows.Select(r => r.Id).ToHashSet();
        foreach (int id in bars.Keys.Where(id => !active.Contains(id)).ToArray()) { Children.Remove(bars[id]); bars.Remove(id); }
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (!bars.TryGetValue(row.Id, out var bar)) { bar = new MeterBar { Background = RowBackground }; bar.Selected += id => ActorSelected?.Invoke(id); bars.Add(row.Id, bar); }
            if (Children.IndexOf(bar) != i) { Children.Remove(bar); Children.Insert(i, bar); }
            bar.Update(row, i + startPosition, compact, healing, showClassSymbols);
        }
    }
}

sealed class MeterBar : Border
{
    readonly Grid canvas = new() { ClipToBounds = true };
    readonly Border fill = new() { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new(3), IsHitTestVisible = false };
    readonly Border stripe = new() { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
    readonly TextBlock rank = new() { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock name = new() { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock job = new() { FontSize = 10, Margin = new(0, 2, 0, 0) };
    readonly TextBlock metadata = new() { FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(0, 2, 0, 0) };
    readonly TextBlock rate = new() { FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
    readonly System.Windows.Shapes.Path classSymbol = new() { Width = 14, Height = 14, Stretch = Stretch.Uniform, StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round, Margin = new(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock amount = new() { FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right };
    MeterRow? current;
    public event Action<int>? Selected;
    public MeterBar()
    {
        CornerRadius = new(4); Margin = new(0, 0, 0, 4);
        Cursor = Cursors.Hand; Child = canvas; canvas.Children.Add(fill); canvas.Children.Add(stripe);
        var content = new Grid { Margin = new(3, 0, 10, 0) };
        content.ColumnDefinitions.Add(new() { Width = new GridLength(29) });
        content.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var identity = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new(3, 0, 10, 0) };
        identity.Children.Add(name); identity.Children.Add(job); identity.Children.Add(metadata);
        var values = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MinWidth = 91 };
        var rateLine = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        rateLine.Children.Add(classSymbol); rateLine.Children.Add(rate);
        values.Children.Add(rateLine); values.Children.Add(amount);
        content.Children.Add(rank); Grid.SetColumn(identity, 1); content.Children.Add(identity); Grid.SetColumn(values, 2); content.Children.Add(values);
        canvas.Children.Add(content);
        SizeChanged += (_, _) => SetWidth();
        MouseEnter += (_, _) => BorderBrush = UiTheme.Brush("#71819A");
        MouseLeave += (_, _) => BorderBrush = current?.Local == true ? UiTheme.Brush(current.Color) : Brushes.Transparent;
        BorderThickness = new(1);
        MouseLeftButtonUp += (_, e) => { if (current != null) Selected?.Invoke(current.Id); e.Handled = true; };
    }
    void SetWidth() => fill.Width = Math.Max(0, canvas.ActualWidth * (current?.Fraction ?? 0));
    public void Update(MeterRow row, int position, bool compact, bool healing, bool showClassSymbols)
    {
        if (current?.Job != row.Job) {
            classSymbol.Data = ClassSymbols.For(row.Job);
            classSymbol.Stroke = UiTheme.Brush(MeterPresentation.ClassColorFor(row.Job, "#CAD4E3"));
            classSymbol.ToolTip = row.Job == "Unknown" ? "Class not known yet" : row.Job;
            System.Windows.Automation.AutomationProperties.SetName(classSymbol, classSymbol.ToolTip.ToString());
        }
        classSymbol.Visibility = showClassSymbols ? Visibility.Visible : Visibility.Collapsed;
        if (current?.Color != row.Color)
        {
            var color = (Color)ColorConverter.ConvertFromString(row.Color);
            var gradient = new LinearGradientBrush(Color.FromArgb(130, color.R, color.G, color.B), Color.FromArgb(55, color.R, color.G, color.B), 0);
            gradient.Freeze(); fill.Background = gradient; stripe.Background = UiTheme.Brush(row.Color);
        }
        bool showMetadata = showClassSymbols || row.CombatPower > 0;
        current = row; Height = (compact ? 35 : 48) + (showMetadata ? 12 : 0);
        metadata.Visibility = showMetadata ? Visibility.Visible : Visibility.Collapsed;
        metadata.Text = "CP " + (row.CombatPower > 0 ? row.CombatPower.ToString("N0") : "--") +
            (showClassSymbols ? "  |  Ping " + (row.LocalPingMs is int ping ? $"~{ping} ms" : "--") : "");
        metadata.Foreground = UiTheme.Brush("#CED5E1");
        metadata.ToolTip = (row.CombatPower > 0 ? $"Combat power: {row.CombatPower:N0} ({row.CombatPowerSource})" : "Combat power not captured. Party profile packets can supply it; character details also allow manual entry.") +
            "\n" + (row.Local ? "Ping is the last local client-clock echo estimate, not other players' latency." : "This capture does not provide this player's ping in milliseconds.");
        BorderBrush = row.Local ? UiTheme.Brush(row.Color) : Brushes.Transparent;
        rank.Text = position.ToString(); rank.Foreground = UiTheme.Brush("#CED5E1");
        name.Text = row.Name + (row.Local ? "  ·  you" : ""); name.FontSize = compact ? 12 : 14; name.Foreground = Brushes.White;
        job.Text = row.Job == "Unknown" ? row.Player ? "Player" : "Unclassified actor" : row.Job; job.Foreground = UiTheme.Brush("#CAD4E3"); job.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        rate.Text = MeterPresentation.Number(row.Rate) + (healing ? " HPS" : " DPS"); rate.FontSize = compact ? 12 : 15; rate.Foreground = Brushes.White;
        amount.Text = $"{MeterPresentation.Number(row.Amount)}  ·  {row.Share:0.0}%"; amount.Foreground = UiTheme.Brush("#CED5E1");
        ToolTip = $"{row.Name}\n{(row.Job == "Unknown" ? row.Player ? "Class not captured; consistent player colour" : "Unclassified actor" : row.Job)}\n{(row.Detail.Length == 0 ? "" : row.Detail + "\n")}{row.Amount:N0} {(healing ? "healing" : "damage")} · {row.Rate:N1} {(healing ? "HPS" : "DPS")}\n{row.Share:0.0}% of { (healing ? "healing" : "damage") } in this view\nClick for skill details";
        SetWidth();
    }
}
