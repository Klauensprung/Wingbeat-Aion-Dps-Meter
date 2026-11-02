using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class ShopPurchaseTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        byte[] Body(ushort op, int quantity = 3, ushort result = 0, bool empty = false) {
            var w = new PartyWireTests.Writer();
            if (op is 0x8e56 or 0x018c) w.Raw(BitConverter.GetBytes(result));
            if (op == 0x018c) w.Var(50);
            if (op == 0x038c) { w.Raw(1); w.Int(10); w.Raw(2); }
            if (op != 0x8f56) w.Var(empty ? 0 : 1);
            if (!empty || op == 0x8f56) { w.Int(133019901); w.Int(quantity); if (op is 0x8e56 or 0x8f56) w.Long(10000); }
            if (op == 0x018c) w.Long(100);
            return w.Bytes.ToArray();
        }
        byte[] Frame(ushort op, byte[] body) { var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw((byte)(op >> 8), (byte)op); w.Raw(body); return w.Bytes.ToArray(); }
        foreach (ushort op in new ushort[] { 0x8e56, 0x8f56, 0x018c, 0x038c }) {
            var body = Body(op); bool rejects = true;
            for (int n = 0; n < body.Length; n++) { try { var r = new WireReader(body.AsSpan(0, n)); ShopPurchaseWire.Read(ref r, op); rejects = false; } catch (Exception ex) when (ex is InvalidDataException or OverflowException) { } }
            try { var r = new WireReader([..body, 0]); ShopPurchaseWire.Read(ref r, op); rejects = false; } catch (InvalidDataException) { }
            check(rejects, $"Shop purchases {op:X4} reject every truncation and trailing bytes");
        }
        using var fixture = new ReferencePacketFixtures(Path.Combine(temp, "reference-shop-synthetic.jsonl"));
        var decoder = new EuGameDecoder(); var engine = new CombatEngine();
        engine.Apply(new("local-player", Actor: new(1, "Player", true, true, 0, "Cleric", 1)));
        long at = 100;
        DecodeResult Send(ushort op, int count = 3, ushort result = 0, bool empty = false) {
            var payload = new CapturedPayload(13328, 50000, Frame(op, Body(op, count, result, empty))) { TimestampMs = at++ };
            fixture.Add(payload); var decoded = decoder.Decode(payload); foreach (var e in decoded.Events) engine.Apply(e); return decoded;
        }
        check(Send(0x038c).Events.Count == 0 && Send(0x018c, result: 1).Events.Count == 0 && Send(0x038c).Events.Count == 0,
            "NPC purchases require a successfully captured shop open");
        check(Send(0x018c, empty: true).Events.Count == 0 && Send(0x038c, 2).Events.Count == 1 && !engine.ShopPurchases.Snapshot().Single().BaselineKnown,
            "Empty successful shop open enables purchases while an unknown count baseline stays explicit");
        Send(0x018c, 5); Send(0x038c, 2); Send(0x038c, 1);
        check(engine.ShopPurchases.Snapshot().Single() is { Count: 8, BaselineKnown: true, ResetAt: null }, "NPC purchase increments add to the captured absolute baseline");
        Send(0x8e56, 4); Send(0x8f56, 6); Send(0x8f56, 6); Send(0x8e56, 999, result: 1);
        check(engine.ShopPurchases.Snapshot().Single() is { Count: 6, ResetAt: 10000 } && engine.Loot.Rows().Length == 0 && engine.Fight.Network.Count == 0,
            "Cash-shop counts replace rather than add; failures and duplicate snapshots cannot invent loot or retain raw history");
        engine.NewFight("Reset"); check(engine.ShopPurchases.Snapshot().Single().Count == 6, "Shop counters survive manual fight reset");
        var tracker = new ShopPurchaseTracker(); var owner = new Actor(1, "Local", true, true, 0, "Cleric", 2);
        void Apply(long time, int goods, int count, long? reset, bool added = false) => tracker.Apply(new("shop-purchases", time, "test", JsonSerializer.SerializeToElement(new ShopPurchaseBatch(added, [new(goods, count, reset)]))), owner);
        Apply(100, 1, 5, 10000); Apply(90, 1, 99, 10000); Apply(110, 1, 99, 100);
        check(tracker.Snapshot().Single().Count == 5, "Stale or expired purchase counts cannot regress current state");
        for (int i = 2; i <= 1100; i++) Apply(i + 100, i, 1, null);
        check(tracker.Snapshot().Length == 1024, "Shop counter memory is bounded independently of packet volume");
        var scoped = new ShopPurchaseTracker();
        long monday = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        long reset = new DateTimeOffset(2026, 11, 4, 7, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        void Purchase(string name, int goods, int count, long time, bool added = false, long? explicitReset = null) => scoped.Apply(
            new("shop-purchases", time, "test", JsonSerializer.SerializeToElement(new ShopPurchaseBatch(added, [new(goods, count, explicitReset)]))), owner with { Server = 2301, Name = name });
        Purchase("First", 1209, 2, monday); Purchase("Second", 1209, 1, monday + 1);
        check(scoped.Snapshot().Count(r => r.Goods == 1209) == 2 && scoped.Snapshot().Single(r => r.Character == "First").Count == 2,
            "Character-scoped Odyle purchases never merge between characters on the same server");
        Purchase("First", 1210, 5, monday); Purchase("Second", 1210, 6, monday + 1);
        check(scoped.Snapshot().Single(r => r.Goods == 1210) is { Count: 6, Character: "", InferredReset: true } shared && shared.ResetAt == reset,
            "Server-scoped Odyle totals share one counter and use the verified global weekly reset");
        Purchase("First", 1209, 1, reset, added: true);
        check(scoped.Snapshot().Single(r => r.Goods == 1209 && r.Character == "First") is { Count: 1, BaselineKnown: false } next && next.ResetAt == reset + 7 * 86400000L,
            "The exact weekly reset starts a new partial counter instead of carrying last week's purchases");
        Purchase("Second", 1209, 3, monday + 2, explicitReset: reset + 1000);
        check(scoped.Snapshot().Single(r => r.Goods == 1209 && r.Character == "Second") is { InferredReset: false } explicitRow && explicitRow.ResetAt == reset + 1000,
            "Captured reset time overrides the scheduled fallback");
        check(new[] { 1101, 1201, 1301, 1401, 1501, 2101, 2501 }.All(s => ChecklistTasks.WeeklyReset(s, monday) == reset) &&
            ChecklistTasks.WeeklyReset(9999, monday) == null && ChecklistTasks.WeeklyReset(2301, reset - 1) == reset && ChecklistTasks.WeeklyReset(2301, reset) == reset + 7 * 86400000L,
            "Global reset calculation is UTC-based, strictly future, shared across regions and refuses unknown server regions");
        Purchase("First", 999999, 2, monday); Purchase("Second", 999999, 3, monday);
        check(scoped.Snapshot().Where(r => r.Goods == 999999).Count() == 2 && scoped.Snapshot().Where(r => r.Goods == 999999).All(r => r.ResetAt == null),
            "Unmapped goods keep owners separate and do not invent a reset calendar");
        var dates = new[] { "2026-03-08T06:59:59Z", "2026-03-29T01:00:00Z", "2026-10-25T01:00:00Z", "2026-11-01T06:00:00Z", "2026-11-04T06:59:59.999Z", "2026-11-04T07:00:00Z", "2026-11-04T07:00:00.001Z", "2026-12-31T23:59:59Z" };
        var calendar = from server in new[] { 1101, 1201, 1301, 1401, 1501, 2101, 2201, 2301, 2401, 2501, 9999 }
                       from date in dates let timestamp = DateTimeOffset.Parse(date).ToUnixTimeMilliseconds()
                       select new { Server = server, Timestamp = timestamp, ResetAt = ChecklistTasks.WeeklyReset(server, timestamp) };
        File.WriteAllText(Path.Combine(temp, "reference-calendar-synthetic.json"), JsonSerializer.Serialize(calendar));
        string cachePath = Path.Combine(temp, "shop-purchases.json"); var savedTracker = new ShopPurchaseTracker(cachePath);
        void Persisted(int goods, int count, long time, bool added = false) => savedTracker.Apply(new("shop-purchases", time, "test",
            JsonSerializer.SerializeToElement(new ShopPurchaseBatch(added, [new(goods, count, null)]))), owner with { Server = 2301 });
        Persisted(133019901, 5, monday); Persisted(233019901, 4, monday + 1); Persisted(999999, 10, monday);
        savedTracker.Save(); var reloaded = new ShopPurchaseTracker(cachePath);
        check(reloaded.Snapshot().Length == 2 && reloaded.Snapshot().All(r => r.ResetAt == reset),
            "Shop persistence retains known periods and excludes unknown-reset counters");
        var summary = ChecklistTasks.Summarize(reloaded.Snapshot(), monday + 2);
        check(summary.Single() is { Count: 9, Target: 12, Owner: "Server-wide", Status: "In progress" },
            "Weekly task aggregation combines alternate goods IDs within one captured period");
        var restarted = new CombatEngine(shopPurchases: reloaded); restarted.Apply(new("local-player", Actor: owner with { Server = 2301 }));
        restarted.Apply(new("network", Network: new("shop-purchases", monday + 3, "test", JsonSerializer.SerializeToElement(new ShopPurchaseBatch(true, [new(133019901, 3, null)])))));
        check(ChecklistTasks.Summarize(reloaded.Snapshot(), monday + 4).Single() is { Count: 12, Status: "Target reached" },
            "A new capture engine continues the persisted current-period baseline without duplicating totals");
        check(ChecklistTasks.Summarize(reloaded.Snapshot(), reset).Single() is { Count: null, Status: "Previous period" },
            "Expired weekly tasks cannot display the previous period as current progress");
        Persisted(133019901, 1, reset, added: true);
        check(ChecklistTasks.Summarize(savedTracker.Snapshot(), reset + 1).Single() is { Count: 1, Status: "Observed additions only" },
            "New-period task aggregation excludes alternate goods from an older period and marks an unknown baseline");
        string invalidPath = Path.Combine(temp, "invalid-shop.json"); File.WriteAllText(invalidPath, "[{\"Server\":-1}]");
        check(new ShopPurchaseTracker(invalidPath).Snapshot().Length == 0 && File.ReadAllText(invalidPath) == "[{\"Server\":-1}]",
            "Invalid saved shop counters are ignored without startup failure or source deletion");
        using var checklistOutput = new StreamWriter(Path.Combine(temp, "reference-checklist-synthetic.jsonl"));
        var oracleTracker = new ShopPurchaseTracker();
        void Compare(string name, int server, long time, bool added, params ShopPurchase[] purchases) {
            var batch = new ShopPurchaseBatch(added, purchases);
            oracleTracker.Apply(new("shop-purchases", time, "test", JsonSerializer.SerializeToElement(batch)), owner with { Name = name, Server = server });
            var expected = ChecklistTasks.Summarize(oracleTracker.Snapshot(), time).Where(r => r.Count != null).Select(r => new {
                Owner = r.Owner == "Server-wide" ? $"server:{r.Server}" : $"{r.Server}:{r.Owner}",
                TaskId = ChecklistTasks.Purchases.Single(t => t.Name == r.Task).Id, r.Count, r.ResetAt
            }).ToArray();
            checklistOutput.WriteLine(JsonSerializer.Serialize(new { Server = server, Character = name, Timestamp = time, Batch = batch, Expected = expected }));
        }
        Compare("First", 2301, monday, false, new ShopPurchase(133019901, 5, null));
        Compare("Second", 2301, monday + 1, false, new ShopPurchase(233019901, 4, null));
        Compare("First", 2301, monday + 2, true, new ShopPurchase(133019901, 3, null));
        Compare("First", 2301, monday + 3, false, new ShopPurchase(1209, 2, null), new ShopPurchase(1210, 5, null));
        Compare("Second", 2301, monday + 4, false, new ShopPurchase(1209, 1, null), new ShopPurchase(1210, 6, null));
        Compare("First", 1301, monday + 5, false, new ShopPurchase(1209, 4, null), new ShopPurchase(133019901, 2, null));
        Compare("First", 2301, monday + 6, false, new ShopPurchase(134019901, 1, reset), new ShopPurchase(140019901, 2, reset));
        Compare("Second", 2301, monday + 7, true, new ShopPurchase(134019901, 1, reset));
        Compare("First", 2301, monday + 8, false, new ShopPurchase(1209, 9, monday), new ShopPurchase(999999, 7, null));
        Compare("First", 2301, reset, true, new ShopPurchase(133019901, 1, null));
        Compare("Second", 2301, reset + 1, false, new ShopPurchase(233019901, 4, null));
        Compare("Second", 2301, reset + 2, false, new ShopPurchase(1209, 0, null));
        Compare("Second", 2301, reset + 3, false, new ShopPurchase(1209, 0, null));
        Compare("First", 2301, reset + 4, false, new ShopPurchase(133019901, 7, reset + 14 * 86400000L), new ShopPurchase(233019901, 2, reset + 7 * 86400000L));
        check(ChecklistTasks.Summarize(oracleTracker.Snapshot(), reset + 4).Single(r => r.Server == 2301 && r.Task == "Commands").Count == 2,
            "Alternate goods with equal timestamps select the last packet row's period, matching the reference checklist");
        string orderedPath = Path.Combine(temp, "ordered-shop.json");
        File.WriteAllText(orderedPath, JsonSerializer.Serialize(oracleTracker.Snapshot().Where(r => r.ResetAt != null)));
        check(ChecklistTasks.Summarize(new ShopPurchaseTracker(orderedPath).Snapshot(), reset + 4).Single(r => r.Server == 2301 && r.Task == "Commands").Count == 2,
            "Saved purchase ordering preserves the selected task period after restart");
    }
}
