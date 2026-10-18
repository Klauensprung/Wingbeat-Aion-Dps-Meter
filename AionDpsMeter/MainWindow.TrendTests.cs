using System.Windows.Controls;
using System.Windows.Shapes;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void CheckTrendViews()
    {
        var savedArchive = archive.ToArray(); var savedView = viewed;
        try {
            archive.Clear();
            for (int i = 1; i <= 120; i++) {
                // Deliberately alternate servers; they must still produce one character choice.
                archive.Add(EncounterTrendTests.Attempt(i, "Training guardian", 30, 30000 + i * 300, 3000 + i * 30, i % 2 == 0 ? 2301 : 2302));
                if (i % 10 == 0) archive.Add(EncounterTrendTests.Attempt(i, "Training golem", 60, 90000 + i * 600, 6000));
            }
            foreach (var fight in archive) store.Save(fight);
            var compact = archive.Select(FightArchiveSummary.Create).ToArray();
            archive.Clear(); archive.AddRange(compact);
            archiveChanged = true; trendMetric.SelectedIndex = 0; RefreshTrends(); UpdateLayout();
            var grid = grids["Trends"];
            if (trendCharacter.Items.Count != 1 || trendCharacter.Items.OfType<TrendChoice>().Any(c => c.Label.Contains("server")) || grid.Items.Count != 2)
                throw new InvalidOperationException("Trends still split characters by server or failed to group encounters.");
            grid.SelectedItem = grid.Items.OfType<EncounterTrendRow>().Single(r => r.Encounter == "Training guardian");
            UpdateLayout();
            if (grid.SelectedItem is not EncounterTrendRow { Attempts: 120, Average: 1605, Latest: 2200 } ||
                trendChart.Children.OfType<Polyline>().Single().Points.Count != 100 || !trendChart.Children.OfType<Line>().Any(l => l.ToolTip?.ToString()?.Contains("120 attempts") == true))
                throw new InvalidOperationException("The encounter chart or all-attempt average is incorrect.");
            var averageColumn = grid.Columns.Single(c => c.SortMemberPath == "Average");
            averageColumn.Width = 125;
            for (int i = 0; i < 4; i++) RefreshTrends();
            if (grid.SelectedItem is not EncounterTrendRow { Encounter: "Training guardian" } || averageColumn.Width.Value != 125)
                throw new InvalidOperationException("Refreshing trends lost encounter selection or column sizing.");
            Render(this, "test-artifacts/Encounter-Trends-DPS.png");
            trendMetric.SelectedIndex = 1; RefreshTrends(); UpdateLayout();
            if (grid.SelectedItem is not EncounterTrendRow { Encounter: "Training guardian", Average: 160.5, Latest: 220 } || averageColumn.Header?.ToString() != "Average HPS")
                throw new InvalidOperationException("HPS mode lost encounter selection or retained DPS values.");
            Render(this, "test-artifacts/Encounter-Trends-HPS.png");
            grid.SelectedItem = grid.Items.OfType<EncounterTrendRow>().Single(r => r.Encounter == "Training golem");
            if (trendChart.Children.OfType<Polyline>().Single().Points.Count != 12 || trendChart.Children.OfType<Ellipse>().Any(p => !p.ToolTip!.ToString()!.Contains("Training golem")))
                throw new InvalidOperationException("Choosing an encounter retained another encounter's attempts.");
            // Check archive navigation through the callback shared by chart points and rows.
            string latest = ((EncounterTrendRow)grid.SelectedItem).FightId;
            OpenTrendFight(latest);
            if (viewed?.Id != latest || viewed.ArchiveSummary != null || viewed.Damage.Count == 0 || tabs.SelectedIndex != 0)
                throw new InvalidOperationException("Trend navigation did not load full detail from the compact archive.");
            tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(t => t.Header?.ToString() == "Trends");
            archive.Clear(); archiveChanged = true; RefreshTrends(); UpdateLayout();
            if (grid.Items.Count != 0 || trendChart.Children.OfType<Polyline>().Any() || trendCharacter.SelectedItem != null || !trendSummary.Text.StartsWith("No saved encounters"))
                throw new InvalidOperationException("Empty trend history retained stale encounters or chart data.");
        } finally {
            archive.Clear(); archive.AddRange(savedArchive); viewed = savedView; archiveChanged = true;
            trendMetric.SelectedIndex = 0; RefreshTrends(); UpdateLayout();
        }
    }
}
