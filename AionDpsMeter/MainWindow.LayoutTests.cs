using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    // Runs only in the isolated WPF smoke test, using synthetic encounters.
    void CheckResponsiveViews()
    {
        var originalView = viewed; var originalArchive = archive.ToArray(); string originalFilter = character.Text;
        double originalWidth = Width, originalHeight = Height;
        var results = new List<string>();
        void Layout() { UpdateLayout(); Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded); UpdateLayout(); }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        try {
            archive.Clear(); archive.AddRange(Enumerable.Range(1, 120).Select(FeatureTests.Sample)); archiveChanged = true; character.Clear();
            foreach (string name in new[] { "Encounter", "Party", "PvP", "Rotations", "Trends", "Top cohort", "Boss analysis", "Skills", "Healing", "Saved fights" }) {
                viewed = name == "Boss analysis" ? BossEncounterTests.Fixture() : FeatureTests.Sample();
                var tab = tabs.Items.Cast<TabItem>().Single(t => t.Header?.ToString() == name);
                tabs.SelectedItem = tab; Refresh(); Width = 1100; Height = 800; Layout();
                var host = (ViewportHost)tab.Content; host.ScrollToTop(); Layout();
                var split = LayoutVisuals<SplitView>(host).FirstOrDefault();
                if (split?.Lower is Expander expander) { expander.IsExpanded = true; Layout(); }
                double upper = split?.Upper.ActualHeight ?? 0, lower = split?.Lower.ActualHeight ?? 0;
                double tableHeight = grids.TryGetValue(name, out var table) ? table.ActualHeight : 0;
                if (name is "Encounter" or "Rotations" or "Boss analysis" or "Trends") Render(this, $"test-artifacts/Responsive-{name}-compact.png");
                Width = 1500; Height = 1100; Layout();
                var page = (FrameworkElement)host.Content;
                Require(Math.Abs(page.ActualHeight - host.ViewportHeight) < 2 && host.ScrollableHeight < 2, $"{name}: page does not fill the enlarged window ({page.ActualHeight}/{host.ViewportHeight}).");
                if (split != null) {
                    Require(split.Upper.ActualHeight > upper + 20 && split.Lower.ActualHeight > lower + 20,
                        $"{name}: both panels must grow ({upper:N0}/{lower:N0} to {split.Upper.ActualHeight:N0}/{split.Lower.ActualHeight:N0}).");
                    Require(Math.Abs(split.RowDefinitions.Sum(r => r.ActualHeight) - split.ActualHeight) < 2, $"{name}: panels exceed their available height.");
                    var divider = split.Children.OfType<GridSplitter>().Single();
                    double before = split.Upper.ActualHeight;
                    divider.Focus(); divider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(divider), 0, Key.Up) { RoutedEvent = Keyboard.KeyDownEvent }); Layout();
                    Require(split.Upper.ActualHeight < before - 5, $"{name}: divider cannot adjust the panel height.");
                    double share = split.RowDefinitions[0].Height.Value / (split.RowDefinitions[0].Height.Value + split.RowDefinitions[2].Height.Value);
                    for (int update = 0; update < 4; update++) { viewed = viewed.Copy(); Refresh(); Layout(); }
                    Require(ReferenceEquals(split, LayoutVisuals<SplitView>(host).First()), $"{name}: refreshing rebuilt the panels.");
                    double refreshed = split.RowDefinitions[0].Height.Value / (split.RowDefinitions[0].Height.Value + split.RowDefinitions[2].Height.Value);
                    Require(Math.Abs(refreshed - share) < .001, $"{name}: a live update reset the divider.");
                    Height = 1000; Layout(); Height = 1100; Layout();
                    Require(Math.Abs(split.RowDefinitions[0].Height.Value / (split.RowDefinitions[0].Height.Value + split.RowDefinitions[2].Height.Value) - share) < .001,
                        $"{name}: resizing reset the divider.");
                    if (split.Lower is Expander section) {
                        double expanded = split.Upper.ActualHeight;
                        section.IsExpanded = false; Layout();
                        Require(split.Upper.ActualHeight > expanded + 80 && divider.Visibility == Visibility.Collapsed, $"{name}: collapsed details still reserve space.");
                        section.IsExpanded = true; Layout();
                        Require(Math.Abs(split.Upper.ActualHeight - expanded) < 2, $"{name}: reopening details lost the chosen split.");
                    }
                    split.Reset(); Layout();
                } else if (table != null) {
                    Require(table.ActualHeight > tableHeight + 150 && table.ActualHeight > 550, $"{name}: table still has a fixed height cap.");
                }
                if (name == "Saved fights") Require(LayoutVisuals<DataGridRow>(table!).Count() < table!.Items.Count / 2, "Resizing disabled saved-fight row virtualization.");
                if (name == "Boss analysis") bossView.CheckResponsiveDetails(() => { Layout(); });
                if (name is "Encounter" or "Rotations" or "Boss analysis" or "Trends") Render(this, $"test-artifacts/Responsive-{name}-large.png");
                if (name == "Encounter") {
                    WindowState = WindowState.Maximized; Layout();
                    Require(Math.Abs(page.ActualHeight - host.ViewportHeight) < 2, "Maximizing leaves unused encounter space.");
                    WindowState = WindowState.Normal; Layout();
                }
                Width = MinWidth; Height = MinHeight; Layout(); host.ScrollToBottom(); Layout();
                Require(double.IsFinite(host.ExtentHeight) && host.VerticalOffset + host.ViewportHeight >= host.ExtentHeight - 2, $"{name}: compact view cannot scroll to the bottom.");
                host.ScrollToTop(); if (split?.Lower is Expander restore) restore.IsExpanded = false;
                results.Add($"PASS: {name} fills available space; compact and enlarged layouts, refresh and scrolling.");
            }
            File.WriteAllLines("responsive-layout-results.txt", results);
        }
        finally {
            viewed = originalView; archive.Clear(); archive.AddRange(originalArchive); archiveChanged = true; character.Text = originalFilter;
            WindowState = WindowState.Normal; Width = originalWidth; Height = originalHeight; tabs.SelectedIndex = 0; Refresh(); Layout();
        }
    }
    static IEnumerable<T> LayoutVisuals<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T result) yield return result;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in LayoutVisuals<T>(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
