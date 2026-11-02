using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class DungeonPartyTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: new(1, "Local", true, false, 0, "Cleric", 2301)));
        var events = new List<object>();
        PartyMemberSummary Member(string name, int cp = 100) => new(name, 2301, "Cleric", 50, 200, cp);
        void Roster(long at, params PartyMemberSummary[] members) {
            events.Add(members.Length == 0 ? new { type = "party-left", at } : (object)new { type = "roster", at,
                members = members.Select(m => new { name = m.Name, server = m.Server, @class = m.Job, level = m.Level, gearScore = m.GearScore, combatPower = m.CombatPower }).ToArray() });
            engine.Apply(new("party", Party: new([], "Synthetic roster") { Timestamp = at, Summaries = members }));
        }
        void Map(long at, int map) {
            events.Add(new { type = "map", at, map });
            engine.Apply(new("network", Network: new("local-zone", at, "test", JsonSerializer.SerializeToElement(new { MapId = map }))));
        }
        Roster(1, Member("Offline A")); Map(2, 600002); var before = engine.Snapshot();
        Roster(3, Member("Offline B")); Roster(4, Member("Offline A", 500)); Roster(5);
        check(engine.Fight.DungeonRun!.Party is [{ Name: "Offline B" }, { Name: "Offline A", CombatPower: 500 }] && before.DungeonRun!.Party is [{ CombatPower: 100 }],
            "Dungeon roster carries entry members, retains departures, replaces matching metadata and preserves immutable earlier snapshots");
        Map(6, 1010); var first = engine.SaveCompleted(_ => { }).Single(); Map(7, 600002);
        check(engine.Fight.DungeonRun!.Party.Length == 0,
            "Party departure clears the next run roster without erasing the finished run");
        Roster(8, Member("Offline C")); Map(9, 1010); var second = engine.SaveCompleted(_ => { }).Single();
        var store = new FightStore(Path.Combine(temp, "dungeon-party")); store.Save(first);
        var restored = store.Load(Path.Combine(store.Root, first.Id + ".json"));
        check(restored.DungeonRun!.Party.SequenceEqual(first.DungeonRun!.Party) && restored.Actors.Count == 1 && !restored.HasEvents,
            "Offline dungeon roster survives disk storage without phantom actors or fabricated combat");
        File.WriteAllText(Path.Combine(temp, "reference-dungeon-party.json"), JsonSerializer.Serialize(new {
            IncludeParty = true, Events = events, Runs = new[] { first, second }.Select(f => new {
                Map = f.DungeonRun!.MapId, Entered = f.DungeonRun.EnteredAt, Left = f == first ? 6 : 9,
                Used = f.DungeonRun.UsedItems, Received = f.DungeonRun.ReceivedItems, Party = f.DungeonRun.Party
            })
        }));
        var tracker = new DungeonRunTracker(); tracker.Apply(new("local-zone", 1, "test", JsonSerializer.SerializeToElement(new { MapId = 600002 })));
        for (int i = 0; i < 260; i++) tracker.RecordParty(new([], "Synthetic") { Timestamp = i + 2, Summaries = [Member("Member " + i)] });
        var bounded = tracker.Current!; tracker.RecordParty(new([], "Synthetic") { Timestamp = 1, Summaries = [Member("Stale")] });
        check(bounded.Party.Length == 256 && bounded.OmittedPartyObservations == 4 && ReferenceEquals(bounded, tracker.Current),
            "Historical dungeon roster has bounded retention and explicit omitted observations while rejecting stale roster changes");
        tracker.Clear(); tracker.Apply(new("local-zone", 300, "test", JsonSerializer.SerializeToElement(new { MapId = 600002 })));
        check(tracker.Current!.Party.Length == 0, "Character boundary clears pending dungeon roster identities");
        Map(10, 600002);
        engine.Apply(new("network", Network: new("account-login", 11, "test", JsonSerializer.SerializeToElement(new LoginReading(0, 2301, [])))));
        Roster(12, Member("Next character party"));
        check(engine.Fight.DungeonRun!.Party.All(m => m.Name != "Next character party"),
            "Unbound login roster cannot contaminate the previous character's active dungeon summary");
    }
}
