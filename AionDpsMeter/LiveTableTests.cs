using System.ComponentModel;
using System.Windows.Controls;

namespace AionDpsMeter.Local;

internal static class LiveTableTests
{
    internal static void Check(LiveTable grid, Action layout)
    {
        var original = grid.ItemsSource.Cast<object>().ToArray();
        var selectionMode = grid.SelectionMode;
        var widths = grid.Columns.Select(c => c.Width).ToArray();
        try
        {
            SkillRow[] Data(int count, int tick) => Enumerable.Range(0, count).Select(i =>
                new SkillRow("Player " + i, tick % 2 == 0 ? "Skill " + i : "A much longer skill name " + i, 17010000 + i,
                    tick + 1, 1000000L * tick + i, 100 + i + tick, 20)).ToArray();
            grid.SetRows(Data(150, 0)); layout();
            var source = grid.ItemsSource; var columns = grid.Columns.ToArray();
            var custom = columns[0]; custom.Width = new DataGridLength(187); custom.DisplayIndex = columns.Length - 1;
            grid.Items.SortDescriptions.Add(new(nameof(SkillRow.Damage), ListSortDirection.Descending));
            grid.SelectedItem = grid.ItemsSource.Cast<SkillRow>().Single(r => r.SkillCode == 17010072);
            // Column reordering schedules width calculation on the dispatcher.
            // Establish the scroll baseline after that work, just as a user who
            // finishes dragging a header before the next capture update would.
            void Settle() { layout(); grid.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded); layout(); }
            Settle(); var scroll = LiveTable.FindScroll(grid)!;
            scroll.ScrollToHorizontalOffset(40); scroll.ScrollToVerticalOffset(450); Settle();
            double horizontal = scroll.HorizontalOffset, vertical = scroll.VerticalOffset, width = grid.ActualWidth;
            var columnWidths = columns.Select(c => c.ActualWidth).ToArray();
            for (int tick = 1; tick <= 20; tick++)
            {
                grid.SetRows(Data(tick % 2 == 0 ? 150 : 180, tick)); layout();
                if (!ReferenceEquals(source, grid.ItemsSource) || !columns.SequenceEqual(grid.Columns)) throw new Exception("Capture update regenerated table columns or data source.");
                if (custom.Width.Value != 187 || custom.DisplayIndex != columns.Length - 1 || grid.Items.SortDescriptions.Count != 1)
                    throw new Exception("Table update lost user width, column order or sorting.");
                if (grid.SelectedItem is not SkillRow { SkillCode: 17010072 } selected || selected.Hits != tick + 1)
                    throw new Exception("Table update lost selection or retained stale values.");
                if (Math.Abs(scroll.HorizontalOffset - horizontal) > 1 || Math.Abs(scroll.VerticalOffset - vertical) > 1 || Math.Abs(grid.ActualWidth - width) > 1)
                    throw new Exception($"Updating table shifted its viewport at update {tick}: horizontal {horizontal:N1} -> {scroll.HorizontalOffset:N1}, vertical {vertical:N1} -> {scroll.VerticalOffset:N1}, width {width:N1} -> {grid.ActualWidth:N1}.");
                if (columns.Where((c, i) => Math.Abs(c.ActualWidth - columnWidths[i]) > 1).Any()) throw new Exception("Live values changed column widths.");
            }
            // The Encounter grids use Single, unlike the older analysis tables.
            grid.SelectedItem = null;
            grid.SelectionMode = DataGridSelectionMode.Single;
            grid.SetRows(Data(150, 0));
            grid.SelectedItem = grid.ItemsSource.Cast<SkillRow>().Single(r => r.SkillCode == 17010072);
            for (int tick = 1; tick <= 20; tick++) {
                grid.SetRows(Data(150, tick).Reverse()); layout();
                if (grid.SelectedItem is not SkillRow { SkillCode: 17010072 } selected || selected.Hits != tick + 1 || grid.SelectedItems.Count != 1)
                    throw new Exception("Single selection was lost during changing values and row reordering.");
            }
            grid.SetRows(Data(1, 22)); layout();
            if (grid.SelectedItem != null) throw new Exception("Removing a selected row selected an unrelated row.");
            grid.SetRows(Data(150, 23)); layout();
            if (grid.SelectedItem != null) throw new Exception("A cleared selection returned during refresh.");
            grid.SelectionMode = DataGridSelectionMode.Extended;
            grid.SelectedItems.Add(grid.ItemsSource.Cast<SkillRow>().Single(r => r.SkillCode == 17010002));
            grid.SelectedItems.Add(grid.ItemsSource.Cast<SkillRow>().Single(r => r.SkillCode == 17010072));
            grid.SetRows(Data(150, 24).Reverse()); layout();
            if (!grid.SelectedItems.Cast<SkillRow>().Select(r => r.SkillCode).Order().SequenceEqual(new[] { 17010002, 17010072 }))
                throw new Exception("Multiple selection was lost during refresh.");
            foreach (int count in new[] { 1, 150, 0, 150 })
            {
                grid.SetRows(Data(count, 25)); layout();
                if (!ReferenceEquals(source, grid.ItemsSource) || !columns.SequenceEqual(grid.Columns) || Math.Abs(grid.ActualWidth - width) > 1)
                    throw new Exception("Scrollbar threshold or empty table changed its layout/schema.");
            }
        }
        finally
        {
            grid.Items.SortDescriptions.Clear(); grid.UnselectAll(); grid.SelectionMode = selectionMode;
            grid.SetRows(original);
            for (int i = 0; i < grid.Columns.Count; i++) { grid.Columns[i].Width = widths[i]; grid.Columns[i].DisplayIndex = i; }
            var scroll = LiveTable.FindScroll(grid); scroll?.ScrollToHorizontalOffset(0); scroll?.ScrollToVerticalOffset(0); layout();
        }
    }
}
