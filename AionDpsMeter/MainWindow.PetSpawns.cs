using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowPetSpawns(int petId, Window owner, Fight? savedFight = null, string? initialMapKey = null)
    {
        var pet = PetCatalogue.Default.Find(petId);
        var reference = PetSpawns.Default;
        var sites = reference.ForPet(petId);
        var window = new Window { Title = global::AionDpsMeter.Local.Localization.T("Pet spawn locations"), Owner = owner, Width = 940, Height = 660, MinWidth = 620, MinHeight = 430,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var layout = new DockPanel { Margin = new(20) };
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        heading.Children.Add(UiSections.Heading(pet?.Name ?? $"Pet {petId}", "Scroll to zoom, drag to pan, and select a spawn for details.",
            "Reference terrain covers Verteron, Altgard and the lower Abyss. The blue cross is your last captured position, not continuous tracking. Creature availability is unknown."));
        var areas = new ComboBox { ItemsSource = sites, DisplayMemberPath = "Map", Margin = new(0, 0, 0, 8), IsEnabled = sites.Count > 0 };
        heading.Children.Add(areas);
        var plot = new PetSpawnPlot();
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) }; heading.Children.Add(status);
        var actions = new WrapPanel { Margin = new(0, 0, 0, 10) }; heading.Children.Add(actions);
        var fit = new Button { Content = global::AionDpsMeter.Local.Localization.T("Fit locations"), IsEnabled = sites.Count > 0 }; fit.Click += (_, _) => plot.Fit(); actions.Children.Add(fit);
        var center = new Button { Content = global::AionDpsMeter.Local.Localization.T("Center last position"), IsEnabled = false, Margin = new(8, 0, 0, 0) }; center.Click += (_, _) => plot.CenterPlayer(); actions.Children.Add(center);
        var copy = new Button { Content = global::AionDpsMeter.Local.Localization.T("Copy selected coordinates"), IsEnabled = false, Margin = new(8, 0, 0, 0) }; actions.Children.Add(copy);
        var details = new TextBlock { Text = global::AionDpsMeter.Local.Localization.T("Select a spawn point."), TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 0) };
        var positionStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) }; heading.Children.Add(positionStatus);
        var historical = new LocalPositionTracker();
        if (savedFight != null) foreach (var message in savedFight.Network) historical.Apply(message, savedFight.LocalPlayer);
        void UpdatePosition() {
            CapturedPosition? sample;
            if (savedFight != null) sample = savedFight.LocalPosition ?? historical.Latest; else lock (engine.Gate) sample = engine.Position.Latest;
            string? mapKey = (areas.SelectedItem as PetSpawnSite)?.MapKey;
            bool matching = LocalPositionTracker.Matches(sample, mapKey);
            long age = sample == null ? 0 : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - sample.Timestamp;
            bool show = matching && (savedFight != null || age >= 0);
            plot.SetPlayer(show ? sample : null); center.IsEnabled = show;
            positionStatus.Text = !matching ? global::AionDpsMeter.Local.Localization.T("No position captured for this area.") : savedFight != null ? global::AionDpsMeter.Local.Localization.T("Blue cross: saved position from this fight.") :
                age < 0 ? global::AionDpsMeter.Local.Localization.T("Position clock mismatch; marker hidden.") : global::AionDpsMeter.Local.Localization.F($"Blue cross: captured {age / 1000:N0}s ago · {(capture == null ? Localization.T("capture stopped") : Localization.T("updates on loading and effects you cast"))}");
        }
        DockPanel.SetDock(details, Dock.Bottom); layout.Children.Add(details);
        PetSpawnPoint? selected = null;
        plot.Selected += point => {
            selected = point; copy.IsEnabled = point != null;
            details.Text = point == null ? global::AionDpsMeter.Local.Localization.T("Select a spawn point.") :
                global::AionDpsMeter.Local.Localization.F($"{NpcNames.Default.GetName(point.NpcId)} · NPC {point.NpcId} · X {point.X:N0} / Y {point.Y:N0} / Z {point.Z:N0} (reference world coordinates)");
        };
        copy.Click += (_, _) => { if (selected is { } point && areas.SelectedItem is PetSpawnSite site)
            Clipboard.SetText(string.Create(CultureInfo.InvariantCulture, $"{pet?.Name ?? $"Pet {petId}"} · {site.Map} · NPC {point.NpcId} · X {point.X:0.###}, Y {point.Y:0.###}, Z {point.Z:0.###}")); };
        areas.SelectionChanged += (_, _) => {
            var site = areas.SelectedItem as PetSpawnSite; plot.SetSite(site);
            status.Text = site == null ? global::AionDpsMeter.Local.Localization.T("No named-area spawn reference is available for this pet.") : global::AionDpsMeter.Local.Localization.F($"{site.Spawns.Length:N0} reference locations · {site.Map} · ") +
                (PetTerrain.Find(site.MapKey) == null ? global::AionDpsMeter.Local.Localization.T("Coordinate plot; terrain unavailable") : global::AionDpsMeter.Local.Localization.T("Reference terrain"));
            UpdatePosition();
        };
        if (sites.Count > 0) areas.SelectedItem = sites.FirstOrDefault(s => s.MapKey == initialMapKey) ?? sites[0];
        else status.Text = reference.LoadError ?? global::AionDpsMeter.Local.Localization.F($"No named-area spawn reference for this pet. The source also contains {reference.ExcludedUnnamedAreas} unnamed areas, excluded from this view.");
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; timer.Tick += (_, _) => UpdatePosition();
        window.Closed += (_, _) => timer.Stop(); UpdatePosition(); if (savedFight == null) timer.Start();
        layout.Children.Add(plot); window.Content = layout; window.Show();
    }
}
