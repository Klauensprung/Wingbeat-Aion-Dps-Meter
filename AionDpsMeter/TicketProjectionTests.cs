using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class TicketProjectionTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var catalogue = TicketCatalogue.Default;
        check(catalogue.LoadError == null && catalogue.Count == 73 && catalogue.Find(60000001) is { Name: "Odyle Energy", Max: 560, MemberMax: 840 },
            "Local ticket catalogue loads factual names and separate membership limits");
        var energy = catalogue.Find(60000001)!;
        long reset = new DateTimeOffset(2026, 11, 1, 7, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var ticket = new CapturedContentTicket(60000001, 550, 300, 123, 456, reset - 1);
        var result = TicketProjection.Estimate(ticket, energy, false, 2301, reset);
        check(result is { Count: 560, FullAt: null, Lost: 15 } && result.NextRefillAt == reset + 10800000,
            "Refill occurs exactly at reset and caps the estimate without adding bonus counts or packet timers");
        var member = TicketProjection.Estimate(ticket, energy, true, 2301, reset);
        check(member.Count == 565 && member.FullAt != null && ticket.Count == 550, "Membership uses its own cap and projection never mutates captured counts");
        check(TicketProjection.Estimate(ticket, energy, false, 0, reset).NextRefillAt == null &&
            TicketProjection.Estimate(ticket, energy, false, 2301, ticket.At - 5000).Count == 550,
            "Unknown region disables refill estimates and a backwards clock cannot invent elapsed refills");
        check(TicketPresentation.Row(new(3, 0, 0, 18227000, 25200000, reset), false, 2301, reset).Balance == "5h 03m" &&
            TicketPresentation.Row(new(3, 0, 0, 18227000, 25200000, reset), false, 2301, reset + 60000).Balance == "5h 03m",
            "Abyss allowance displays captured duration without decrementing it as a refill countdown");
        check(TicketPresentation.Row(new(999999999, 4, 2, 0, 0, reset), false, 0, reset) is { Name: "Ticket 999999999", Balance: "4 (+2)" },
            "Uncatalogued ticket retains ID, captured count and bonus without guessed refill rules");
        var random = new Random(6012);
        using var output = new StreamWriter(Path.Combine(temp, "reference-ticket-projection.jsonl"));
        int[] ids = [60000001, 1, 4, 7, 8, 10, 14, 101];
        for (int i = 0; i < 600; i++) {
            var definition = catalogue.Find(ids[i % ids.Length])!;
            long seen = reset + random.NextInt64(-604800000, 604800000);
            long now = seen + random.NextInt64(-600000, 1814400000);
            bool isMember = i % 2 == 0; int server = i % 3 == 0 ? 1101 : 2301;
            var value = new CapturedContentTicket(definition.Id, random.Next(0, definition.Max + 100), 99, 0, 0, seen);
            output.WriteLine(JsonSerializer.Serialize(new { Ticket = value, Definition = definition, Member = isMember, Server = server, Now = now,
                Expected = TicketProjection.Estimate(value, definition, isMember, server, now) }));
        }
        foreach (long offset in new long[] { -1, 0, 1, 10800000 - 1, 10800000 }) {
            output.WriteLine(JsonSerializer.Serialize(new { Ticket = ticket, Definition = energy, Member = false, Server = 2301, Now = reset + offset,
                Expected = TicketProjection.Estimate(ticket, energy, false, 2301, reset + offset) }));
        }
    }
}
