using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AionDpsMeter.Local;

internal sealed record TrackedBuff(int Id, string Name, string Time, bool Missing)
{
    internal int SourceSkill { get; init; }
    internal double? Seconds { get; init; }
}
internal static class BuffTracking
{
    internal static string Style(string? style) => style is "Square" or "Round" ? style : "Bars";
    internal static TrackedBuff[] Rows(IEnumerable<int> selected, LiveEffect[] active, bool capturing) => BuffChoice.Group(selected).Take(24).Select(choice => {
        int id = choice.Id;
        var matches = active.Where(e => BuffChoice.Resolve(e.EffectId).Id == id).ToArray();
        string time = !capturing ? "Start capture" : matches.Length == 0 ? "Check buff" : matches.Any(e => e.UntilRemoved) ? "Active" :
            matches.Any(e => e.RemainingSeconds == null) ? "Active · ?" : Math.Ceiling(matches.Max(e => e.RemainingSeconds)!.Value) + "s";
        return new TrackedBuff(id, choice.Name, time, capturing && matches.Length == 0) {
            SourceSkill = matches.FirstOrDefault(e => e.SourceSkill > 0)?.SourceSkill ?? 0,
            Seconds = capturing && matches.Length > 0 && matches.All(e => !e.UntilRemoved && e.RemainingSeconds != null)
                ? matches.Max(e => e.RemainingSeconds) : null
        };
    }).ToArray();
}

internal sealed class TrackedBuffView : StackPanel
{
    sealed record Row(Border Card, Image Icon, TextBlock Name, TextBlock Status, TextBlock Time, ProgressBar Bar)
    {
        internal double Maximum;
        internal int Skill;
    }
    readonly Dictionary<int, Row> rows = new();
    string lastStyle = "";
    readonly Dictionary<string,int> iconSkills = new(StringComparer.OrdinalIgnoreCase);
    internal void Update(TrackedBuff[] buffs, SkillNames names, SkillIcons icons, string style = "Bars")
    {
        style = BuffTracking.Style(style);
        bool compact = style != "Bars";
        if (lastStyle != style || !rows.Keys.SequenceEqual(buffs.Select(b => b.Id))) {
            lastStyle = style;
            var previous = rows.ToDictionary();
            Children.Clear(); rows.Clear();
            Children.Add(new TextBlock { Text = compact ? global::AionDpsMeter.Local.Localization.T("BUFFS") : global::AionDpsMeter.Local.Localization.T("BUFF UPTIME"), Foreground = UiTheme.Brush("#93A2B8"), FontSize = 10, FontWeight = FontWeights.SemiBold, Margin = new(8, 9, 8, 6) });
            Panel tiles = compact ? new WrapPanel() : new StackPanel();
            Children.Add(tiles);
            foreach (var buff in buffs) {
                var body = new Grid { Height = 48 };
                var icon = new Image { Width = 34, Height = 34, Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(7,0,0,0) };
                var label = new TextBlock { Text = buff.Name, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
                var status = new TextBlock { FontSize = 10, Margin = new(0,3,0,0) };
                var labels = new StackPanel { Margin = new(50,0,78,0), VerticalAlignment = VerticalAlignment.Center };
                labels.Children.Add(label); labels.Children.Add(status);
                var time = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new(0,0,10,0) };
                var bar = new ProgressBar { Minimum=0, Maximum=1, Height=2, VerticalAlignment=VerticalAlignment.Bottom, BorderThickness=new(0), Background=Brushes.Transparent, Foreground=UiTheme.Brush("#55D6BE"), IsHitTestVisible=false };
                body.Children.Add(icon); body.Children.Add(labels); body.Children.Add(time); body.Children.Add(bar);
                var card = new Border { Background = UiTheme.Brush("#E0131A24"), BorderBrush = UiTheme.Brush("#303C49"), BorderThickness = new(1), CornerRadius = new(6), Margin = new(4,0,4,5), Child = body };
                if (compact) {
                    card.Width = 52; card.Height = 52; body.Height = 50;
                    card.CornerRadius = new(style == "Round" ? 26 : 4);
                    if (style == "Round") body.Clip = new EllipseGeometry(new Point(25,25),25,25);
                    icon.Width = 50; icon.Height = 50; icon.Margin = new(0);
                    labels.Visibility = Visibility.Collapsed;
                    time.HorizontalAlignment = HorizontalAlignment.Center; time.Margin = new(0);
                    time.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color=Colors.Black, BlurRadius=4, ShadowDepth=1, Opacity=1 };
                }
                rows[buff.Id] = new(card,icon,label,status,time,bar) { Maximum=previous.GetValueOrDefault(buff.Id)?.Maximum ?? 0, Skill=previous.GetValueOrDefault(buff.Id)?.Skill ?? 0 }; tiles.Children.Add(card);
            }
        }
        foreach (var buff in buffs) {
            var row = rows[buff.Id];
            if (buff.SourceSkill > 0) row.Skill = buff.SourceSkill;
            if (!iconSkills.TryGetValue(buff.Name, out int namedSkill)) {
                namedSkill = names.SearchCodes(buff.Name).FirstOrDefault(id => string.Equals(names.GetSkillName(id),buff.Name,StringComparison.OrdinalIgnoreCase) && icons.Find(id) != null);
                iconSkills[buff.Name] = namedSkill;
            }
            row.Icon.Source = icons.Find(row.Skill) ?? icons.Find(namedSkill) ?? SkillIcons.Placeholder;
            row.Icon.Opacity = buff.Missing ? .45 : 1;
            bool expiring = buff.Seconds is > 0 and <= 5;
            var accent = UiTheme.Brush(buff.Missing || expiring ? "#F0BA62" : "#55D6BE");
            row.Time.Foreground = accent; row.Status.Foreground = UiTheme.Brush("#93A2B8"); row.Bar.Foreground = accent;
            row.Status.Text = buff.Missing ? global::AionDpsMeter.Local.Localization.T("Not observed on you") : buff.Seconds != null ? global::AionDpsMeter.Local.Localization.T("Remaining uptime") : buff.Time == global::AionDpsMeter.Local.Localization.T("Start capture") ? global::AionDpsMeter.Local.Localization.T("Capture stopped") : global::AionDpsMeter.Local.Localization.T("Buff active");
            row.Time.FontSize = buff.Seconds != null ? 15 : 12;
            row.Time.Text = buff.Seconds is double seconds ? seconds >= 60 ? $"{(int)Math.Ceiling(seconds)/60}:{(int)Math.Ceiling(seconds)%60:00}" : global::AionDpsMeter.Local.Localization.F($"{Math.Ceiling(seconds)}s") : buff.Time;
            if (compact) {
                row.Time.FontSize = buff.Seconds != null ? 16 : 20;
                if (buff.Seconds == null) row.Time.Text = buff.Missing ? "!" : buff.Time == global::AionDpsMeter.Local.Localization.T("Active") ? "✓" : "?";
                row.Card.BorderBrush = accent;
            }
            row.Maximum = buff.Seconds is double remaining ? Math.Max(row.Maximum,remaining) : 0;
            row.Bar.Visibility = buff.Seconds != null ? Visibility.Visible : Visibility.Collapsed;
            CountdownBar.Update(row.Bar,CooldownPresentation.RemainingFraction(buff.Seconds,row.Maximum),buff.Seconds ?? 0);
            row.Card.ToolTip = buff.Name + global::AionDpsMeter.Local.Localization.T("\nRemaining effect duration on your character, separate from skill cooldown.\nCheck buff: no active matching effect captured; it may have expired, been removed, or not been captured yet.\nIcon identifies the source skill where available.");
        }
    }
}

internal sealed class BuffTrackingSettings : Window
{
    internal BuffTrackingSettings(Preferences settings, int[] selected, LiveEffect[] active, Action<int[]> save)
    {
        Title = global::AionDpsMeter.Local.Localization.T("Track buff uptime"); Width = 540; Height = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner; UiTheme.Apply(this, settings);
        var chosen = BuffChoice.Group(selected).Take(24).Select(b => b.Id).ToList();
        var panel = new DockPanel { Margin = new(16) }; Content = panel;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        header.Children.Add(new TextBlock { Text = global::AionDpsMeter.Local.Localization.T("Track up to 24 buffs on yourself. Cast your buffs first to find them at the top, or search by name. Timers use captured effect duration."), TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) });
        var search = new TextBox { ToolTip = global::AionDpsMeter.Local.Localization.T("Search buffs"), Margin = new(0, 0, 0, 10) }; header.Children.Add(search);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        var tracked = new ListBox { Height = 140, Margin = new(0, 8, 0, 8) }; footer.Children.Add(tracked);
        var remove = new Button { Content = global::AionDpsMeter.Local.Localization.T("Remove selected buff") }; footer.Children.Add(remove);
        var done = new Button { Content = global::AionDpsMeter.Local.Localization.T("Use selected buffs"), Margin = new(0, 8, 0, 0) }; footer.Children.Add(done);
        var add = new Button { Content = global::AionDpsMeter.Local.Localization.T("Track selected buff"), Margin = new(0, 8, 0, 0) }; DockPanel.SetDock(add, Dock.Bottom); panel.Children.Add(add);
        var available = new ListBox(); panel.Children.Add(available);
        BuffChoice Describe(int id) => BuffChoice.Resolve(id);
        void Reload() { tracked.ItemsSource = chosen.Select(Describe).ToArray(); add.IsEnabled = chosen.Count < 24; }
        void Search() {
            available.ItemsSource = BuffChoice.Search(active.Select(e => e.EffectId), search.Text);
        }
        add.Click += (_, _) => { if (chosen.Count < 24 && available.SelectedItem is BuffChoice c && !chosen.Contains(c.Id)) { chosen.Add(c.Id); Reload(); } };
        remove.Click += (_, _) => { if (tracked.SelectedItem is BuffChoice c) { chosen.Remove(c.Id); Reload(); } };
        done.Click += (_, _) => { save(chosen.ToArray()); Close(); };
        search.TextChanged += (_, _) => Search(); Reload(); Search();
    }
}
