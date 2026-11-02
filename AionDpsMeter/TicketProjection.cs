namespace AionDpsMeter.Local;

internal sealed record TicketEstimate(long Count, long? NextRefillAt, long? FullAt, long? LossAt, long Lost);

internal static class TicketProjection
{
    // Estimates use reset-aligned catalogue rules. Captured ticket timers describe
    // separate allowances and never become refill deadlines here.
    internal static TicketEstimate Estimate(CapturedContentTicket ticket, TicketDefinition definition, bool member, int server, long now)
    {
        var unchanged = new TicketEstimate(ticket.Count, null, null, null, 0);
        var rule = definition.Refill;
        if (rule == null || ticket.At < 0 || now < 0 || rule.EverySeconds <= 0) return unchanged;
        long current = Math.Max(now, ticket.At), interval = (long)rule.EverySeconds * 1000;
        long period = rule.Weekly ? 604800000 : 86400000;
        if (interval > period || period % interval != 0) return unchanged;
        long? Reset(long at) => rule.Weekly ? ChecklistTasks.WeeklyReset(server, at) : ChecklistTasks.DailyReset(server, at);
        if (Reset(ticket.At) is not long seenReset || Reset(current) is not long reset) return unchanged;
        long amount = member ? rule.MemberAmount : rule.Amount;
        long max = member && definition.MemberMax is > 0 ? definition.MemberMax.Value : definition.Max;
        if (amount <= 0 || max <= 0 || ticket.Count < 0) return unchanged;
        long first = seenReset - period + ((ticket.At - (seenReset - period)) / interval + 1) * interval;
        long steps = current < first ? 0 : (current - first) / interval + 1;
        long needed = Math.Max(0, (max - ticket.Count + amount - 1) / amount);
        long count = ticket.Count >= max ? ticket.Count : Math.Min(max, ticket.Count + Math.Min(steps, needed) * amount);
        long next = reset - period + ((current - (reset - period)) / interval + 1) * interval;
        if (count >= max) return new(count, next, null, next, amount);
        long remaining = (max - count + amount - 1) / amount;
        long full = next + (remaining - 1) * interval, excess = count + remaining * amount - max;
        return new(count, next, full, excess > 0 ? full : full + interval, excess > 0 ? excess : amount);
    }
}
