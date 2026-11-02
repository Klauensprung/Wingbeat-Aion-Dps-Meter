namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    TicketOverlayWindow? ticketOverlay;
    void ToggleTicketOverlay()
    {
        if (ticketOverlay != null) { ticketOverlay.Close(); return; }
        var window = new TicketOverlayWindow(preferences, SavePreferences); ticketOverlay = window;
        window.Closed += (_, _) => { if (ReferenceEquals(ticketOverlay, window)) ticketOverlay = null; };
        RefreshCooldownVisibility();
    }
    void RefreshTicketOverlay(bool allowed)
    {
        if (ticketOverlay == null) return;
        CapturedContentTicket[] tickets; InventoryHoldingSnapshot holdings; bool complete; string? name; int server;
        lock (engine.Gate) {
            tickets = engine.CurrentTickets(); holdings = engine.CurrentHoldings(); complete = engine.CurrentTicketsComplete;
            var owner = engine.Fight.Actors.GetValueOrDefault(engine.Fight.LocalPlayer);
            name = tickets.Length > 0 || complete ? owner?.Name : null; server = owner?.Server ?? 0;
        }
        ticketOverlay.Update(tickets, complete, capture != null, name, server, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), holdings);
        if (allowed && !ticketOverlay.IsVisible) ticketOverlay.Show();
        else if (!allowed && ticketOverlay.IsVisible) ticketOverlay.Hide();
    }
}
