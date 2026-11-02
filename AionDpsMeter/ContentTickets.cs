namespace AionDpsMeter.Local;

internal sealed record ContentTicket(int Id, int Count, int Extra, long Time, long ExtraTime);
internal sealed record ContentTicketReading(bool Full, ContentTicket[] Tickets);
internal sealed record CapturedContentTicket(int Id, int Count, int Extra, long Time, long ExtraTime, long At);

// Current owner state only: timers are preserved as transmitted, not guessed
// to be wall-clock deadlines or decremented without verified timer semantics.
internal sealed class ContentTickets
{
    const int Capacity = 4096;
    readonly Dictionary<int, CapturedContentTicket> values = new();
    long latest = -1;
    internal bool Complete { get; private set; }
    internal CapturedContentTicket[] Snapshot() => values.Values.OrderBy(t => t.Id).ToArray();
    internal void Clear() { values.Clear(); latest = -1; Complete = false; }
    internal void InheritMissing(ContentTickets previous)
    {
        if (Complete || ReferenceEquals(this, previous) || values.Count + previous.values.Keys.Count(k => !values.ContainsKey(k)) > Capacity) return;
        foreach (var pair in previous.values) values.TryAdd(pair.Key, pair.Value);
        latest = Math.Max(latest, previous.latest); Complete = previous.Complete;
    }
    internal bool Apply(ContentTicketReading reading, long at)
    {
        if (at < 0 || at < latest || reading.Tickets is not { Length: <= 1000 } ||
            reading.Tickets.Any(t => t == null || t.Id <= 0 || t.Count < 0 || t.Extra < 0)) return false;
        if (!reading.Full && values.Count + reading.Tickets.Select(t => t.Id).Distinct().Count(id => !values.ContainsKey(id)) > Capacity) return false;
        if (reading.Full) { values.Clear(); Complete = true; }
        foreach (var ticket in reading.Tickets)
            values[ticket.Id] = new(ticket.Id, ticket.Count, ticket.Extra, ticket.Time, ticket.ExtraTime, at);
        latest = at;
        return true;
    }
}
