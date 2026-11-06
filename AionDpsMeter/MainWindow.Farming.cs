using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowFarming()
    {
        var window = new Window { Title = global::AionDpsMeter.Local.Localization.T("Farming tracking"), Owner = this, Width = 720, Height = 500, MinWidth = 540, MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var layout = new DockPanel { Margin = new(18) }; var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        heading.Children.Add(UiSections.Heading("Farming tracking", "Saved on this PC",
            "Totals across characters since each counter was started. Kills require your party to engage an observed NPC and a captured death signal. Nearby kills and ordinary despawns do not count. Gathering requires a rewarded completion. Loot counts acquired item quantities from captured inventory changes, not purchases or inventory lists. Pet souls count explicit gains, excluding login balances and spending. Pet registration and level-up totals exclude existing pets in login rosters. DPS resets and deleted fight history do not change these totals. The category reset affects only that counter; Reset all starts every farming counter together. Refresh for new activity."));
        var controls = new WrapPanel { Margin = new(0, 0, 0, 10) };
        var category = new ComboBox { ItemsSource = new[] { "Kills", "Gathering", "Loot", "Pet souls", "Pets registered", "Pet level-ups" }, SelectedIndex = 0, Width = 150, Margin = new(0, 0, 8, 0) };
        var refresh = new Button { Content = global::AionDpsMeter.Local.Localization.T("Refresh"), Margin = new(0, 0, 8, 0) };
        var reset = new Button { Content = global::AionDpsMeter.Local.Localization.T("Reset kills"), Margin = new(0, 0, 8, 0) };
        var resetAll = new Button { Content = global::AionDpsMeter.Local.Localization.T("Reset all"), ToolTip = global::AionDpsMeter.Local.Localization.T("Reset all six farming totals. Current fight, inventory and pet progress are kept.") };
        controls.Children.Add(category); controls.Children.Add(refresh); controls.Children.Add(reset); controls.Children.Add(resetAll); heading.Children.Add(controls);
        var search = new TextBox { Tag = "Search name or ID...", ToolTip = global::AionDpsMeter.Local.Localization.T("Search NPC, gathering object, item or pet name and ID"), Margin = new(0, 0, 0, 10) }; heading.Children.Add(search);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) }; heading.Children.Add(status);
        var table = new LiveTable { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, EnableRowVirtualization = true, RowHeight = 36 };
        var nameStyle = new Style(typeof(TextBlock));
        nameStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        nameStyle.Setters.Add(new Setter(TextBlock.ToolTipProperty, new Binding("Name")));
        table.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding("Name"), ElementStyle = nameStyle, Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 240 });
        table.Columns.Add(new DataGridTextColumn { Header = "ID", Binding = new Binding("Id"), Width = 110 });
        table.Columns.Add(new DataGridTextColumn { Header = "Count", Binding = new Binding("Count") { StringFormat = "N0" }, Width = 100 });
        FarmingCounterLedger Selected() => category.SelectedIndex switch { 1 => gatheringTracking, 2 => lootTracking, 3 => soulTracking, 4 => petRegistrationTracking, 5 => petLevelTracking, _ => killTracking };
        void RefreshRows() {
            bool gathering = category.SelectedIndex == 1, loot = category.SelectedIndex == 2, souls = category.SelectedIndex >= 3;
            var ledger = Selected(); var data = ledger.Snapshot(); string query = search.Text.Trim();
            var rows = data.Counts.Select(p => new { Id = p.Key, Name = gathering ? WorldObjectNames.Default.Find(p.Key) : loot ? ItemNames.Default.Find(p.Key) : souls ? PetCatalogue.Default.Find(p.Key)?.Name ?? $"Pet {p.Key}" : NpcNames.Default.GetName(p.Key), Count = p.Value })
                .Where(r => query.Length == 0 || r.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || r.Id.ToString().Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.Count).ThenBy(r => r.Name).ToArray();
            table.SetRows(rows); reset.Content = gathering ? global::AionDpsMeter.Local.Localization.T("Reset gathers") : loot ? global::AionDpsMeter.Local.Localization.T("Reset loot") : souls ? category.SelectedIndex switch { 4 => global::AionDpsMeter.Local.Localization.T("Reset registrations"), 5 => global::AionDpsMeter.Local.Localization.T("Reset levels"), _ => global::AionDpsMeter.Local.Localization.T("Reset souls") } : global::AionDpsMeter.Local.Localization.T("Reset kills");
            status.Text = $"{data.Counts.Values.Sum(v => (decimal)v):N0} " + (gathering ? global::AionDpsMeter.Local.Localization.T("confirmed gathers") : loot ? global::AionDpsMeter.Local.Localization.T("items acquired") : souls ? category.SelectedIndex switch { 4 => global::AionDpsMeter.Local.Localization.T("pets registered"), 5 => global::AionDpsMeter.Local.Localization.T("pet level-ups"), _ => global::AionDpsMeter.Local.Localization.T("pet souls gained") } : global::AionDpsMeter.Local.Localization.T("party-engaged kills")) +
                global::AionDpsMeter.Local.Localization.F($" since {DateTimeOffset.FromUnixTimeMilliseconds(data.Since).ToLocalTime():g}. Showing {rows.Length:N0} entries.");
            if (data.Counts.Count == 0) status.Text += global::AionDpsMeter.Local.Localization.T(" No activity tracked yet.");
            else if (rows.Length == 0) status.Text += global::AionDpsMeter.Local.Localization.T(" No matches.");
            if (data.Omitted > 0) status.Text += global::AionDpsMeter.Local.Localization.F($" {data.Omitted:N0} units omitted at tracking limits.");
            if (ledger.LoadError != null) status.Text += " " + ledger.LoadError;
        }
        category.SelectionChanged += (_, _) => RefreshRows(); search.TextChanged += (_, _) => RefreshRows();
        refresh.Click += (_, _) => RefreshRows();
        reset.Click += (_, _) => {
            var ledger = Selected(); ledger.Reset(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()); RefreshRows();
            try { ledger.Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { status.Text += global::AionDpsMeter.Local.Localization.T(" Reset could not be saved yet; autosave will retry."); }
        };
        resetAll.Click += (_, _) => {
            engine.ResetFarmingTracking(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()); RefreshRows();
            int failed = 0;
            foreach (var ledger in new[] { killTracking, gatheringTracking, lootTracking, soulTracking, petRegistrationTracking, petLevelTracking }) {
                try { ledger.Save(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed++; }
            }
            if (failed > 0) status.Text += global::AionDpsMeter.Local.Localization.F($" {failed} counter resets could not be saved yet; autosave will retry.");
        };
        RefreshRows(); layout.Children.Add(table); window.Content = layout; window.Show();
    }
}
