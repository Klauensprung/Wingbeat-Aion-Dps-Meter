using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowNearbyPets(Window owner, Fight? savedFight, int goal = 1)
    {
        var window = new Window { Title = "Nearby pet farming", Owner = owner, Width = 880, Height = 600,
            MinWidth = 650, MinHeight = 440, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var layout = new DockPanel { Margin = new(20) };
        var heading = new StackPanel();
        var scroll = new ScrollViewer { Content = heading, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = window.Height * .5 };
        window.SizeChanged += (_, _) => scroll.MaxHeight = window.ActualHeight * .5;
        DockPanel.SetDock(scroll, Dock.Top); layout.Children.Add(scroll);
        heading.Children.Add(UiSections.Heading("Nearby pet farming", "Find the nearest spawn for pets you still need.",
            "Distances use straight-line world units from your last captured position. Terrain, elevation and creature availability are not considered."));
        var phase = new ComboBox { ItemsSource = new[] { "Phase 1: collect", "Phase 2: level 2", "Phase 3: level 3" }, SelectedIndex = Math.Clamp(goal, 1, 3) - 1, Margin = new(0, 0, 0, 8) };
        var unknown = new CheckBox { Content = "Include pets with unknown progress", Margin = new(0, 0, 0, 8) };
        heading.Children.Add(phase); heading.Children.Add(unknown);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) }; heading.Children.Add(status);
        var open = new Button { Content = "Show selected pet on map", IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 12) }; heading.Children.Add(open);
        var table = new LiveTable { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false,
            CanUserDeleteRows = false, RowHeight = 36, EnableRowVirtualization = true };
        void Column(string title, string binding, int width) => table.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(binding), Width = width });
        Column("Pet", "Name", 210); Column("Distance (units)", "DistanceText", 130); Column("Souls needed", "Needed", 110);
        Column("Progress", "Status", 140); Column("Family", "Family", 100); Column("Coordinates", "Coordinates", 220);
        var historical = new LocalPositionTracker();
        if (savedFight != null) foreach (var message in savedFight.Network) historical.Apply(message, savedFight.LocalPlayer);
        CapturedPosition? position = null, previousPosition = null; PetCollectionSnapshot? previousCollection = null;
        int previousGoal = -1; bool previousUnknown = false;
        void Update() {
            PetCollectionSnapshot? collection;
            if (savedFight != null) { position = savedFight.LocalPosition ?? historical.Latest; collection = savedFight.Pets; }
            else lock (engine.Gate) { position = engine.Position.Latest; collection = engine.CurrentPets(); }
            long age = position == null ? 0 : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - position.Timestamp;
            if (savedFight == null && age < 0) position = null;
            bool includeUnknown = unknown.IsChecked == true; int selectedGoal = phase.SelectedIndex + 1;
            if (position != previousPosition || !ReferenceEquals(collection, previousCollection) || selectedGoal != previousGoal || includeUnknown != previousUnknown) {
                int? selected = (table.SelectedItem as NearbyPet)?.Id;
                var rows = PetNearby.Build(PetCatalogue.Default.Pets, collection, position, selectedGoal, includeUnknown, PetSpawns.Default.ForPet);
                table.SetRows(rows); table.SelectedItem = rows.FirstOrDefault(r => r.Id == selected);
                previousPosition = position; previousCollection = collection; previousGoal = selectedGoal; previousUnknown = includeUnknown;
            }
            status.Text = position == null ? "No position captured. Updates on loading and effects you cast." :
                LocalPositionTracker.MapKey(position.MapId) == null ? "No spawn mapping for the captured area." :
                $"{(savedFight == null ? $"Position captured {age / 1000:N0}s ago" : "Saved position from this fight")} · {table.Items.Count} nearest pets (up to 30)" +
                (table.Items.Count == 0 ? " · No matching farming needs known; unknown progress can be included above." : "");
        }
        table.SelectionChanged += (_, _) => open.IsEnabled = table.SelectedItem is NearbyPet;
        open.Click += (_, _) => { if (table.SelectedItem is NearbyPet selected)
            ShowPetSpawns(selected.Id, window, savedFight, LocalPositionTracker.MapKey(position?.MapId ?? 0)); };
        phase.SelectionChanged += (_, _) => Update(); unknown.Checked += (_, _) => Update(); unknown.Unchecked += (_, _) => Update();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; timer.Tick += (_, _) => Update();
        window.Closed += (_, _) => timer.Stop();
        layout.Children.Add(table); window.Content = layout; Update(); if (savedFight == null) timer.Start(); window.Show();
    }
}
