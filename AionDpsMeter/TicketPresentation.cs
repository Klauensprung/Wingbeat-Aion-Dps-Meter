namespace AionDpsMeter.Local;

internal sealed record TicketRow(int Id, string Name, string Balance, string Detail);
internal static class TicketPresentation
{
    internal static string Duration(long milliseconds)
    {
        if (milliseconds < 0) return "Unknown";
        long minutes = milliseconds / 60000;
        return $"{minutes / 60:N0}h {minutes % 60:00}m";
    }
    internal static string Bag(int id, InventoryHoldingSnapshot? holdings)
        => Bag(id, holdings?.Items.ToDictionary(i => i.Id, i => i.Count), holdings?.Complete == true);
    internal static string Bag(int id, IReadOnlyDictionary<int, decimal>? counts, bool complete)
    {
        var definition = TicketCatalogue.Default.Find(id);
        if (definition == null || definition.Items.Length == 0) return "";
        if (counts == null || counts.Count == 0 && !complete) return " | Bag: not captured";
        decimal amount = definition.Items.Sum(item => counts.GetValueOrDefault(item.Id) * item.Amount);
        string unit = definition.Max == 0 ? " seconds" : "";
        return $" | Bag: {amount:N0}{unit}" + (complete ? "" : " (partial)");
    }
    internal static TicketRow Row(CapturedContentTicket ticket, bool member, int server, long now)
    {
        var definition = TicketCatalogue.Default.Find(ticket.Id);
        string name = definition?.Name ?? $"Ticket {ticket.Id}";
        if (definition?.Max == 0)
            return new(ticket.Id, name, Duration(ticket.Time), $"Captured time · Extra: {Duration(ticket.ExtraTime)}");
        var estimate = definition == null ? new TicketEstimate(ticket.Count, null, null, null, 0) : TicketProjection.Estimate(ticket, definition, member, server, now);
        int? max = definition == null ? null : member && definition.MemberMax is > 0 ? definition.MemberMax : definition.Max;
        string balance = max is > 0 ? $"{estimate.Count:N0} / {max:N0}" : $"{estimate.Count:N0}";
        if (ticket.Extra != 0) balance += $" (+{ticket.Extra:N0})";
        string detail = $"Captured: {ticket.Count:N0}";
        if (estimate.NextRefillAt is long next) {
            detail += estimate.Count != ticket.Count ? " · Estimated now" : " · Refill estimate";
            detail += $" · Next in {Duration(Math.Max(60000, next - Math.Max(now, ticket.At)))}";
            if (estimate.FullAt is long full) detail += $" · Full in {Duration(Math.Max(60000, full - Math.Max(now, ticket.At)))}";
            else detail += " · At capacity";
        } else detail += " · Refill not estimated";
        return new(ticket.Id, name, balance, detail);
    }
}
