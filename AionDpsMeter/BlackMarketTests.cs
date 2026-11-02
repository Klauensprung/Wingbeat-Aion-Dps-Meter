using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class BlackMarketTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        const long now = 1793470000000;
        byte[] Stock(int count, byte tab = 1) {
            var w = new PartyWireTests.Writer(); w.Raw(tab, (byte)count);
            for (int i = 0; i < count; i++) {
                w.Int(i); w.Int(110000000 + i); w.Long(5000000000 + i); w.Raw((byte)i, 2); w.Long(6000000000 + i); w.Long(4000000000 + i);
                w.Bool(i % 2 == 0); w.Bool(i % 3 == 0);
            }
            w.Long(now + 3600000); w.Int(3); return w.Bytes.ToArray();
        }
        byte[] Purchase(ushort result, byte tab, int slot) { var w = new PartyWireTests.Writer(); w.Raw(BitConverter.GetBytes(result)); w.Raw(tab); w.Int(slot); return w.Bytes.ToArray(); }
        CapturedPayload Packet(byte op, byte[] body) { var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw(op, 0x56); w.Raw(body); return new CapturedPayload(13328, 50000, w.Bytes.ToArray()) { TimestampMs = now }; }
        DecodeResult Decode(byte op, byte[] body) => new EuGameDecoder().Decode(Packet(op, body));
        using var fixtures = new ReferencePacketFixtures(Path.Combine(temp, "reference-black-market-synthetic.jsonl"));
        foreach (int count in new[] { 0, 1, 5, 255 }) {
            var body = Stock(count); var decoded = Decode(0x91, body);
            check(decoded.Events.Single().Network!.Fields.Deserialize<BlackMarketReading>()!.Entries.Length == count,
                "Black market accepts byte-sized stock list " + count);
            fixtures.Add(Packet(0x91, body));
        }
        foreach (var (op, body) in new[] { ((byte)0x91, Stock(5)), ((byte)0x95, Purchase(0, 1, 2)) })
            check(Enumerable.Range(0, body.Length).All(n => Decode(op, body[..n]).Events.Count == 0) && Decode(op, [..body, 0]).Events.Count == 0,
                "Black market rejects all truncations and trailers before publishing " + op);
        fixtures.Add(Packet(0x95, Purchase(0, 1, 2))); fixtures.Add(Packet(0x95, Purchase(1, 1, 2)));
        check(Decode(0x95, Purchase(1, 1, 2)).Events.Count == 0, "Failed black market purchase emits no sold-out update");
        var reading = Decode(0x91, Stock(5)).Events.Single().Network!.Fields.Deserialize<BlackMarketReading>()!;
        check(reading.Entries[4] is { Quantity: 5000000004, BasePrice: 6000000004, Price: 4000000004, SoldOut: false } && reading.Entries[3].SoldOut,
            "Black market retains 64-bit amounts and shared packed purchase bits across entries");
        var clockDecoder = new EuGameDecoder(); var clock = new PartyWireTests.Writer(); clock.Var(14); clock.Raw(0, 0x36); clock.Long(now + 2500);
        clockDecoder.Decode(new CapturedPayload(13328, 50000, clock.Bytes.ToArray()) { TimestampMs = now });
        check(clockDecoder.Decode(Packet(0x91, Stock(1))).Events.Single().Network!.Fields.Deserialize<BlackMarketReading>()!.ServerOffset == 2500,
            "Black market carries the measured map-clock offset through the production decoder");
        var tracker = new BlackMarketTracker(); long at = now;
        var owner = new Actor(1, "First", true, true, 0, "Cleric", 2301);
        using var states = new StreamWriter(Path.Combine(temp, "reference-black-market-state.jsonl"));
        void Record(string kind, object? data = null) => states.WriteLine(JsonSerializer.Serialize(new { Kind = kind, At = at, Server = owner.Server, Name = owner.Name, Data = data, Expected = tracker.Snapshot(at) }));
        void Apply(string kind, object data) { tracker.Apply(new(kind, at, "test", JsonSerializer.SerializeToElement(data)), at); Record(kind, data); }
        Apply("black-market-purchase", new BlackMarketPurchase(1, 2));
        Apply("black-market", reading with { ServerOffset = 1000 });
        tracker.Follow(owner); Record("follow");
        check(tracker.Snapshot(at).Single().RefreshAt == reading.RefreshAt - 1000, "Black market corrects refresh time and preserves initial offers when identity arrives");
        at++; Apply("black-market-purchase", new BlackMarketPurchase(1, 2));
        check(tracker.Snapshot(at).Single().Entries[2].SoldOut, "Black market purchase marks only the captured slot sold out");
        tracker.Apply(new("black-market", at - 1, "test", JsonSerializer.SerializeToElement(reading)), at);
        var detached = tracker.Snapshot(at); detached[0].Entries[2] = detached[0].Entries[2] with { SoldOut = false };
        check(tracker.Snapshot(at).Single().Entries[2].SoldOut, "Stale stock and mutation of a returned snapshot cannot undo a purchase");
        at++; Apply("black-market", reading with { Tab = 2 });
        at++; Apply("black-market-purchase", new BlackMarketPurchase(1, 999));
        at++; Apply("black-market", reading with { Entries = [] });
        check(tracker.Snapshot(at).Single(s => s.Tab == 1).Entries.Length == 0, "Empty black market snapshot replaces old offers");
        owner = owner with { Name = "Second" }; tracker.Follow(owner); Record("follow");
        check(tracker.Snapshot(at).Length == 0, "Black market cannot carry offers across character identities");
        Apply("black-market", reading); at = reading.RefreshAt; Record("tick");
        check(tracker.Snapshot(at).Length == 0, "Black market expires offers at the exact refresh boundary");
        var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: owner));
        foreach (var e in Decode(0x91, Stock(5)).Events) engine.Apply(e);
        engine.NewFight("Reset");
        check(engine.BlackMarket.Snapshot(now).Length == 1 && engine.Fight.Network.Count == 0 && engine.Loot.Rows().Length == 0,
            "Fight reset preserves current shop offers without raw history or invented loot");
        engine.Apply(new("local-player", Actor: owner with { Name = "Third" }));
        check(engine.BlackMarket.Snapshot(now).Length == 0, "Production identity binding clears another character's shop");
        var identityTracker = new BlackMarketTracker();
        var identified = owner with { PersistentId = 6000000001 };
        identityTracker.Follow(identified);
        identityTracker.Apply(new("black-market", now, "test", JsonSerializer.SerializeToElement(reading)), now);
        identityTracker.Follow(identified with { PersistentId = 0 });
        identityTracker.Follow(identified with { PersistentId = 10294967297 });
        check(identityTracker.Snapshot(now).Length == 0,
            "Same-name black market owners with different full IDs cannot share offers after a sparse identity update");
        identityTracker.Clear(); identityTracker.Follow(owner with { PersistentId = 0 });
        identityTracker.Apply(new("black-market", now, "test", JsonSerializer.SerializeToElement(reading)), now);
        identityTracker.Follow(identified);
        check(identityTracker.Snapshot(now).Length == 1, "First full black market identity enriches an existing owner without discarding stock");
        engine.Apply(new("local-player", Actor: identified));
        foreach (var e in Decode(0x91, Stock(5)).Events) engine.Apply(e);
        engine.Apply(new("local-player", Actor: identified with { PersistentId = 10294967297 }));
        check(engine.BlackMarket.Snapshot(now).Length == 0, "Production binding clears stock on full identity change with unchanged character name");
    }
}
