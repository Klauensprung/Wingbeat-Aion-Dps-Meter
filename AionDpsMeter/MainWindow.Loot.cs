using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowLoot(bool liveOnly = false)
    {
        var saved = liveOnly ? null : viewed;
        var historical = saved == null ? null : LootLedger.FromFight(saved);
        var window = new Window { Title = global::AionDpsMeter.Local.Localization.T("Loot"), Owner = this, Width = 940, Height = 590, MinWidth = 650, MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var layout = new DockPanel { Margin = new(20) };
        var heading = new StackPanel();
        var filters = new ScrollViewer { Content = heading, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = window.Height * .55 };
        window.SizeChanged += (_, _) => filters.MaxHeight = Math.Max(150, window.ActualHeight * .55);
        DockPanel.SetDock(filters, Dock.Top); layout.Children.Add(filters);
        heading.Children.Add(UiSections.Heading("Loot", saved == null ? "Your pickups in this capture" : "Saved fight: " + saved.Label,
            "Loot comes from increases in your inventory marked as drops, gathering, boxes or quest rewards. Existing inventory and purchases are excluded. Sessions separate characters and gaps of 30 minutes. Older records may have an unknown map."));
        var session = new ComboBox { DisplayMemberPath = "Label", SelectedValuePath = "Id", Margin = new(0, 0, 0, 8) };
        var maps = new ComboBox { DisplayMemberPath = "Label", SelectedValuePath = "Id", Margin = new(0, 0, 0, 8) };
        var search = new TextBox { Tag = "Item, map or character...", ToolTip = global::AionDpsMeter.Local.Localization.T("Search item, map, ID or character"), Margin = new(0, 0, 0, 8) };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) };
        var filterRow = new WrapPanel();
        filterRow.Children.Add(UiSections.Field("Session", session, 250));
        filterRow.Children.Add(UiSections.Field("Map", maps, 220));
        filterRow.Children.Add(UiSections.Field("Search pickups", search, 260));
        heading.Children.Add(filterRow); heading.Children.Add(status);
        var table = new LiveTable { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
            EnableRowVirtualization = true, EnableColumnVirtualization = true, RowHeight = 36 };
        void Column(string title, string property, int width, string? format = null) => table.Columns.Add(new DataGridTextColumn {
            Header = title, Binding = new Binding(property) { StringFormat = format }, Width = width });
        Column("Item", "Item", 250); Column("Quantity", "Quantity", 95, "N0"); Column("Pickups", "Pickups", 80, "N0");
        Column("Character", "Character", 150); Column("First pickup", "FirstTime", 160); Column("Last pickup", "LastTime", 160);
        Column("Map", "Map", 190); Column("Item ID", "ItemId", 100); Column("Map ID", "MapId", 80);
        LootItemRow[] allRows = [];
        LootLedger? lastLedger = null; long revision = -1, omittedItems = 0; int omittedSessions = 0; bool updating = false;
        void Filter() {
            if (updating) return;
            int selected = session.SelectedValue is int id ? id : 0;
            int selectedMap = maps.SelectedValue is int mapId ? mapId : -1;
            string query = search.Text.Trim();
            var rows = allRows.Where(r => (selected == 0 || r.Session == selected) && (selectedMap == -1 || r.MapId == selectedMap) &&
                (r.Item.Contains(query, StringComparison.OrdinalIgnoreCase) || r.ItemId.ToString().Contains(query) || r.Character.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 r.Map.Contains(query, StringComparison.OrdinalIgnoreCase) || r.MapId.ToString().Contains(query))).ToArray();
            table.SetRows(rows);
            status.Text = allRows.Length == 0 ? global::AionDpsMeter.Local.Localization.T("No loot captured yet. Start capture before entering your character, then collect loot or claim a reward.") :
                global::AionDpsMeter.Local.Localization.F($"{rows.Sum(r => (decimal)r.Quantity):N0} items · {rows.Sum(r => (long)r.Pickups):N0} pickups · {rows.Select(r => r.ItemId).Distinct().Count():N0} item types");
            if (omittedItems > 0 || omittedSessions > 0) status.Text += global::AionDpsMeter.Local.Localization.F($" · Memory limit: {omittedItems:N0} item entries and {omittedSessions:N0} older sessions omitted from this view.");
            if (ItemNames.Default.LoadError is { } error) status.Text += " · " + error;
            if (ZoneNames.Default.LoadError is { } mapError) status.Text += " · " + mapError;
        }
        void Update() {
            lock (engine.Gate) {
                var ledger = historical ?? engine.Loot;
                if (ReferenceEquals(ledger, lastLedger) && ledger.Revision == revision) return;
                lastLedger = ledger; revision = ledger.Revision; allRows = ledger.Rows(); omittedItems = ledger.OmittedItems; omittedSessions = ledger.OmittedSessions;
            }
            int selected = session.SelectedValue is int id ? id : 0;
            updating = true;
            var options = new[] { new LootSessionChoice(0, "All sessions") }.Concat(allRows.GroupBy(r => r.Session).Select(g =>
                new LootSessionChoice(g.Key, $"Session {g.Key} · {g.First().Character} · {DateTimeOffset.FromUnixTimeMilliseconds(g.First().SessionStart).ToLocalTime():dd MMM HH:mm}"))).ToArray();
            session.ItemsSource = options; session.SelectedValue = options.Any(o => o.Id == selected) ? selected : 0;
            int selectedMap = maps.SelectedValue is int mapId ? mapId : -1;
            var mapOptions = new[] { new LootSessionChoice(-1, "All maps") }.Concat(allRows.GroupBy(r => r.MapId).OrderBy(g => g.Key)
                .Select(g => new LootSessionChoice(g.Key, g.Key == 0 ? g.First().Map : $"{g.First().Map} ({g.Key})"))).ToArray();
            maps.ItemsSource = mapOptions; maps.SelectedValue = mapOptions.Any(m => m.Id == selectedMap) ? selectedMap : -1;
            updating = false; Filter();
        }
        session.SelectionChanged += (_, _) => Filter(); search.TextChanged += (_, _) => Filter();
        maps.SelectionChanged += (_, _) => Filter();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; timer.Tick += (_, _) => Update();
        window.Closed += (_, _) => timer.Stop();
        layout.Children.Add(table); window.Content = layout; Update(); if (saved == null) timer.Start(); window.Show();
    }
    sealed record LootSessionChoice(int Id, string Label);
}
