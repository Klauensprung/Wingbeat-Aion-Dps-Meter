using System.Windows;
using System.Windows.Controls;

namespace AionDpsMeter.Local;

internal sealed class CooldownSettingsWindow : Window
{
    internal CooldownSettingsWindow(Preferences settings, SkillNames names, IEnumerable<int> observed, Action saved, Action open, Func<LiveEffect[]>? activeEffects = null)
    {
        Title = "Tracked cooldowns"; Width = 920; Height = 710; MinWidth = 760; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; UiTheme.Apply(this, settings);
        var selected = CooldownSkillChoice.Group(settings.CooldownSkills ?? [], names).Take(24).ToList();
        var observedIds = observed.ToArray();
        var fallback = new Dictionary<int, double>(settings.CooldownFallbackSeconds ?? new());
        int[] buffs = [.. settings.CooldownBuffs ?? []];
        var root = new DockPanel { Margin = new(16) }; Content = root;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(UiSections.Heading("Tracked cooldowns", "Choose up to 24 skills. Charge levels share one timer; observed skills appear first."));
        top.Children.Add(new TextBlock { Text = CooldownCharacterPresets.Description(settings), TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) });
        var query = new TextBox { Tag = "Search skill names or IDs...", Margin = new(0, 0, 0, 12), ToolTip = "Search skill names (or look up an ID)" }; top.Children.Add(query);
        var bottom = new StackPanel { Margin = new(12, 0, 4, 0) };
        var options = new ScrollViewer { Content = bottom, Width = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        DockPanel.SetDock(options, Dock.Right); root.Children.Add(options);
        var ready = new CheckBox { Content = "Show skills when ready", IsChecked = settings.CooldownShowReady, Margin = new(0, 4, 0, 6) };
        var layout = new ComboBox { ItemsSource = new[] { "Square", "Round", "Bars" }, SelectedItem = CooldownPresentation.Style(settings.CooldownStyle), Margin = new(0, 4, 0, 6) };
        var glow = new CheckBox { Content = "Glow when ready", IsChecked = settings.CooldownReadyGlow, Margin = new(0, 0, 0, 6) };
        var combatOnly = new CheckBox { Content = "Show overlay only during combat or training", IsChecked = settings.CooldownCombatOnly, Margin = new(0, 0, 0, 6),
            ToolTip = "Hide after 10 seconds without your or your party's damage or defensive events. Timers keep tracking while hidden. Reopen these settings from the main window." };
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
        bottom.Children.Add(new TextBlock { Text = "Overlay layout" }); bottom.Children.Add(layout); bottom.Children.Add(glow);
        bottom.Children.Add(combatOnly);
        var buffButton = new Button { Content = $"Track buff uptime ({buffs.Length})", Margin = new(0, 12, 0, 0) };
        buffButton.Click += (_, _) => new BuffTrackingSettings(settings, buffs, activeEffects?.Invoke() ?? [], ids => { buffs = ids; buffButton.Content = $"Track buff uptime ({buffs.Length})"; }) { Owner = this }.ShowDialog();
        bottom.Children.Add(buffButton);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 12, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Insert(1, actions);
        void Save() { settings.CooldownCombatOnly = combatOnly.IsChecked == true; settings.CooldownStyle = layout.SelectedItem?.ToString() ?? "Square"; settings.CooldownReadyGlow = glow.IsChecked == true; settings.CooldownFallbackSeconds = fallback; settings.CooldownSkills = selected.Select(c => c.Id).ToArray(); settings.CooldownShowReady = ready.IsChecked == true; settings.CooldownOneSecondEarly = early.IsChecked == true; settings.CooldownLocked = locked.IsChecked == true; saved(); }
        var show = new Button { Content = "Save and open overlay", Margin = new(0, 0, 8, 0) }; show.Click += (_, _) => { settings.CooldownBuffs = buffs; Save(); Close(); open(); }; actions.Children.Add(show);
        var apply = new Button { Content = "Save" }; apply.Click += (_, _) => { settings.CooldownBuffs = buffs; Save(); Close(); }; actions.Children.Add(apply);
        var body = new Grid(); body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); body.RowDefinitions.Add(new() { Height = GridLength.Auto }); body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); root.Children.Add(body);
        var available = new ListBox(); body.Children.Add(UiSections.TableSection("Available skills", available));
        var middle = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 8, 0, 8) }; Grid.SetRow(middle, 1); body.Children.Add(middle);
        var add = new Button { Content = "Add skill", Margin = new(0, 0, 8, 0) }; middle.Children.Add(add);
        var remove = new Button { Content = "Remove selected" }; middle.Children.Add(remove);
        var tracked = new ListBox(); var trackedSection = UiSections.TableSection("Tracked skills", tracked); Grid.SetRow(trackedSection, 2); body.Children.Add(trackedSection);
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
