using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AionDpsMeter.Local;

internal sealed class LiveEffectView : StackPanel
{
    sealed record Row(Border Container, Image Icon, TextBlock Name, TextBlock Time);
    readonly Dictionary<int, Row> rows = new();
    readonly TextBlock title = new() { Foreground = Brushes.White, FontSize = 11, Margin = new(7, 8, 7, 4) };
    internal LiveEffectView() { Children.Add(title); }
    internal void Update(LiveEffect[] effects, SkillNames names, SkillIcons icons)
    {
        title.Text = effects.Length == 0 ? "No captured effects on your character" : "Effects on your character";
        var ids = effects.Select(e => e.Slot).ToHashSet();
        foreach (int slot in rows.Keys.Where(k => !ids.Contains(k)).ToArray()) { Children.Remove(rows[slot].Container); rows.Remove(slot); }
        for (int i = 0; i < effects.Length; i++) {
            var effect = effects[i];
            if (!rows.TryGetValue(effect.Slot, out var row)) {
                var icon = new Image { Width = 28, Height = 28, Margin = new(0, 0, 8, 0) };
                var name = new TextBlock { FontSize = 11, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                var time = new TextBlock { FontSize = 13, Foreground = UiTheme.Brush("#E7CF7D"), VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 0, 0) };
                var body = new DockPanel(); DockPanel.SetDock(icon, Dock.Left); DockPanel.SetDock(time, Dock.Right);
                body.Children.Add(icon); body.Children.Add(time); body.Children.Add(name);
                var border = new Border { Child = body, Background = UiTheme.Brush("#D0131A24"), CornerRadius = new(5), Padding = new(7), Margin = new(4, 0, 4, 4) };
                rows[effect.Slot] = row = new(border, icon, name, time);
            }
            if (Children.IndexOf(row.Container) != i + 1) { Children.Remove(row.Container); Children.Insert(i + 1, row.Container); }
            row.Icon.Source = effect.SourceSkill > 0 ? icons.Find(effect.SourceSkill) ?? SkillIcons.Placeholder : SkillIcons.Placeholder;
            var definition = EffectCatalogue.Default.Find(effect.EffectId);
            row.Name.Text = definition?.Name ?? $"Effect {effect.EffectId}";
            row.Time.Text = effect.UntilRemoved ? "∞" : effect.RemainingSeconds is double seconds ? Math.Ceiling(seconds) + "s" : "?";
            row.Container.ToolTip = $"Effect {effect.EffectId} · slot {effect.Slot} · source entity {effect.CasterId}\n" +
                (effect.UntilRemoved ? "No finite duration reported; shown until removal." : effect.RemainingSeconds == null ? "Duration not captured; shown until removal." : "Remaining captured duration, corrected for packet age.") +
                $"\nType: {definition?.Kind ?? "Unknown"} (reference catalogue). Source skill: " +
                (effect.SourceSkill > 0 ? names.GetSkillName(effect.SourceSkill) : "Unknown") +
                "\nIcon identifies the source skill when captured. Stack count is not available.";
        }
    }
}
