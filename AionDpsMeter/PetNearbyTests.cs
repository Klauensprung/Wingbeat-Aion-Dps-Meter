namespace AionDpsMeter.Local;

internal static class PetNearbyTests
{
    internal static void Run(Action<bool, string> check)
    {
        var pet = new PetDefinition(1002, "Example", "Family", 5, "Monster", "reference") { Level2Souls = 25, Level3Souls = 75 };
        PetDefinition[] pets = [pet, pet with { Id = 1003 }, pet with { Id = 1004 }];
        var position = new CapturedPosition(1010, 0, 0, 99, 1000);
        var collection = new PetCollectionSnapshot(1000, 0, false, [new(1002, 1, 10, 0), new(1003, 2, 0, 0)]);
        IReadOnlyList<PetSpawnSite> Sites(int id) => [
            new(id, "World/World_L/World_L_A", "Verteron", [new(1, 30, 40, 0), new(2, 3, 4, 500)]),
            new(id, "World/World_D/World_D_A", "Altgard", [new(3, 0, 0, 99)])];
        var rows = PetNearby.Build(pets, collection, position, 2, false, Sites);
        var completeRows = PetNearby.Build(pets, collection with { HasRoster = true }, position, 1, false, Sites);
        check(completeRows is [{ Id: 1004, SoulsNeeded: 5 }],
            "A complete roster makes absent pets known unowned and eligible for summon-cost farming guidance");
        check(rows.Length == 1 && rows[0] is { Id: 1002, SoulsNeeded: 15, Distance: 5, Point.NpcId: 2 },
            "Nearby farming selects the closest planar spawn on the matching map and excludes completed or unknown needs");
        rows = PetNearby.Build(pets, collection, position, 2, true, Sites);
        check(rows.Length == 2 && rows[1] is { Id: 1004, SoulsNeeded: null, Status: "Level unknown" },
            "Unknown nearby pet progress is opt-in and remains explicitly unknown with deterministic distance ties");
        check(PetNearby.Build(pets, collection, position, 1, false, Sites).Length == 0 &&
            PetNearby.Build(pets, collection with { Pets = [new(1002, 1, 25, 0)] }, position, 2, false, Sites).Length == 0,
            "Nearby farming does not recommend completed collection goals or pets already ready to level");
        check(PetNearby.Build(pets, collection, null, 2, true, Sites).Length == 0 &&
            PetNearby.Build(pets, collection, position with { MapId = 9999 }, 2, true, Sites).Length == 0,
            "Nearby farming cannot invent guidance without a position or verified area mapping");
        rows = PetNearby.Build(Enumerable.Range(1000, 100).Select(id => pet with { Id = id }), null, position, 2, true, Sites);
        check(rows.Length == 30 && rows[0].Id == 1000 && rows[^1].Id == 1029,
            "Nearby pet results retain at most thirty nearest candidates");
        var tracker = new PetNearbyTracker();
        var initial = tracker.Update(position, collection, 2);
        check(initial.Count > 0 && initial.Count <= 5 && ReferenceEquals(initial, tracker.Update(position with { }, collection, 2)),
            "Pet tracker retains at most five results and reuses them across unchanged display ticks");
        var changed = tracker.Update(position with { X = 300000 }, collection, 2);
        check(!ReferenceEquals(initial, changed) && initial[0].Distance != changed[0].Distance &&
            tracker.Update(position, collection with { Pets = [new(1002, 3, 0, 0), new(1003, 3, 0, 0)] }, 2).Count == 0,
            "Pet tracker refreshes when location or collection changes without mutating previous rows");
        check(tracker.Update(null, collection, 2).Count == 0 && tracker.Update(position, collection, -1).Count == 0,
            "Pet tracker clears lost-position guidance and clamps invalid saved farming phases");
    }
}
