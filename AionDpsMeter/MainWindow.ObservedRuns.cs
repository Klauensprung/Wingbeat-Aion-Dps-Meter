using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowObservedRuns()
    {
        var groups = ObservedRunGroups.Build(archive);
        var window = new Window { Title = "Observed dungeon runs", Owner = this, Width = 1050, Height = 760,
            MinWidth = 750, MinHeight = 600, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var layout = new DockPanel { Margin = new(20) };
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        heading.Children.Add(UiSections.Heading("Observed dungeon runs", "Saved fights, captured completions and available clear times.",
            "Completion requires a successful dungeon result. Clear time uses a captured process-state start and the arrival of that result; post-clear deadlines are excluded. Partial entries are labeled. Rates cover captured combat spans, including gaps; missing fights are not reconstructed."));
        var status = new TextBlock { Text = groups.Length == 0 ? "No saved fights with matching run observations yet. Capture a dungeon from entry to preserve its context." : $"{groups.Length:N0} observed runs · select a run to inspect its members and fights.",
            TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) }; heading.Children.Add(status);
        var progress = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12), Visibility = Visibility.Collapsed };
        heading.Children.Add(progress);
        LiveTable Table() => new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false, RowHeight = 34, EnableRowVirtualization = true };
        void Column(LiveTable table, string title, string property, int width) => table.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = width });
        var runs = Table(); var members = Table(); var fights = Table(); var usedItems = Table(); var checkpoints = Table(); var runSkills = Table(); var roster = Table();
        Column(roster, "Member", "Name", 180); Column(roster, "Server", "Server", 70); Column(roster, "Class", "Job", 110);
        Column(roster, "Level", "Level", 65); Column(roster, "Gear score", "GearScore", 90); Column(roster, "CP", "CombatPower", 90);
        Column(runSkills, "Skill used", "Name", 450); Column(runSkills, "Skill ID", "Id", 120);
        Column(checkpoints, "Event", "Event", 350); Column(checkpoints, "Stage", "Stage", 70); Column(checkpoints, "Step", "Step", 70);
        Column(checkpoints, "Status", "Status", 120);
        Column(usedItems, "Item", "Item", 350); Column(usedItems, "Received", "Received", 120); Column(usedItems, "Used", "Used", 120);
        Column(runs, "Area", "Map", 180); Column(runs, "Status", "Status", 230); Column(runs, "Clear time", "ClearTime", 110);
        Column(runs, "Fights", "Fights", 65); Column(runs, "Span (s)", "CapturedSeconds", 100);
        Column(runs, "Character", "Character", 150); Column(runs, "Recorded", "Recorded", 155);
        Column(members, "Member", "Character", 180); Column(members, "Server", "Server", 85); Column(members, "Class", "Job", 100);
        Column(members, "Damage", "Damage", 100); Column(members, "Captured DPS", "CapturedDps", 110);
        Column(members, "Healing", "Healing", 100); Column(members, "Captured HPS", "CapturedHps", 110);
        Column(members, "Damage taken", "DamageTaken", 130); Column(members, "Deaths", "Deaths", 80);
        Column(fights, "Fight", "Label", 260); Column(fights, "Bosses", "Bosses", 280); Column(fights, "Seconds", "Seconds", 90);
        var panels = new Grid();
        for (int i = 0; i < 7; i++) panels.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star), MinHeight = 150 });
        panels.Children.Add(UiSections.TableSection("Runs", runs));
        var memberSection = UiSections.TableSection("Members", members); memberSection.Margin = new(0, 8, 0, 0);
        Grid.SetRow(memberSection, 1); panels.Children.Add(memberSection);
        var fightSection = UiSections.TableSection("Fights - double-click to inspect", fights); fightSection.Margin = new(0, 8, 0, 0);
        Grid.SetRow(fightSection, 2); panels.Children.Add(fightSection);
        var usedSection = UiSections.TableSection("Items received and used - confirmed inventory changes", usedItems); usedSection.Margin = new(0, 8, 0, 0);
        Grid.SetRow(usedSection, 3); panels.Children.Add(usedSection);
        var checkpointSection = UiSections.TableSection("Captured progress checkpoints", checkpoints); checkpointSection.Margin = new(0, 8, 0, 0);
        Grid.SetRow(checkpointSection, 4); panels.Children.Add(checkpointSection);
        var skillSection = UiSections.TableSection("Local skills used - including owned summons", runSkills); skillSection.Margin = new(0, 8, 0, 0);
        Grid.SetRow(skillSection, 5); panels.Children.Add(skillSection);
        var rosterSection = UiSections.TableSection("Roster observed during this run - includes departed and offline members", roster); rosterSection.Margin = new(0, 8, 0, 0);
        Grid.SetRow(rosterSection, 6); panels.Children.Add(rosterSection);
        runs.SelectionChanged += (_, _) => {
            var selected = runs.SelectedItem as ObservedRunGroup;
            progress.Visibility = selected is null ? Visibility.Collapsed : Visibility.Visible;
            progress.Text = selected is null ? "" : $"{selected.Map}: {selected.CapturedBossKills:N0} boss kills captured | Reported deaths: {selected.ReportedDeaths}";
            if (selected?.OmittedUses > 0) progress.Text += $" | {selected.OmittedUses:N0} uses omitted at the retention limit";
            if (selected?.OmittedReceived > 0) progress.Text += $" | {selected.OmittedReceived:N0} received items omitted at the retention limit";
            if (selected?.OmittedSplits > 0) progress.Text += $" | {selected.OmittedSplits:N0} checkpoints omitted at the retention limit";
            checkpoints.SetRows((selected?.Splits ?? []).Select(s => new { s.Event, s.Stage, s.Step, Status = s.Done ? "Completed" : "In progress" }).ToArray());
            if (selected?.OmittedSkillEvents > 0) progress.Text += $" | {selected.OmittedSkillEvents:N0} skill observations omitted at the retention limit";
            runSkills.SetRows((selected?.Skills ?? []).Select(id => new { Id = id, Name = skills.GetSkillName(id) }).ToArray());
            if (selected?.OmittedPartyObservations > 0) progress.Text += $" | {selected.OmittedPartyObservations:N0} roster observations omitted at the retention limit";
            roster.SetRows(selected?.Party ?? []);
            var received = selected?.ReceivedItems.ToDictionary(i => i.ItemId, i => i.Count) ?? new();
            var used = selected?.UsedItems.ToDictionary(i => i.ItemId, i => i.Count) ?? new();
            usedItems.SetRows(received.Keys.Concat(used.Keys).Distinct().Select(id => new { Item = ItemNames.Default.Find(id), Received = received.GetValueOrDefault(id), Used = used.GetValueOrDefault(id) }).OrderBy(i => i.Item).ToArray());
            members.SetRows(selected?.Members ?? []);
            fights.SetRows(selected?.Encounters.Select(f => new ObservedRunFight(f.Id, f.Label, BossAnalysis.Summary(f), Math.Round(f.Seconds, 1))).ToArray() ?? []);
        };
        fights.MouseDoubleClick += (_, _) => { if (fights.SelectedItem is ObservedRunFight fight) OpenTrendFight(fight.Id); };
        runs.SetRows(groups); if (groups.Length > 0) runs.SelectedIndex = 0;
        layout.Children.Add(new ScrollViewer { Content = panels, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalContentAlignment = VerticalAlignment.Stretch });
        window.Content = layout; window.Show();
    }
    sealed record ObservedRunFight(string Id, string Label, string Bosses, double Seconds);
}
