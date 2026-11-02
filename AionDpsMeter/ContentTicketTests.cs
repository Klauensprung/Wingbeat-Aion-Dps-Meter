using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class ContentTicketTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var tracker = new ContentTickets(); long at = 0;
        using var output = new StreamWriter(Path.Combine(temp, "reference-content-tickets.jsonl"));
        void Apply(bool full, params ContentTicket[] tickets) {
            var reading = new ContentTicketReading(full, tickets);
            tracker.Apply(reading, ++at);
            output.WriteLine(JsonSerializer.Serialize(new { Reading = reading, At = at, Expected = tracker.Snapshot(), Complete = tracker.Complete }));
        }
        Apply(false, new ContentTicket(60000002, 3, 5, 1234567890000, -1));
        check(!tracker.Complete && tracker.Snapshot() is [{ Id: 60000002, Time: 1234567890000, ExtraTime: -1 }], "Partial non-energy tickets preserve both signed timer fields without claiming completeness");
        var detached = tracker.Snapshot();
        Apply(true, new(60000001, 840, 120, 10000, 20000), new(60000003, 0, 0, 0, 0));
        check(tracker.Complete && tracker.Snapshot().Length == 2 && detached[0].Count == 3, "Full ticket list replaces omitted types and detached snapshots stay immutable");
        Apply(false, new(60000001, 1, 2, 10, 20), new(60000001, 4, 5, 30, 40));
        check(tracker.Snapshot()[0].Count == 4 && tracker.Snapshot().Length == 2, "Ticket delta keeps other types and duplicate IDs use the final delivered reading");
        var random = new Random(6011);
        for (int i = 0; i < 200; i++) Apply(i % 23 == 0, new ContentTicket(60000001 + i % 9, random.Next(900), random.Next(200), random.NextInt64(0, 2000000000000), random.NextInt64(-1, 2000000000000)));
        Apply(true);
        check(tracker.Complete && tracker.Snapshot().Length == 0, "Empty full ticket list is a complete empty baseline");
        check(!tracker.Apply(new(false, [new(1, -1, 0, 0, 0)]), ++at) && tracker.Snapshot().Length == 0, "Invalid ticket update is rejected atomically");
        var owner = new Actor(1, "Ticket owner", true, false, 0, "Cleric", 2301) { PersistentId = 6000000001 };
        var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: owner));
        void Send(long timestamp, object fields, string kind = "content-tickets") => engine.Apply(new("network", Network: new(kind, timestamp, "test", JsonSerializer.SerializeToElement(fields))));
        Send(1, new ContentTicketReading(true, [new(60000002, 4, 2, 10000, 20000)]));
        Send(0, new ContentTicketReading(true, [])); engine.NewFight("Reset");
        check(engine.CurrentTickets() is [{ Count: 4 }] && engine.CurrentTicketsComplete && engine.Fight.Network.Count == 0,
            "Owner ticket state ignores stale lists and survives DPS reset without raw event retention");
        Send(2, new { MapId = 1010, AwaitingIdentity = true }, "local-zone");
        Send(3, new ContentTicketReading(false, [new(60000003, 7, 0, 0, 0)]));
        check(engine.CurrentTickets().Length == 0 && !engine.CurrentTicketsComplete, "Pending ticket state is hidden from the previous owner");
        engine.Apply(new("local-player", Actor: owner with { PersistentId = 10294967297 }));
        check(engine.CurrentTickets() is [{ Id: 60000003, Count: 7 }] && !engine.CurrentTicketsComplete,
            "Confirmed new owner receives only pending tickets and their actual completeness");
        var bounded = new ContentTickets();
        for (int i = 1; i <= 4096; i++) bounded.Apply(new(false, [new(i, 1, 0, 0, 0)]), i);
        check(!bounded.Apply(new(false, [new(4097, 1, 0, 0, 0)]), 4097) && bounded.Snapshot().Length == 4096,
            "Distinct ticket state is bounded without discarding established balances");
        bounded.Clear(); check(!bounded.Complete && bounded.Snapshot().Length == 0, "Owner reset clears ticket state and completeness");
    }
}
