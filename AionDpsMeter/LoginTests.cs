using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class LoginTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        byte[] Body(ushort result, bool populated, byte header = 12) {
            var w = new PartyWireTests.Writer(); w.Raw(BitConverter.GetBytes(result)); w.Zero(16); w.Var(populated ? 2 : 0);
            if (populated) foreach (string name in new[] { "Player", "$hidden" }) {
                w.Int(1); w.Text(name); w.Int(25); w.Raw(1, 2); w.Int(50); w.Int(0); w.Int(321); w.Zero(20); w.Text("Guild"); w.Zero(40);
                w.Var(1); w.Int(123); w.Raw(2, 3); w.Long(0); w.Bool(false); w.Int(456); w.Var(0); w.Var(1); w.Int(100);
                w.Bool(true); w.Bool(false); w.Bool(true); w.Var(4);
                w.Long(650000000000000001); w.Zero(12); w.Int(123); w.Int(456); w.Var(3); w.Raw(header, 2, 3); w.Raw(1); w.Bool(true);
                w.Var(1); w.Int(789); w.Long(5); w.Bool(true); w.Long(0); w.Long(0); w.Raw(1); w.Long(12345);
            }
            w.Raw(0); w.Bool(true); w.Bool(false); w.Text("synthetic-session-token"); w.Text("123"); w.Text("456");
            w.Bool(true); w.Raw(1); w.Bool(false); w.Var(10); w.Bool(true); w.Raw(2); return w.Bytes.ToArray();
        }
        byte[] Frame(byte[] body) { var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw(0x15, 0x36); w.Raw(body); return w.Bytes.ToArray(); }
        DecodeResult Decode(byte[] body) => new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, Frame(body)) { TimestampMs = 100 });
        using var output = new StreamWriter(Path.Combine(temp, "reference-login-synthetic.jsonl"));
        foreach (bool populated in new[] { false, true }) {
            var body = Body(0, populated); var decoded = Decode(body);
            var reading = decoded.Events.Single().Network!.Fields.Deserialize<LoginReading>()!;
            check(reading.Server == 123 && reading.Characters.Length == (populated ? 1 : 0) && (!populated || reading.Characters[0] is { Id: 650000000000000001, Name: "Player", Template: 25, Level: 50, Guild: "Guild", CombatPower: 12345, GearScore: 321 }),
                "Login reader handles shared boolean bytes, wide identity and hidden roster entries " + populated);
            check(!JsonSerializer.Serialize(decoded).Contains("synthetic-session-token") && Enumerable.Range(0, body.Length).All(n => Decode(body[..n]).Events.Count == 0) && Decode([..body, 0]).Events.Count == 0,
                "Login token is discarded and all truncations/trailers rejected " + populated);
            output.WriteLine(JsonSerializer.Serialize(new { Opcode = "1536", Record = Convert.ToBase64String(Frame(body)), Events = decoded.Events.Select(e => new { e.Network!.Kind, e.Network.Fields }) }));
        }
        var unsupportedBody = Body(0, true, 1); var unsupportedDecoded = Decode(unsupportedBody);
        output.WriteLine(JsonSerializer.Serialize(new { Opcode = "1536", Record = Convert.ToBase64String(Frame(unsupportedBody)), Events = unsupportedDecoded.Events.Select(e => new { e.Network!.Kind, e.Network.Fields }) }));
        var appearance = Decode(Body(0, true)).Events.Single().Network!.Fields.Deserialize<LoginReading>()!.Characters.Single().Appearance;
        check(appearance is { Race: "light", Gender: "female", Data: "DAID", Wing: 456, Equipment.Length: 1 } && appearance.Equipment[0] is { Item: 123, Skin: 456 }, "Login appearance retains supported data, displayed equipment and preferred wing skin");
        check(Decode(Body(0, true, 1)).Events.Single().Network!.Fields.Deserialize<LoginReading>()!.Characters.Single().Appearance!.Data == null, "Unsupported login appearance headers do not masquerade as usable appearance data");
        check(Decode(Body(1, false)).Events.Count == 0, "Failed login cannot trigger a new account boundary");
        var engine = new CombatEngine();
        void Network(string kind, long at, object fields) => engine.Apply(new("network", Network: new(kind, at, "test", JsonSerializer.SerializeToElement(fields))));
        void Player(string name, int server) => engine.Apply(new("local-player", Actor: new(1, name, true, true, 0, "Cleric", server)));
        void Inventory(long at, long count, bool full = false) => Network(full ? "inventory" : "inventory-update", at, new InventoryBatch(full, [new(100, 110120003, count, 1, 1, 5)], full ? (ushort)528 : (ushort)0));
        Player("Old", 1); Inventory(1, 10, true); Inventory(2, 12);
        Network("pet-roster", 3, new CapturedPetRoster(1002, [new(1002, 1)], [new(1002, 5)], 0));
        engine.Apply(new("player-stats", Stats: new(4, new() { [19] = 100 })));
        Network("resources", 4, new { EntityId = 1, Values = new Dictionary<byte, long> { [1] = 50 } });
        foreach (var e in Decode(Body(0, true)).Events) engine.Apply(e);
        Inventory(101, 500, true); Inventory(102, 505);
        Network("local-combat-power", 101, new { CombatPower = 999 });
        Network("actor-stats", 101, new StatUpdateReading(1, new() { [19] = 999 }, 1000));
        Network("resources", 101, new { EntityId = 1, Values = new Dictionary<byte, long> { [1] = 999 } });
        engine.Apply(new("player-stats", Stats: new(102, new() { [19] = 200 })));
        check(engine.Fight.Actors[1].Cp == 0 && engine.Fight.LocalStats?.Values[19] == 100 && engine.Fight.LocalResources?.Mp == 50,
            "Login-pending CP, full stats, deltas and resources cannot modify the previous character even with a reused entity ID");
        Network("pet-roster", 102, new CapturedPetRoster(1117, [new(1117, 3)], [new(1117, 0)], 0));
        check(engine.Fight.Pets?.Pets.Single().PetId == 1002, "Pre-appearance pet roster cannot overwrite the previous character after login");
        foreach (var e in Decode(Body(0, true)).Events) engine.Apply(e);
        Network("duty-missions", 103, new DutyMissionReading(3, 10000));
        Network("local-zone", 104, new { MapId = 1010, AwaitingIdentity = true });
        Player("New", 2); Inventory(105, 508);
        check(engine.Fight.LocalStats?.Values[19] == 200 && engine.Fight.LocalResources == null,
            "Pending full stats bind to the new identity while unconfirmed resource updates are discarded");
        engine.Apply(new("player-stats", Stats: new(99, new() { [19] = 999 })));
        Network("resources", 99, new { EntityId = 1, Values = new Dictionary<byte, long> { [1] = 999 } });
        check(engine.Fight.LocalStats?.Values[19] == 200 && engine.Fight.LocalResources == null,
            "Delayed pre-login stats and resources cannot overwrite the new identity");
        Network("resources", 106, new { EntityId = 1, Values = new Dictionary<byte, long> { [1] = 75 } });
        check(engine.Fight.LocalResources?.Mp == 75, "Confirmed new-character resource updates resume after identity binding");
        check(engine.Fight.Pets?.Pets.Single() is { PetId: 1117, Level: 3, Souls: 0 } && engine.DrainPetNotices(105).Length == 0,
            "Duplicate login and first map preparation preserve pending pets without generating login level-up alerts");
        check(engine.Loot.Rows().Single(r => r.Character == "Old").Quantity == 2 &&
            engine.Loot.Rows().Single(r => r.Character == "New"&&r.MapId==0) is {Quantity:5,First:102} &&
            engine.Loot.Rows().Single(r => r.Character == "New"&&r.MapId==1010).Quantity==3,
            "Successful login defers new inventory loot with its original unknown map and preserves its baseline through preparation");
        check(engine.DutyMissions.Snapshot().Single() is { Server: 2, Rewarded: 3 } && engine.Fight.Network.All(n => n.Kind != "account-login"),
            "Login-pending duty count reaches only the new server without retaining account packets");
        check(engine.AccountCharacters?.Characters.Single().Id == 650000000000000001, "Current account roster preserves a 64-bit character ID independently of combat entity IDs");
    }
}
