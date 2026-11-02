using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class LootLedgerTests
{
    internal static void Run(Action<bool, string> check)
    {
        LootPickup Pickup(long time, uint drop = 1, int item = 500000001, uint character = 30, uint quantity = 3) => new(time, drop, item, quantity, character, 1);
        var ledger = new LootLedger();
        var partial=new LootLedger();var originalView=partial.OverlaySnapshot();partial.RecordOmitted(2);
        var partialCopy=partial.Copy();partial.RecordOmitted(1);
        var partialFight=new Fight {LiveLoot=partialCopy}.ForStorage();
        check(!originalView.OmittedItems&&partial.OverlaySnapshot().OmittedItems&&partialCopy.OmittedItems==2&&
            partialFight is {HasRecordedData:true,SavedLoot:{Rows.Length:0,OmittedItems:2}},
            "Omission-only loot refreshes overlays, preserves snapshots and remains recorded after compact storage");
        ledger.Add(Pickup(1000), "Player 1"); ledger.Add(Pickup(2000, 2), "Player 1");
        var first = ledger.Rows().Single();
        check(first.Quantity == 6 && first.Pickups == 2 && first.First == 1000 && first.Last == 2000,
            "Loot session totals accumulate quantities and pickup counts with first/last capture times");
        ledger.Add(Pickup(3000, 3, 500000002), "Player 1");
        check(ledger.Rows().Length == 2 && ledger.Rows().All(r => r.Session == 1) && first.Quantity == 6,
            "Multiple item types share a session without mutating earlier display rows");
        ledger.Add(Pickup(1803000, 4), "Player 1");
        ledger.Add(Pickup(1804000, 5, character: 31), "Player 2");
        check(ledger.Rows().Select(r => r.Session).Distinct().Count() == 3 && ledger.Rows()[0].Character == "Player 2",
            "Thirty-minute gaps and character switches start distinct loot sessions in newest-first order");
        var revision = ledger.Revision;
        ledger.Add(Pickup(1802000, 6), "Player 1"); ledger.Add(Pickup(1805000, 7, quantity: 0), "Player 2");
        check(ledger.Revision == revision, "Stale or invalid pickups do not alter session totals or trigger display refreshes");
        var large = new LootLedger();
        for (int i = 0; i < 520; i++) large.Add(Pickup(1000 + i, (uint)i + 1, 500000000 + i), "Player 1");
        large.Add(Pickup(2000, 1000, 500000000), "Player 1");
        check(large.Rows().Length == 512 && large.OmittedItems == 8 && large.Rows().Single(r => r.ItemId == 500000000).Quantity == 6,
            "Loot item memory limits report omitted entries while continuing to total already-retained items");
        var sessions = new LootLedger();
        for (int i = 0; i < 40; i++) sessions.Add(Pickup(1000L + i * 1800000L, (uint)i + 1), "Player 1");
        check(sessions.Rows().Length == 32 && sessions.OmittedSessions == 8 && sessions.Rows()[0].Session == 40,
            "Capture loot history retains at most 32 sessions and reports older-session eviction");
        var fight = new Fight();
        fight.Actors[1] = new(1, "Player 1", true, false, 0, "Cleric", 1) { CharacterId = 30 };
        var pickup = Pickup(1000);
        fight.Network.Add(new("loot-pickup", 1000, "synthetic", JsonSerializer.SerializeToElement(pickup)));
        fight.Network.Add(fight.Network[0]);
        fight.Network.Add(new("ground-drops", 1000, "synthetic", JsonSerializer.SerializeToElement(new { Quantity = 999 })));
        fight.Network.Add(new("loot-pickup", 1001, "synthetic", JsonSerializer.SerializeToElement(pickup)));
        var restored = LootLedger.FromFight(fight).Rows().Single();
        check(restored.Quantity == 3 && restored.Character == "Player 1",
            "Saved loot views count only attributed pickups, ignore mismatched timestamps and suppress duplicate records");
        var mapped = new LootLedger();
        mapped.Add(Pickup(1000) with { MapId = 1010 }, "Player 1");
        mapped.Add(Pickup(2000, 2) with { MapId = 2010 }, "Player 1");
        mapped.Add(Pickup(3000, 3), "Player 1");
        check(mapped.Rows().Length == 3 && mapped.Rows().Select(r => r.Session).Distinct().Count() == 1 && mapped.Rows().Sum(r => r.Quantity) == 9 &&
            mapped.Rows().Single(r => r.MapId == 0).Map == "Unknown map",
            "Loot groups the same item separately by captured map while preserving unknown maps and shared session totals");
        var overlayLedger = new LootLedger();
        for (int i = 0; i < 10; i++) overlayLedger.Add(Pickup(1000 + i, (uint)i + 1, 500000000 + i), "Player 1");
        var overlay = overlayLedger.OverlaySnapshot();
        check(overlay.Rows.Length == 5 && overlay.Rows[0].ItemId == 500000009 && overlay.Rows[^1].ItemId == 500000005 &&
            ReferenceEquals(overlay, overlayLedger.OverlaySnapshot()),
            "Loot overlay snapshots keep only five recent item types and reuse unchanged data without copying session history");
        overlayLedger.Add(Pickup(2000, 11, 500000000), "Player 1");
        check(overlayLedger.OverlaySnapshot().Rows[0].Quantity == 6 && overlay.Rows[0].ItemId == 500000009,
            "New pickups reorder overlay item totals without mutating an earlier display snapshot");
        overlayLedger.Add(Pickup(3000, 12, character: 31), "Player 2");
        check(overlayLedger.OverlaySnapshot().Rows is [{ Character: "Player 2" }],
            "Loot overlay switches to the latest character session instead of mixing earlier character totals");
    }
}
