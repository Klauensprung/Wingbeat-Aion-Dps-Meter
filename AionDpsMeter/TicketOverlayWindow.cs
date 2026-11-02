using System.Windows;
using System.Windows.Controls;

namespace AionDpsMeter.Local;

internal sealed class TicketOverlayWindow : Window
{
    readonly Preferences preferences;
    readonly Action? save;
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
    readonly StackPanel rows = new() { Name = "TicketRows" };
    readonly CheckBox membership = new() { Content = "Membership benefits", Margin = new(0, 4, 0, 8), ToolTip = "Use membership capacity and refill amounts for estimates." };
    readonly TextBox search = new() { Margin = new(0, 0, 0, 8), ToolTip = "Filter tickets by name or ID" };
    readonly Button choose = new() { Content = "Choose tickets", Margin = new(0, 8, 0, 0), Padding = new(8, 4, 8, 4), HorizontalAlignment = HorizontalAlignment.Left };
    readonly TextBlock empty = new() { TextWrapping = TextWrapping.Wrap };
    CapturedContentTicket[] tickets = [];
    TicketRow[] previous = [];
    InventoryHoldingSnapshot? holdings;
    int server;
    bool complete;
    long now;
    internal TicketOverlayWindow(Preferences preferences, Action? save = null)
    {
        this.preferences = preferences; this.save = save;
        Title = "Wingbeat tickets"; MinWidth = 440; MinHeight = 340;
        Width = double.IsFinite(preferences.TicketWidth) ? Math.Clamp(preferences.TicketWidth, MinWidth, Math.Max(MinWidth, SystemParameters.VirtualScreenWidth)) : 540;
        Height = double.IsFinite(preferences.TicketHeight) ? Math.Clamp(preferences.TicketHeight, MinHeight, Math.Max(MinHeight, SystemParameters.VirtualScreenHeight)) : 480;
        if (preferences.TicketLeft is double left && preferences.TicketTop is double top && double.IsFinite(left) && double.IsFinite(top)) {
            Left = Math.Clamp(left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + Math.Max(0, SystemParameters.VirtualScreenWidth - Width));
            Top = Math.Clamp(top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + Math.Max(0, SystemParameters.VirtualScreenHeight - Height));
        }
        Topmost = true; ShowInTaskbar = false; ShowActivated = false; UiTheme.Apply(this, preferences);
        var layout = new DockPanel { Margin = new(12) };
        DockPanel.SetDock(choose, Dock.Bottom); layout.Children.Add(choose); choose.Click += Choose;
        void AddTop(UIElement control) { DockPanel.SetDock(control, Dock.Top); layout.Children.Add(control); }
        AddTop(new TextBlock { Text = "TICKETS & REFILLS", FontWeight = FontWeights.SemiBold }); AddTop(status);
        membership.IsChecked = preferences.TicketMember; membership.Click += MembershipChanged; AddTop(membership);
        AddTop(new TextBlock { Text = "Find a ticket", Margin = new(0, 0, 0, 4) }); AddTop(search); search.TextChanged += SearchChanged;
        AddTop(empty);
        layout.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = layout;
        void SavePlacement() {
            if (WindowState != WindowState.Normal || !double.IsFinite(Left) || !double.IsFinite(Top)) return;
            preferences.TicketLeft = Left; preferences.TicketTop = Top; preferences.TicketWidth = Width; preferences.TicketHeight = Height; save?.Invoke();
        }
        if (save != null) OverlayPlacement.Track(this, SavePlacement);
        Closed += (_, _) => {
            if (IsLoaded) SavePlacement();
            membership.Click -= MembershipChanged; search.TextChanged -= SearchChanged;
            choose.Click -= Choose;
            if (choose.ContextMenu is { } menu) { menu.IsOpen = false; menu.Items.Clear(); menu.PlacementTarget = null; }
            choose.ContextMenu = null;
            tickets = []; previous = []; holdings = null; rows.Children.Clear(); layout.Children.Clear(); Content = null;
        };
    }
    void MembershipChanged(object sender, RoutedEventArgs e) { preferences.TicketMember = membership.IsChecked == true; save?.Invoke(); RenderRows(); }
    void SearchChanged(object sender, TextChangedEventArgs e) => RenderRows();
    void Choose(object sender, RoutedEventArgs e)
    {
        var menu = UiTheme.Menu(this);
        var all = new MenuItem { Header = "Show all captured tickets", IsCheckable = true, IsChecked = preferences.TicketShown == null };
        all.Click += (_, _) => { preferences.TicketShown = null; save?.Invoke(); RenderRows(); }; menu.Items.Add(all);
        menu.Items.Add(new Separator());
        var ids = TicketCatalogue.Default.Definitions.Select(t => t.Id).Concat(tickets.Select(t => t.Id))
            .Concat(preferences.TicketShown ?? []).Where(id => id > 0).Distinct().Order().Take(4096).ToArray();
        foreach (int id in ids) {
            var item = new MenuItem { Header = $"{TicketCatalogue.Default.Find(id)?.Name ?? "Unknown ticket"} ({id})", Tag = id,
                IsCheckable = true, IsChecked = preferences.TicketShown?.Contains(id) ?? tickets.Any(t => t.Id == id) };
            item.Click += (_, _) => {
                var selected = (preferences.TicketShown ?? tickets.Select(t => t.Id).ToArray()).Where(i => i > 0).Take(4096).ToHashSet();
                if (item.IsChecked) selected.Add(id); else selected.Remove(id);
                preferences.TicketShown = selected.Order().Take(4096).ToArray(); save?.Invoke(); RenderRows();
            };
            menu.Items.Add(item);
        }
        choose.ContextMenu = menu; menu.PlacementTarget = choose; menu.IsOpen = true;
    }
    void RenderRows()
    {
        var bagCounts = holdings?.Items.ToDictionary(i => i.Id, i => i.Count);
        var selected = preferences.TicketShown?.Where(id => id > 0).Distinct().Take(4096).ToHashSet();
        var captured = tickets.Where(t => selected == null || selected.Contains(t.Id)).Select(t => TicketPresentation.Row(t, preferences.TicketMember, server, now));
        var missing = selected?.Except(tickets.Select(t => t.Id)).Select(id => new TicketRow(id, TicketCatalogue.Default.Find(id)?.Name ?? $"Ticket {id}",
            "Not captured", complete ? "Not in the captured list" : "Waiting for current-character data")) ?? [];
        var displayed = captured.Concat(missing).OrderBy(t => t.Id)
            .Select(t => t with { Detail = t.Detail + TicketPresentation.Bag(t.Id, bagCounts, holdings?.Complete == true) })
            .Where(t => t.Name.Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase) || t.Id.ToString().Contains(search.Text.Trim())).ToArray();
        choose.Content = selected == null ? "Choose tickets - All captured" : $"Choose tickets - {selected.Count} selected";
        empty.Text = displayed.Length > 0 ? "" : selected?.Count == 0 ? "No tickets selected. Use Choose tickets to add entries." :
            search.Text.Trim().Length > 0 ? "No tickets match this search." : "";
        if (previous.SequenceEqual(displayed)) return;
        previous = displayed; rows.Children.Clear();
        foreach (var ticket in displayed) {
            var block = new StackPanel { Margin = new(0, 0, 0, 12), Tag = ticket.Id };
            block.Children.Add(new TextBlock { Text = ticket.Name, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, ToolTip = $"Ticket {ticket.Id}" });
            block.Children.Add(new TextBlock { Text = ticket.Balance, Margin = new(0, 3, 0, 3) });
            block.Children.Add(new TextBlock { Text = ticket.Detail, TextWrapping = TextWrapping.Wrap, FontSize = 11 });
            rows.Children.Add(block);
        }
    }
    internal void Update(CapturedContentTicket[] values, bool complete, bool capturing, string? character, int server, long now, InventoryHoldingSnapshot? holdings = null)
    {
        this.complete = complete; this.holdings = holdings; this.server = server; this.now = now; tickets = values.ToArray();
        status.Text = (capturing ? "Live capture" : "Capture stopped — last captured state") + (character == null ? "" : " · " + character) + "\n" +
            (values.Length == 0 ? complete ? "No tickets in the captured list." : "No tickets captured for the current character. Enter the game with capture running." :
            (complete ? "Complete captured list." : "Partial captured list.") + " Refills are estimates; bonus counts stay separate.");
        RenderRows();
    }
}
