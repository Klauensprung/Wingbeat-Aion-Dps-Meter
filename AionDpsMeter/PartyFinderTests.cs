using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class PartyFinderTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        void Member(PartyWireTests.Writer w, long id, string name, byte flags = 22, byte slot = 1, int template = 29) {
            w.Raw(flags, slot); w.Long(id); w.Text(name); w.Int(template); w.Int(50); w.Int(123);
            w.Bool(false); if ((flags & 2) != 0) w.Bool(true);
            if ((flags & 4) != 0) { w.Raw(BitConverter.GetBytes((ushort)2301)); w.Raw(0, 0); }
            w.Raw(0); w.Long(44000); w.Raw(0); if ((flags & 16) != 0) w.Long(0); w.Raw(0, 0);
        }
        byte[] Group(bool extensions = false, bool ambiguous = false) {
            var w = new PartyWireTests.Writer(); w.Int(123); w.Text("Group"); w.Raw(3); w.Int(600002); w.Raw(0, 0); w.Long(6000000001); w.Bool(false); w.Raw(0, 0); w.Var(3);
            Member(w, ambiguous ? (2301L << 48) | 123 : 6000000001, "Local", ambiguous ? (byte)30 : (byte)22);
            Member(w, ambiguous ? (2301L << 48) | 124 : 7000000001, "Member", ambiguous ? (byte)30 : (byte)22, 2); Member(w, 0, "", 0, 3);
            if (extensions) w.Raw(7, 8, 9); return w.Bytes.ToArray();
        }
        byte[] Join(byte flags) { var w = new PartyWireTests.Writer(); Member(w, 8000000001, "Third", flags); return w.Bytes.ToArray(); }
        CapturedPayload Packet(byte op, byte[] body) { var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw(op, 0x97); w.Raw(body); return new CapturedPayload(13328, 50000, w.Bytes.ToArray()) { TimestampMs = 100 }; }
        DecodeResult Decode(byte op, byte[] body) => new EuGameDecoder().Decode(Packet(op, body));
        using var fixtures = new ReferencePacketFixtures(Path.Combine(temp, "reference-party-finder-synthetic.jsonl"));
        var group = Decode(2, Group()).Events.Single(e => e.Network != null).Network!.Fields.Deserialize<FinderGroup>()!;
        check(group is { Id: 123, Leader: 6000000001, Members.Length: 2 } && group.Members[0] is { Class: "Cleric", Server: 2301, Ready: true, CombatPower: 44000 },
            "Reference party finder group retains wide IDs, explicit server, class, readiness and leader while discarding empty slots");
        check(Decode(2, Group()).Events.Single(e => e.Profiles != null).Profiles!.Length == 2, "Verified party finder layout continues supplying scoped CP profiles");
        foreach (var body in new[] { Group(), Group(true) }) fixtures.Add(Packet(2, body));
        var compatible = Group(ambiguous: true); var legacy = new WireReader(compatible);
        check(PlayerTelemetry.ReadParty(ref legacy).Length == 2 && Decode(2, compatible).Events.Any(e => e.Network?.Kind == "party-finder"),
            "Verified finder parsing wins even when legacy CP parsing also accepts the packet");
        fixtures.Add(Packet(2, compatible));
        check(Enumerable.Range(0, Group().Length).All(n => Decode(2, Group()[..n]).Events.Count == 0), "Truncated party finder group publishes neither partial members nor CP profiles");
        foreach (byte flags in new byte[] { 0, 2, 4, 16, 22, 31 }) {
            byte[] body = Join(flags); fixtures.Add(Packet(11, body));
            check(Decode(11, body).Events.Count == 1 && Enumerable.Range(0, body.Length).All(n => Decode(11, body[..n]).Events.Count == 0) && Decode(11, [..body, 0]).Events.Count == 0,
                "Party finder join parses optional fields and rejects truncations/trailers for flags " + flags);
        }
        foreach (int template in new[] { 21, 24, 45, 48, 0, 49 }) {
            var member = new PartyWireTests.Writer(); Member(member, 8000000001, "Class alias", template: template);
            fixtures.Add(Packet(11, member.Bytes.ToArray()));
        }
        foreach (var (op, body, success) in new[] {
            ((byte)0x15, new byte[] { 0, 0, 1 }, true), ((byte)0x15, new byte[] { 1, 0, 1 }, false),
            ((byte)0x16, (byte[])[..BitConverter.GetBytes(7000000001L), 0], true),
            ((byte)0x1d, new byte[] { 0, 0 }, true), ((byte)0x1d, new byte[] { 1, 0 }, false), ((byte)0x2a, new byte[] { 2 }, true) }) {
            fixtures.Add(Packet(op, body));
            check(Decode(op, body).Events.Count == (success ? 1 : 0) && Enumerable.Range(0, body.Length).All(n => Decode(op, body[..n]).Events.Count == 0) && Decode(op, [..body, 0]).Events.Count == 0,
                "Party finder ready/leave/kick success gates and exact reads " + op + "/" + success);
        }
        var tracker = new PartyFinderTracker(); var local = new Actor(1, "Local", true, true, 0, "Cleric", 2301); tracker.Follow(local);
        long at = 100; using var states = new StreamWriter(Path.Combine(temp, "reference-party-finder-state.jsonl"));
        void Apply(string kind, object data) {
            var message = new NetworkEvent(kind, at++, "test", JsonSerializer.SerializeToElement(data)); tracker.Apply(message);
            states.WriteLine(JsonSerializer.Serialize(new { message.Kind, At = message.Timestamp, Data = data, Expected = tracker.Snapshot() }));
        }
        Apply("party-finder-ready", new FinderReady(null, false));
        Apply("party-finder", group);
        Apply("party-finder-ready", new FinderReady(null, false));
        Apply("party-finder-ready", new FinderReady(7000000001, false));
        check(tracker.Snapshot()!.Members.All(m => !m.Ready), "Self and wide-ID readiness updates select their intended members");
        Apply("party-finder-joined", group.Members[1] with { Id = 8000000001, Name = "Third" });
        Apply("party-finder-joined", group.Members[1] with { Id = 8000000001, Name = "Renamed" });
        check(tracker.Snapshot()!.Members.Length == 3, "Party finder rejoin replaces the same persistent identity without duplicates");
        Apply("party-finder-left", new { }); Apply("party-finder-joined", group.Members[1]);
        check(tracker.Snapshot() == null, "Leave clears ready-check state and subsequent joins require a full baseline");
        Apply("party-finder", group); tracker.Roster(2); tracker.Map();
        check(tracker.Snapshot() == null, "Formed roster and map transition retire the finder group");
        tracker.Follow(local with { Name = "Other" }); check(tracker.Snapshot() == null, "Character change clears finder membership");
        var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: local)); foreach (var e in Decode(2, Group()).Events) engine.Apply(e);
        engine.NewFight("Manual reset");
        check(engine.PartyFinder.Snapshot()?.Members.Length == 2 && engine.Fight.Network.Count == 0 && engine.Loot.Rows().Length == 0,
            "Finder membership survives manual fight reset without raw packet history or loot effects");
        engine.PartyFinder.Roster(2);
        engine.Apply(new("network", Network: new("account-login", 2000, "test", JsonSerializer.SerializeToElement(new LoginReading(0, 2301, [])))));
        check(engine.PartyFinder.Snapshot(7000) == null && engine.PartyFinder.Snapshot(7001)?.Members.Length == 2,
            "Production account login retains recruitment while expiring the prior roster after five seconds");
        engine.Apply(new("local-player", Actor: local with { Name = "Other" }));
        check(engine.PartyFinder.Snapshot(7002) == null, "Confirmed character switch clears recruitment retained across account login");
        var delayed = new PartyFinderTracker(); delayed.Follow(local with { Cp = 42000 });
        using var delayedStates = new StreamWriter(Path.Combine(temp, "reference-party-finder-delayed.jsonl"));
        FinderGroup? Delayed(string kind, long time, object data, int cp = 42000, int gear = 321) {
            var fields = JsonSerializer.SerializeToElement(data);
            if (kind == "account-characters") delayed.AccountCharacters(time);
            else if (kind == "roster") delayed.Roster(fields.GetProperty("Count").GetInt32());
            else delayed.Apply(new(kind, time, "test", fields));
            var snapshot = delayed.Snapshot(time, local with { Cp = cp, Metadata = new(GearScore: gear, GearScoreAt: time) });
            delayedStates.WriteLine(JsonSerializer.Serialize(new { Kind = kind, At = time, Data = data, ProfileCp = cp, ProfileGear = gear, Expected = snapshot }));
            return snapshot;
        }
        var missingCp = group with { Members = group.Members.Select(m => m with { CombatPower = 0 }).ToArray() };
        var fallback = Delayed("party-finder", 1000, missingCp)!;
        check(fallback.Members.Single(m => m.Name == "Local").CombatPower == 42000 && fallback.Members.Single(m => m.Name != "Local").CombatPower == 0,
            "Missing finder CP falls back only for the local player without altering captured member values");
        Delayed("roster", 1100, new { Count = 2 });
        Delayed("account-characters", 1200, new { });
        check(Delayed("tick", 6200, new { }) == null && Delayed("tick", 6201, new { }, 43000)?.Members.Single(m => m.Name == "Local").CombatPower == 43000,
            "Account transition expires old roster suppression strictly after five seconds and uses the latest local CP");
        Delayed("roster", 7000, new { Count = 2 }); Delayed("account-characters", 7100, new { });
        Delayed("roster", 7200, new { Count = 2 });
        check(Delayed("tick", 13000, new { }) == null, "A fresh roster cancels delayed account-transition invalidation");
        Delayed("roster", 14000, new { Count = 0 });
        check(Delayed("party-finder", 14100, group, 99000)?.Members.Single(m => m.Name == "Local").CombatPower == 44000,
            "Explicit finder CP remains authoritative over the local fallback");
        var missingGear = group with { Members = group.Members.Select(m => m with { GearScore = 0 }).ToArray() };
        var gearFallback = Delayed("party-finder", 14200, missingGear)!;
        check(gearFallback.Members.Single(m => m.Name == "Local").GearScore == 321 && gearFallback.Members.Single(m => m.Name != "Local").GearScore == 0 &&
            Delayed("tick", 14300, new { }, gear: 654)?.Members.Single(m => m.Name == "Local").GearScore == 654,
            "Only local missing gear score uses the latest independently captured value");
        check(Delayed("party-finder", 14400, group, gear: 999)?.Members.Single(m => m.Name == "Local").GearScore == 123,
            "Explicit finder gear score takes precedence over player metadata");
        var world = new PartyFinderTracker();
        var worldLocal = local with { PersistentId = 6000000001, Cp = 42000, Metadata = new(GearScore: 321, GearScoreAt: 1000) };
        using var worldStates = new StreamWriter(Path.Combine(temp, "reference-party-finder-world.jsonl"));
        FinderGroup? World(string kind, long time, object data) {
            if (kind == "player") world.Follow(worldLocal);
            else if (kind == "map") world.Map();
            else if (kind == "account-characters") world.AccountCharacters(time);
            else world.Apply(new(kind, time, "test", JsonSerializer.SerializeToElement(data)));
            // Deliberately supply the old Actor even after map reset: UI still has
            // that actor until a new local appearance is confirmed.
            var result = world.Snapshot(time, worldLocal);
            worldStates.WriteLine(JsonSerializer.Serialize(new { Kind = kind, At = time, Data = data, Expected = result }));
            return result;
        }
        object Player() => new { Entity = 1, Id = "6000000001", Name = "Local", Server = local.Server, CombatPower = worldLocal.Cp, GearScore = worldLocal.Metadata!.GearScore };
        World("player", 1000, Player());
        var noStats = group with { Members = group.Members.Select(m => m with { CombatPower = 0, GearScore = 0 }).ToArray() };
        check(World("party-finder", 1100, noStats)?.Members.Single(m => m.Name == "Local") is { CombatPower: 42000, GearScore: 321 },
            "Finder world fixture establishes live local CP and gear fallback");
        check(World("map", 1200, new { Map = 1010 })?.Members.Single(m => m.Name == "Local") is { CombatPower: 0, GearScore: 0 },
            "Map transition stops finder fallback from reusing the previous world actor's stats");
        check(World("account-characters", 1300, new { Characters = new[] { new { name = "Local", id = "6000000001", combatPower = 99999, gearScore = 999 } } })?.Members.Single(m => m.Name == "Local") is { CombatPower: 0, GearScore: 0 },
            "Account character list alone does not supply world-player finder stats");
        worldLocal = worldLocal with { Cp = 43000, Metadata = new(GearScore: 654, GearScoreAt: 1400) };
        check(World("player", 1400, Player())?.Members.Single(m => m.Name == "Local") is { CombatPower: 43000, GearScore: 654 },
            "Confirmed local appearance restores finder fallback using the new world stats");
        World("party-finder", 1500, group);
        check(World("map", 1600, new { Map = 1020 })?.Members.Single(m => m.Name == "Local") is { CombatPower: 44000, GearScore: 123 },
            "Map transition leaves explicitly captured finder member stats intact");
        var worldEngine = new CombatEngine(); worldEngine.Apply(new("local-player", Actor: worldLocal));
        worldEngine.Apply(new("network", Network: new("party-finder", 2000, "test", JsonSerializer.SerializeToElement(noStats))));
        worldEngine.Apply(new("network", Network: new("local-zone", 2100, "test", JsonSerializer.SerializeToElement(new { MapId = 1010 }))));
        check(worldEngine.PartyFinder.Snapshot(2200, worldEngine.Fight.Actors.GetValueOrDefault(worldEngine.Fight.LocalPlayer))?.Members.Single(m => m.Name == "Local") is { CombatPower: 0, GearScore: 0 },
            "Production map handling suppresses stale finder stats until local identity is refreshed");
        worldEngine.Apply(new("network", Network: new("account-login", 3000, "test", JsonSerializer.SerializeToElement(new LoginReading(0, local.Server, [])))));
        worldEngine.Apply(new("local-player", Actor: worldLocal));
        worldEngine.Apply(new("network", Network: new("party-finder", 3100, "test", JsonSerializer.SerializeToElement(noStats))));
        worldEngine.Apply(new("network", Network: new("local-zone", 2999, "test", JsonSerializer.SerializeToElement(new { MapId = 1010 }))));
        check(worldEngine.PartyFinder.Snapshot(3200, worldLocal)?.Members.Single(m => m.Name == "Local") is { CombatPower: 43000, GearScore: 654 },
            "Pre-login map cannot erase newly confirmed party-finder fallback stats");
        worldEngine.Apply(new("network", Network: new("local-zone", 3300, "test", JsonSerializer.SerializeToElement(new { MapId = 0 }))));
        check(worldEngine.PartyFinder.Snapshot(3400, worldLocal)?.Members.Single(m => m.Name == "Local") is { CombatPower: 43000, GearScore: 654 },
            "Invalid map cannot clear current party-finder identity");
        worldEngine.Apply(new("network", Network: new("local-zone", 3500, "test", JsonSerializer.SerializeToElement(new { MapId = 1110 }))));
        check(worldEngine.PartyFinder.Snapshot(3600, worldLocal)?.Members.Single(m => m.Name == "Local") is { CombatPower: 0, GearScore: 0 },
            "Valid current map still invalidates party-finder fallback until new appearance");
        delayed.Follow(local with { Name = "Other" });
        check(delayed.Snapshot(15000) == null, "A changed local character clears recruitment retained during account selection");
    }
}
