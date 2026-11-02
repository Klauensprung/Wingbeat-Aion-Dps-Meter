using System.IO;

namespace AionDpsMeter.Local;

internal static class ItemNameTests
{
    internal static void Run(Action<bool, string> check)
    {
        var names = ItemNames.Default;
        check(names.LoadError == null && names.Count == 9177 && names.Find(110120003) == "Ludra's Blade of Extinction",
            "The compact embedded item-name catalogue resolves exact game IDs to English names");
        check(names.Find(0) == "Item 0" && names.Find(int.MaxValue) == $"Item {int.MaxValue}" && ReferenceEquals(names, ItemNames.Default),
            "Unknown item IDs remain visible and the lazy immutable dictionary is shared across loot views");
        var row = new LootItemRow(1, 1000, "Player 1", 110120003, 1, 1, 1000, 1000);
        check(row.Item == names.Find(row.ItemId), "Loot rows display names without changing the numeric aggregation key");
        bool Rejected(ItemNameData data) { try { ItemNames.Validate(data); return false; } catch (InvalidDataException) { return true; } }
        string hash = new('a', 64);
        check(Rejected(new("synthetic", "bad", new() { [1] = "Item" })) &&
            Rejected(new("synthetic", hash, new() { [0] = "Item" })) &&
            Rejected(new("synthetic", hash, new() { [1] = " " })) &&
            Rejected(new("synthetic", hash, new() { [1] = new string('x', 301) })),
            "Item catalogue validation rejects invalid provenance hashes, IDs, empty names and oversized labels");
        var zones = ZoneNames.Default;
        check(zones.LoadError == null && zones.Count == 718 && zones.Find(1010) == "Verteron" && zones.Find(20) == "Chaotic Lower Reshanta",
            "The compact zone dictionary resolves captured zone IDs against the pinned English reference");
        check(zones.Find(0) == "Unknown map" && zones.Find(int.MaxValue) == $"Map {int.MaxValue}" &&
            (row with { MapId = 1010 }).Map == "Verteron",
            "Loot map labels preserve unknown IDs and use exact map-name lookup without guessing a region");
        var objects = WorldObjectNames.Default;
        check(objects.LoadError == null && objects.Count == 31 && objects.Find(1590001) == "Odyle" && objects.Find(1500045) == "Kukuru",
            "Gathering marker object IDs resolve to the compact local name dictionary");
        check(objects.Find(2000001) == "Object 2000001" && objects.Find(int.MaxValue) == $"Object {int.MaxValue}" &&
            ReferenceEquals(objects, WorldObjectNames.Default),
            "Gathering database IDs are not confused with packet object IDs and unknown objects retain their identifiers");
    }
}
