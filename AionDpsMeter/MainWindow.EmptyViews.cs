using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void CheckEmptyViews()
    {
        var previous = viewed; var previousArchive = archive.ToArray(); var previousTab = tabs.SelectedItem; string previousSearch = character.Text;
        void Layout() { UpdateLayout(); Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded); UpdateLayout(); }
        try {
            archive.Clear(); archiveChanged = true; viewed = new Fight(); character.Clear();
            foreach (string name in new[] { "Skills", "Healing", "Saved fights", "Trends", "Player stats", "Network events", "Report card", "Top 5%", "Top cohort", "Openers" }) {
                tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(t => t.Header?.ToString() == name); Refresh(); Layout();
                var table = (LiveTable)grids[name];
                if (table.Items.Count != 0 || !table.EmptyStateVisible) throw new InvalidOperationException(name + " lost its empty-state guidance.");
                Render(this, "test-artifacts/Empty-" + name + ".png");
            }
            tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(t => t.Header?.ToString() == "Skills");
            viewed = FeatureTests.Sample(); character.Text = global::AionDpsMeter.Local.Localization.T("No matching character"); Refresh(); Layout();
            if (!((LiveTable)grids["Skills"]).EmptyStateVisible) throw new InvalidOperationException("Filtered table lost its empty state.");
            Render(this, "test-artifacts/Filtered-Skills.png");
            character.Clear(); Refresh(); Layout();
            if (((LiveTable)grids["Skills"]).EmptyStateVisible || grids["Skills"].Items.Count == 0) throw new InvalidOperationException("Clearing search did not restore the populated table.");
        } finally {
            viewed = previous; archive.Clear(); archive.AddRange(previousArchive); archiveChanged = true;
            character.Text = previousSearch; tabs.SelectedItem = previousTab; Refresh(); Layout();
        }
    }
}
