using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowPetCatalogue()
    {
        var catalog = PetCatalogue.Default;
        var savedFight = viewed;
        PetCollectionSnapshot? ReadCollection() { if (savedFight != null) return savedFight.Pets; lock (engine.Gate) return engine.CurrentPets(); }
        var snapshot = ReadCollection();
        var allRows = PetCollectionView.Build(catalog, snapshot);
        var window = new Window { Title = global::AionDpsMeter.Local.Localization.T("Pets and souls"), Owner = this, Width = 1180, Height = 680, MinWidth = 700, MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var layout = new DockPanel { Margin = new(18) };
        var heading = new StackPanel();
        var filters = new ScrollViewer { Content = heading, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = window.Height * .55 };
        DockPanel.SetDock(filters, Dock.Top); layout.Children.Add(filters);
        window.SizeChanged += (_, _) => filters.MaxHeight = Math.Max(150, window.ActualHeight * .55);
        heading.Children.Add(UiSections.Heading("Pets and souls", "Track your collection and find the next pet to farm.",
            $"Offline reference, game data {catalog.GameVersion}. Soul sources are farming references. Captured levels and souls are experimental; ? means unknown. Gains count since the latest roster or capture start. After a level change, remaining souls stay unknown until reported again."));
        var primaryFilters = new WrapPanel(); heading.Children.Add(primaryFilters);
        var family = new ComboBox { ItemsSource = new[] { "All families" }.Concat(catalog.Pets.Select(p => p.Family).Distinct().Order()).ToArray(), SelectedIndex = 0, Margin = new(0, 0, 0, 8) };
        var search = new TextBox { Tag = "Pet name or soul source...", Margin = new(0, 0, 0, 8), ToolTip = global::AionDpsMeter.Local.Localization.T("Search pet name, ID or soul source") };
        primaryFilters.Children.Add(UiSections.Field("Find a pet", search, 270));
        primaryFilters.Children.Add(UiSections.Field("Family", family, 190));
        var phase = new ComboBox { ItemsSource = new[] { "All pets", "Phase 1: collect", "Phase 2: level 2", "Phase 3: level 3" }, SelectedIndex = 0, Margin = new(0, 0, 0, 8) };
        phase.ToolTip = global::AionDpsMeter.Local.Localization.T("Completed goals are hidden. Unknown capture entries remain visible; an absent pet is not assumed unowned. Souls to goal sums remaining level costs and subtracts captured souls.");
        primaryFilters.Children.Add(UiSections.Field("Farming goal", phase, 210));
        var areaFilters = new WrapPanel();
        var side = new ComboBox { ItemsSource = new[] { "All sides / areas", "Elyos", "Asmodian", "Abyss" }, SelectedIndex = 0, MinWidth = 160, Margin = new(0, 0, 8, 8) };
        var region = new ComboBox { ItemsSource = new[] { "All regions" }.Concat(catalog.Pets.SelectMany(p => p.Locations).Select(l => l.Region).Distinct().Order()).ToArray(),
            SelectedIndex = 0, MinWidth = 180, Margin = new(0, 0, 0, 8) };
        areaFilters.Children.Add(UiSections.Field("Side / area", side, 190)); areaFilters.Children.Add(UiSections.Field("Region", region, 270)); heading.Children.Add(areaFilters);
        var capturedOnly = new CheckBox { Content = global::AionDpsMeter.Local.Localization.T("Only pets seen in capture"), Margin = new(0, 0, 0, 8) }; heading.Children.Add(capturedOnly);
        var status = new TextBlock { Margin = new(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap }; heading.Children.Add(status);
        var table = new LiveTable { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
            EnableRowVirtualization = true, EnableColumnVirtualization = true, RowHeight = 36, HeadersVisibility = DataGridHeadersVisibility.Column };
        var spawns = new Button { Content = global::AionDpsMeter.Local.Localization.T("Show spawn map"), IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 10) };
        var actions = new WrapPanel(); heading.Children.Add(actions); spawns.Margin = new(0, 0, 8, 8); actions.Children.Add(spawns);
        var nearby = new Button { Content = global::AionDpsMeter.Local.Localization.T("Find nearby pets"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 10) };
        nearby.Click += (_, _) => ShowNearbyPets(window, savedFight, Math.Max(1, phase.SelectedIndex)); actions.Children.Add(nearby);
        table.SelectionChanged += (_, _) => spawns.IsEnabled = table.SelectedItem is PetCollectionRow;
        spawns.Click += (_, _) => { if (table.SelectedItem is PetCollectionRow selected) ShowPetSpawns(selected.Id, window, savedFight); };
        void Column(string title, string property, double width) => table.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = width });
        Column("Pet", "Name", 220); Column("Active", "Active", 70); Column("Level", "Level", 60); Column("Level source", "LevelSource", 100); Column("Souls", "Souls", 80);
        Column("Gained", "Gains", 80); Column("Family", "Family", 100); Column("Summon souls", "SummonSouls", 110);
        Column("Souls to goal", "Needed", 100); Column("Goal status", "PhaseStatus", 130);
        Column("Regions", "Regions", 240); Column("Side / area", "Factions", 140);
        Column("Listed soul source", "SoulSource", 240); Column("Pet ID", "Id", 80);
        void Filter() {
            string query = search.Text.Trim();
            var rows = allRows.Where(p => !p.GoalComplete && PetCollectionView.MatchesLocation(p, region.SelectedIndex == 0 ? null : region.SelectedItem?.ToString(), side.SelectedIndex == 0 ? null : side.SelectedItem?.ToString()) &&
                (capturedOnly.IsChecked != true || p.Captured) && (family.SelectedIndex == 0 || p.Family == family.SelectedItem?.ToString()) &&
                (p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || p.SoulSource.Contains(query, StringComparison.OrdinalIgnoreCase) || p.Id.ToString().Contains(query))).OrderBy(p => p.Name).ToArray();
            table.SetRows(rows);
            string context = savedFight == null ? "Live collection" : "Saved fight: " + savedFight.Label;
            string coverage = snapshot == null ? "No pet events captured" : snapshot.HasRoster ? "Roster received" : "Partial capture; roster not received";
            if (phase.SelectedIndex > 0) coverage += $" · {allRows.Count(p => p.GoalComplete):N0} at goal; unknown entries retained";
            status.Text = global::AionDpsMeter.Local.Localization.F($"{context} · {coverage} · {rows.Length:N0} pets · ") + (catalog.LoadError ?? global::AionDpsMeter.Local.Localization.T("reference: aion2data.com/pets"));
        }
        family.SelectionChanged += (_, _) => Filter(); search.TextChanged += (_, _) => Filter();
        phase.SelectionChanged += (_, _) => { allRows = PetCollectionView.Build(catalog, snapshot, phase.SelectedIndex); Filter(); };
        side.SelectionChanged += (_, _) => Filter(); region.SelectionChanged += (_, _) => Filter();
        capturedOnly.Checked += (_, _) => Filter(); capturedOnly.Unchecked += (_, _) => Filter();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => {
            var next = ReadCollection();
            if (ReferenceEquals(snapshot, next)) return;
            snapshot = next; allRows = PetCollectionView.Build(catalog, snapshot, phase.SelectedIndex); Filter();
        };
        window.Closed += (_, _) => timer.Stop();
        if (savedFight == null) timer.Start();
        layout.Children.Add(table); window.Content = layout; Filter(); window.Show();
    }
}
