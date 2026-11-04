using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AionDpsMeter.Local;

internal sealed record TrackedBuff(int Id, string Name, string Time, bool Missing);
internal static class BuffTracking
{
    internal static TrackedBuff[] Rows(IEnumerable<int> selected, LiveEffect[] active, bool capturing) => selected.Where(id => id > 0).Distinct().Take(24).Select(id => {
        var matches = active.Where(e => e.EffectId == id).ToArray();
        string time = !capturing ? "Start capture" : matches.Length == 0 ? "Check buff" : matches.Any(e => e.UntilRemoved) ? "Active" :
            matches.Any(e => e.RemainingSeconds == null) ? "Active · ?" : Math.Ceiling(matches.Max(e => e.RemainingSeconds)!.Value) + "s";
        return new TrackedBuff(id, EffectCatalogue.Default.Find(id)?.Name ?? $"Effect {id}", time, capturing && matches.Length == 0);
    }).ToArray();
}

internal sealed class TrackedBuffView : StackPanel
{
    readonly Dictionary<int, TextBlock> rows = new();
    internal void Update(TrackedBuff[] buffs)
    {
        if (!rows.Keys.SequenceEqual(buffs.Select(b => b.Id))) {
            Children.Clear(); rows.Clear();
            Children.Add(new TextBlock { Text = "Buff uptime", Foreground = Brushes.White, Margin = new(7, 8, 7, 4) });
            foreach (var buff in buffs) {
                var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(8), FontSize = 13 };
                rows[buff.Id] = text;
                Children.Add(new Border { Background = UiTheme.Brush("#D0131A24"), CornerRadius = new(5), Margin = new(4, 0, 4, 4), Child = text });
            }
        }
        foreach (var buff in buffs) {
            var text = rows[buff.Id]; text.Text = $"{buff.Name}  ·  {buff.Time}";
            text.Foreground = UiTheme.Brush(buff.Missing ? "#F0BA62" : "#55D6BE");
            text.ToolTip = "Remaining effect duration on your character, separate from skill cooldown.\nCheck buff: no active matching effect captured; it may have expired, been removed, or not been captured yet.";
        }
    }
}

internal sealed class BuffTrackingSettings : Window
{
    sealed record Choice(int Id, string Name) { public override string ToString() => $"{Name} ({Id})"; }
    internal BuffTrackingSettings(Preferences settings, int[] selected, LiveEffect[] active, Action<int[]> save)
    {
        Title = "Track buff uptime"; Width = 540; Height = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner; UiTheme.Apply(this, settings);
        var chosen = selected.Distinct().Take(24).ToList();
        var panel = new DockPanel { Margin = new(16) }; Content = panel;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "Track up to 24 buffs on yourself. Cast your buffs first to find them at the top, or search by name. Timers use captured effect duration.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) });
        var search = new TextBox { ToolTip = "Search buffs", Margin = new(0, 0, 0, 10) }; header.Children.Add(search);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        var tracked = new ListBox { Height = 140, Margin = new(0, 8, 0, 8) }; footer.Children.Add(tracked);
        var remove = new Button { Content = "Remove selected buff" }; footer.Children.Add(remove);
        var done = new Button { Content = "Use selected buffs", Margin = new(0, 8, 0, 0) }; footer.Children.Add(done);
        var add = new Button { Content = "Track selected buff", Margin = new(0, 8, 0, 0) }; DockPanel.SetDock(add, Dock.Bottom); panel.Children.Add(add);
        var available = new ListBox(); panel.Children.Add(available);
        Choice Describe(int id) => new(id, EffectCatalogue.Default.Find(id)?.Name ?? $"Effect {id}");
        void Reload() { tracked.ItemsSource = chosen.Select(Describe).ToArray(); add.IsEnabled = chosen.Count < 24; }
        void Search() {
            var current = active.Select(e => e.EffectId).Where(id => EffectCatalogue.Default.Find(id)?.Kind != "Debuff").ToHashSet();
            available.ItemsSource = current.Concat(EffectCatalogue.Default.Buffs.Select(e => e.Id)).Distinct().Select(Describe)
                .Where(c => c.ToString().Contains(search.Text, StringComparison.OrdinalIgnoreCase)).OrderByDescending(c => current.Contains(c.Id)).ThenBy(c => c.Name).Take(150).ToArray();
        }
        add.Click += (_, _) => { if (chosen.Count < 24 && available.SelectedItem is Choice c && !chosen.Contains(c.Id)) { chosen.Add(c.Id); Reload(); } };
        remove.Click += (_, _) => { if (tracked.SelectedItem is Choice c) { chosen.Remove(c.Id); Reload(); } };
        done.Click += (_, _) => { save(chosen.ToArray()); Close(); };
        search.TextChanged += (_, _) => Search(); Reload(); Search();
    }
}
