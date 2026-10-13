using System.Windows;
using System.Windows.Controls;

namespace AionDpsMeter.Local;

internal sealed class CooldownSettingsWindow : Window
{
    internal CooldownSettingsWindow(Preferences settings, SkillNames names, IEnumerable<int> observed, Action saved, Action open)
    {
        Title = "Tracked cooldowns"; Width = 560; Height = 750; MinWidth = 430; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; UiTheme.Apply(this, settings);
        var selected = CooldownSkillChoice.Group(settings.CooldownSkills ?? [], names).Take(24).ToList();
        var observedIds = observed.ToArray();
        var fallback = new Dictionary<int, double>(settings.CooldownFallbackSeconds ?? new());
        var root = new DockPanel { Margin = new(16) }; Content = root;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "Choose up to 24 skills", FontSize = 18, Margin = new(0, 0, 0, 8) });
        top.Children.Add(new TextBlock { Text = "Choose a skill once. Its charge levels and MAX share one timer and duration. Observed skills appear first.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) });
        var query = new TextBox { Margin = new(0, 0, 0, 8), ToolTip = "Search skill names (or look up an ID)" }; top.Children.Add(query);
        var bottom = new StackPanel { Margin = new(0, 10, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var ready = new CheckBox { Content = "Show skills when ready", IsChecked = settings.CooldownShowReady, Margin = new(0, 4, 0, 6) };
        var early = new CheckBox { Content = "Show cooldowns 1 second earlier", IsChecked = settings.CooldownOneSecondEarly, Margin = new(0, 0, 0, 6),
            ToolTip = "Subtracts 1 second from all overlay countdowns and ready indicators. Turn off if skills look ready too soon." };
        var locked = new CheckBox { Content = "Lock cooldown overlay position", IsChecked = settings.CooldownLocked, Margin = new(0, 0, 0, 10) };
        bottom.Children.Add(new TextBlock { Text = "Optional cast estimate (when no server cooldown arrives)", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 5) });
        var estimate = new StackPanel { Orientation = Orientation.Horizontal };
        var seconds = new TextBox { Width = 75, ToolTip = "Cooldown in seconds, 0 disables the estimate", Text = "0" };
        var setSeconds = new Button { Content = "Set seconds", Margin = new(7, 0, 0, 0) };
        estimate.Children.Add(seconds); estimate.Children.Add(setSeconds); bottom.Children.Add(estimate);
        var estimateHint = new TextBlock { Text = "Select a tracked skill above. Estimates appear with ~; enter the duration shown in game.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 8) }; bottom.Children.Add(estimateHint);
        bottom.Children.Add(ready); bottom.Children.Add(early); bottom.Children.Add(locked);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; bottom.Children.Add(actions);
        void Save() { settings.CooldownFallbackSeconds = fallback; settings.CooldownSkills = selected.Select(c => c.Id).ToArray(); settings.CooldownShowReady = ready.IsChecked == true; settings.CooldownOneSecondEarly = early.IsChecked == true; settings.CooldownLocked = locked.IsChecked == true; saved(); }
        var show = new Button { Content = "Save and open overlay", Margin = new(0, 0, 8, 0) }; show.Click += (_, _) => { Save(); Close(); open(); }; actions.Children.Add(show);
        var apply = new Button { Content = "Save" }; apply.Click += (_, _) => { Save(); Close(); }; actions.Children.Add(apply);
        var body = new Grid(); body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); body.RowDefinitions.Add(new() { Height = GridLength.Auto }); body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); root.Children.Add(body);
        var available = new ListBox { Background = UiTheme.Brush("#171F2C"), Foreground = UiTheme.Brush("#E7EDF7"), BorderBrush = UiTheme.Brush("#2B374A") }; body.Children.Add(available);
        var middle = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 8, 0, 8) }; Grid.SetRow(middle, 1); body.Children.Add(middle);
        var add = new Button { Content = "Add skill", Margin = new(0, 0, 8, 0) }; middle.Children.Add(add);
        var remove = new Button { Content = "Remove selected" }; middle.Children.Add(remove);
        var tracked = new ListBox { Background = UiTheme.Brush("#171F2C"), Foreground = UiTheme.Brush("#E7EDF7"), BorderBrush = UiTheme.Brush("#2B374A") }; Grid.SetRow(tracked, 2); body.Children.Add(tracked);
        tracked.SelectionChanged += (_, _) => {
            if (tracked.SelectedItem is CooldownSkillChoice choice) {
                seconds.Text = CooldownDurations.Seconds(choice.Id, names, fallback).ToString(System.Globalization.CultureInfo.CurrentCulture);
                estimateHint.Text = $"{choice.Name}: enter its in-game cooldown. 0 disables the estimate.";
            }
        };
        setSeconds.Click += (_, _) => {
            if (tracked.SelectedItem is not CooldownSkillChoice choice) { estimateHint.Text = "Select a tracked skill first."; return; }
            if (!double.TryParse(seconds.Text, out double duration) || !double.IsFinite(duration) || duration is < 0 or > 86400) { estimateHint.Text = "Enter seconds between 0 and 86400."; return; }
            CooldownDurations.Set(choice.Id, duration, names, fallback);
            estimateHint.Text = duration > 0 ? $"{choice.Name}: {duration}s estimate set. Save to apply." : "Estimate disabled. Save to apply.";
        };
        void ReloadSelected() { tracked.ItemsSource = selected.ToArray(); add.IsEnabled = selected.Count < 24; }
        void Search() {
            available.ItemsSource = CooldownSkillChoice.Search(observedIds, names, query.Text);
            available.SelectedIndex = 0;
        }
        void Add() { if (selected.Count < 24 && available.SelectedItem is CooldownSkillChoice c && !selected.Any(s => s.Matches(c))) { selected.Add(c); ReloadSelected(); } }
        add.Click += (_, _) => Add(); available.MouseDoubleClick += (_, _) => Add();
        remove.Click += (_, _) => { if (tracked.SelectedItem is CooldownSkillChoice c) { selected.Remove(c); ReloadSelected(); } };
        query.TextChanged += (_, _) => Search(); ReloadSelected(); Search();
    }
}
