using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class PlayerMetadataTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        byte[] Guild(string name) { var w = new PartyWireTests.Writer(); w.Var(1); w.Long(123); w.Text(name); w.Zero(17); w.Text("Discarded union"); return w.Bytes.ToArray(); }
        byte[] Level(int level) { var w = new PartyWireTests.Writer(); w.Var(1); w.Int(level); return w.Bytes.ToArray(); }
        byte[] Frame(ushort op, byte[] body) { var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw((byte)(op >> 8), (byte)op); w.Raw(body); return w.Bytes.ToArray(); }
        DecodeResult Decode(ushort op, byte[] body) => new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, Frame(op, body)) { TimestampMs = 20 });
        foreach (var (op, body) in new[] { ((ushort)0x338a, Guild("Guild")), ((ushort)0x918d, Level(50)) })
            check(Decode(op, body).Events.Count == 1 && Enumerable.Range(0, body.Length).All(n => Decode(op, body[..n]).Events.Count == 0) && Decode(op, [..body, 0]).Events.Count == 0,
                $"Player metadata {op:X4} rejects all incomplete or extended records");
        var engine = new CombatEngine();
        engine.Apply(new("local-player", Actor: new(1, "Player", true, true, 1234, "Cleric", 1) { Metadata = new(49, "Old guild", 10, 10), Wings = new(1, []) }));
        foreach (var e in Decode(0x338a, Guild("New guild")).Events.Concat(Decode(0x918d, Level(50)).Events)) engine.Apply(e);
        var current = engine.Fight.Actors[1];
        check(current.Metadata is { Level: 50, Guild: "New guild" } && current.Name == "Player" && current.Cp == 1234 && current.Wings != null && engine.Fight.Network.Count == 0,
            "Guild and level updates preserve player identity and other captured state without retaining raw updates");
        engine.Apply(new("actor", Actor: current with { Metadata = new(1, "Stale", 5, 5) }));
        check(engine.Fight.Actors[1].Metadata is { Level: 50, Guild: "New guild" }, "Older player appearance metadata cannot overwrite newer guild/level readings");
        foreach (var e in Decode(0x338a, Guild("")).Events) engine.Apply(e);
        check(engine.Fight.Actors[1].Metadata?.Guild == "", "Explicit empty guild clears prior membership");
        var fight = engine.Snapshot(); var store = new FightStore(Path.Combine(temp, "player-metadata")); store.Save(fight);
        check(store.Load(Path.Combine(store.Root, fight.Id + ".json")).Actors[1].Metadata == fight.Actors[1].Metadata, "Player level/guild metadata persists with full saved fights");
        var unknown = new CombatEngine();
        foreach (var e in Decode(0x338a, Guild("Unidentified guild")).Events.Concat(Decode(0x918d, Level(99)).Events)) unknown.Apply(e);
        check(!unknown.Fight.Actors.ContainsKey(1) && unknown.Fight.Network.Count == 0,
            "Metadata updates for unknown entities neither invent players nor retain raw history");
        unknown.Apply(new("actor", Actor: new(1, "Later player", true, false, 0, "", 0) { Metadata = new(10, "Actual guild", 10, 10) }));
        check(unknown.Fight.Actors[1].Metadata is { Level: 10, Guild: "Actual guild" },
            "Unknown-player metadata is not replayed onto a later appearance, matching reference world state");
        var npc = new CombatEngine();
        npc.Apply(new("actor", Actor: new(1, "NPC", false, false, 0, "", 0)));
        foreach (var e in Decode(0x338a, Guild("Wrong guild")).Events.Concat(Decode(0x918d, Level(99)).Events)) npc.Apply(e);
        check(npc.Fight.Actors[1].Metadata == null && !npc.Fight.Actors[1].Player,
            "Player metadata updates cannot attach player details to a known NPC");
        using var fixtures = new ReferencePacketFixtures(Path.Combine(temp, "reference-player-metadata-synthetic.jsonl"));
        foreach (int score in new[] { 0, 321, 65536 }) {
            byte[] body = LocalCharacterWireTests.Body(1, "Local", gearScore: score);
            var decoded = Decode(0x3336, body);
            check(decoded.Events.First().Actor?.Metadata is { } metadata && metadata.GearScore == score && metadata.GearScoreAt == 20,
                "Local appearance preserves explicit gear score " + score);
            fixtures.Add(new CapturedPayload(13328, 50000, Frame(0x3336, body)) { TimestampMs = 20 });
        }
        void Gear(long at, int score) => engine.Apply(new("network", Network: new("visible-equipment", at, "test",
            JsonSerializer.SerializeToElement(new EquipmentReading(1, false, [], score)))));
        Gear(100, 321); Gear(99, 999);
        check(engine.Fight.Actors[1].Metadata is { GearScore: 321, GearScoreAt: 100, Level: 50 },
            "Gear score has an independent clock and rejects stale equipment changes without losing other metadata");
        Gear(101, 0);
        check(engine.Fight.Actors[1].Metadata is { GearScore: 0, GearScoreAt: 101 }, "Explicit zero gear score clears prior captured value");
        store.Save(engine.Snapshot());
        check(store.Load(Path.Combine(store.Root, engine.Fight.Id + ".json")).Actors[1].Metadata?.GearScore == 0,
            "Captured gear score survives saved fight metadata");
        foreach (var (op, body) in new[] { ((ushort)0x338a, Guild("Guild")), ((ushort)0x338a, Guild("")), ((ushort)0x918d, Level(50)), ((ushort)0x918d, Level(0)) })
            fixtures.Add(new CapturedPayload(13328, 50000, Frame(op, body)) { TimestampMs = 20 });
    }
}
