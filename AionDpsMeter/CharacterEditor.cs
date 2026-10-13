using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AionDpsMeter.Local;

internal sealed class CharacterEditor : Window
{
    internal sealed class CharacterEntry
    {
        public int Id { get; init; }
        public string Character { get; init; } = "";
        public bool Player { get; set; }
        public bool Party { get; set; }
        public string Class { get; set; } = "Unknown";
        public int CP { get; set; }
        public int Server { get; set; }
        public bool CompleteLoadout { get; set; }
    }
    internal sealed class SkillEntry
    {
        public int ActorId { get; set; }
        public int SkillId { get; set; }
        public int Level { get; set; }
        public string Choices { get; set; } = "";
    }
    internal sealed class GearEntry
    {
        public int ActorId { get; set; }
        public string Slot { get; set; } = "";
        public string Item { get; set; } = "";
    }
    readonly Fight original;
    readonly ObservableCollection<CharacterEntry> characters = new();
    readonly ObservableCollection<SkillEntry> skills = new();
    readonly ObservableCollection<GearEntry> gear = new();
    readonly List<DataGrid> grids = new();
    readonly TabControl tabs = new() { TabStripPlacement = Dock.Left };
    readonly CheckBox roster = new() { Content = "Use checked Party members as the current roster (up to 10)", Margin = new(8, 12, 8, 12) };
    readonly TextBlock error = new() { Margin = new(10), TextWrapping = TextWrapping.Wrap };
    public Fight? Result { get; private set; }
    internal CharacterEditor(Fight fight, Preferences preferences)
    {
        original = fight.Copy();
        Title = "Party & character details"; Width = 1100; Height = 610; MinWidth = 900; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UiTheme.Apply(this, preferences);
        foreach (var actor in fight.Actors.Values.Where(a => a.NpcId == 0).OrderByDescending(a => a.Id == fight.LocalPlayer).ThenByDescending(a => a.Player).ThenBy(a => a.Name))
        {
            var build = FightMetadata.BuildFor(fight, actor.Id);
            characters.Add(new() { Id = actor.Id, Character = actor.Name, Player = actor.Player, Party = actor.Party || actor.Id == fight.LocalPlayer,
                Class = actor.Job, CP = actor.Cp, Server = actor.Server, CompleteLoadout = build?.Complete == true });
            if (build == null) continue;
            foreach (var skill in build.Skills) skills.Add(new() { ActorId = actor.Id, SkillId = skill.Skill, Level = skill.Level, Choices = string.Join(", ", skill.Choices) });
            foreach (var item in build.Equipment) gear.Add(new() { ActorId = actor.Id, Slot = item.Key, Item = item.Value });
        }
        roster.IsChecked = fight.PartyKnown || fight.Actors.Values.Any(a => a.Party);
        var root = new DockPanel { Margin = new(14) }; Content = root;
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); footer.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; footer.Children.Add(buttons);
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new(4), Padding = new(15, 6, 15, 6) }; buttons.Children.Add(cancel);
        var save = new Button { Content = "Save details", Margin = new(4), Padding = new(15, 6, 15, 6) }; buttons.Children.Add(save); save.Click += (_, _) => Save();
        var help = new TextBlock { Text = "Add class, Combat Power and server for comparisons. Changes are stored with this fight and marked as entered locally. Player names come from capture.", TextWrapping = TextWrapping.Wrap, Margin = new(8, 4, 8, 12) };
        DockPanel.SetDock(help, Dock.Top); root.Children.Add(help);
        root.Children.Add(tabs);
        DataGrid Grid(string title, object source)
        {
            var grid = new DataGrid { ItemsSource = (System.Collections.IEnumerable)source, AutoGenerateColumns = false, CanUserAddRows = false,
                CanUserDeleteRows = title != "Characters", EnableRowVirtualization = true, RowHeight = 32, HeadersVisibility = DataGridHeadersVisibility.Column, Margin = new(5), GridLinesVisibility = DataGridGridLinesVisibility.None,
                Background = Background, Foreground = Foreground, RowBackground = UiTheme.Brush("#171F2C"), AlternatingRowBackground = UiTheme.Brush("#1B2533") };
            grids.Add(grid); return grid;
        }
        void TextColumn(DataGrid grid, string property, string header, bool readOnly = false, double width = 1)
            => grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(property), IsReadOnly = readOnly, MinWidth = 70,
                Width = property switch { "SkillId" => 140, "Level" => 80, "Choices" => 270, _ => Math.Max(70, width * 100) } });
        void CheckColumn(DataGrid grid, string property, string header)
            => grid.Columns.Add(new DataGridCheckBoxColumn { Header = header, Binding = new Binding(property), Width = 70 });
        var actors = Grid("Characters", characters);
        TextColumn(actors, "Id", "ID", true, .6); TextColumn(actors, "Character", "Character", true, 1.8);
        CheckColumn(actors, "Player", "Player"); CheckColumn(actors, "Party", "Party");
        actors.Columns.Add(new DataGridComboBoxColumn { Header = "Class", ItemsSource = FightMetadata.Jobs, SelectedItemBinding = new Binding("Class"), Width = 115 });
        TextColumn(actors, "CP", "CP"); TextColumn(actors, "Server", "Server");
        var actorPanel = new DockPanel(); DockPanel.SetDock(roster, Dock.Top); actorPanel.Children.Add(roster); actorPanel.Children.Add(actors);
        tabs.Items.Add(new TabItem { Header = "Characters", Content = actorPanel });
    }
    internal static Fight Apply(Fight original, IEnumerable<CharacterEntry> characterEntries, IEnumerable<SkillEntry> skillEntries, IEnumerable<GearEntry> gearEntries, bool explicitRoster)
    {
        var edited = original.Copy(); var characters = characterEntries.ToArray(); var skills = skillEntries.ToArray(); var gear = gearEntries.ToArray();
        edited.Specs.Clear();
        foreach (var row in characters)
        {
            if (!edited.Actors.TryGetValue(row.Id, out var actor) || actor.NpcId != 0 || row.CP is < 0 or > 100000000 || row.Server is < 0 or > 99999 || !FightMetadata.Jobs.Contains(row.Class))
                throw new InvalidDataException("Check character class, CP and server values.");
            if (row.Id == original.LocalPlayer && !row.Player) throw new InvalidDataException("Your detected character must remain a player.");
            bool changed = row.Player != actor.Player || row.Class != actor.Job || row.CP != actor.Cp || row.Server != actor.Server;
            edited.Actors[row.Id] = actor with { Player = row.Player, Party = false, Cp = row.CP, CpSource = row.CP != actor.Cp ? "Entered locally" : actor.CpSource, Job = row.Class, Server = row.Server, ProfileSource = changed ? "Entered locally" : actor.ProfileSource };
            var specs = skills.Where(s => s.ActorId == row.Id).Select(s => new Spec(s.SkillId, s.Level,
                s.Choices.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToArray())).ToArray();
            var equipment = gear.Where(g => g.ActorId == row.Id).ToDictionary(g => g.Slot.Trim(), g => g.Item.Trim());
            edited.Builds.Remove(row.Id);
            if (specs.Length > 0 || equipment.Count > 0 || row.CompleteLoadout)
            {
                var previous = FightMetadata.BuildFor(original, row.Id);
                bool same = previous != null && previous.Complete == row.CompleteLoadout && previous.Skills.Length == specs.Length &&
                    previous.Skills.All(s => specs.Any(p => p.Skill == s.Skill && p.Level == s.Level && p.Choices.SequenceEqual(s.Choices))) &&
                    previous.Equipment.Count == equipment.Count && previous.Equipment.All(e => equipment.GetValueOrDefault(e.Key) == e.Value);
                FightMetadata.SetBuild(edited, new(row.Id, specs, row.CompleteLoadout, same ? previous!.Source : "Entered locally") { Equipment = equipment });
            }
        }
        if (skills.Any(s => !characters.Any(a => a.Id == s.ActorId)) || gear.Any(s => !characters.Any(a => a.Id == s.ActorId)))
            throw new InvalidDataException("Every loadout row needs a character from this fight.");
        if (explicitRoster)
        {
            var members = characters.Where(c => c.Party || c.Id == original.LocalPlayer).Select(c => c.Id).ToArray();
            bool same = original.PartyKnown && original.Actors.Values.Where(a => FightMetadata.InParty(original, a.Id)).Select(a => a.Id).ToHashSet().SetEquals(members);
            FightMetadata.SetRoster(edited, new(members, same ? original.PartySource : "Entered locally"));
        }
        else { edited.PartyKnown = false; edited.PartySource = "Not captured"; }
        return edited;
    }
    void Save()
    {
        try {
            foreach (var grid in grids) if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true)) throw new InvalidDataException("Finish correcting the highlighted value before saving.");
            Result = Apply(original, characters, skills, gear, roster.IsChecked == true); DialogResult = true;
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or OverflowException or ArgumentException) { error.Text = ex.Message; }
    }
    internal void CheckViews(Action<FrameworkElement, string> render)
    {
        foreach (TabItem tab in tabs.Items)
        {
            tabs.SelectedItem = tab; UpdateLayout();
            render(this, "test-artifacts/Details-" + tab.Header + ".png");
        }
        if (grids.Count != 1 || grids[0].Items.Count != 10 || tabs.Items.Count != 1)
            throw new InvalidOperationException("Character editor must show just the party and character table.");
    }
}
