using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class CharacterCollectionTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        byte[] Frame(ushort op, byte[] body) { var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw((byte)(op >> 8), (byte)op); w.Raw(body); return w.Bytes.ToArray(); }
        using var fixtures = new ReferencePacketFixtures(Path.Combine(temp, "reference-collections-synthetic.jsonl"));
        foreach (ushort op in new ushort[] { 0x5b8d, 0xaf8a, 0x0be3 }) {
            var w = new PartyWireTests.Writer(); w.Var(2); w.Int(100); if (op == 0xaf8a) w.Raw(1); else w.Int(3);
            w.Int(200); if (op == 0xaf8a) w.Raw(0); else w.Int(0);
            var bytes = w.Bytes.ToArray();
            DecodeResult Decode(byte[] b) => new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, Frame(op, b)));
            check(Decode(bytes).Events.Single().Network!.Fields.GetProperty("Entries").GetArrayLength() == (op == 0xaf8a ? 1 : 2) &&
                Enumerable.Range(0, bytes.Length).All(n => Decode(bytes[..n]).Events.Count == 0) && (Decode([..bytes, 99]).Events.Count == 1) == (op == 0x5b8d),
                $"Collection {op:X4} parses counted entries, rejects truncation and follows its reference trailer policy");
            foreach (var body in new[] { bytes, new byte[] { 0 } }) fixtures.Add(new CapturedPayload(13328, 50000, Frame(op, body)) { TimestampMs = 1 });
            if (op == 0x5b8d) fixtures.Add(new CapturedPayload(13328, 50000, Frame(op, [..bytes, 99, 100])) { TimestampMs = 1 });
        }
        var engine = new CombatEngine();
        void Send(string kind, long time, CollectionValue[] entries) => engine.Apply(new("network", Network: new(kind, time, "test", JsonSerializer.SerializeToElement(new { Entries = entries }))));
        void Prepare(long time) => engine.Apply(new("network", Network: new("local-zone", time, "test", JsonSerializer.SerializeToElement(new { MapId = 1010, AwaitingIdentity = true }))));
        Send("skins", 1, [new(100, 3)]); Send("decorations", 1, [new(200, 1)]); Send("monoliths", 1, [new(300, 5)]); Prepare(2);
        engine.Apply(new("local-player", Actor: new(1, "First", true, false, 0, "Cleric", 1)));
        var before = engine.Snapshot(); Send("skins", 3, []); Send("skins", 2, [new(999, 1)]);
        check(engine.Fight.Actors[1].Collections is { Skins.Entries.Length: 0, Decorations.Entries.Length: 1, Monoliths.Entries.Length: 1 } && before.Actors[1].Collections!.Skins!.Entries.Single().Value == 3 && engine.Fight.Network.Count == 0,
            "Collections bind at login, independently replace stale-safe snapshots and retain no raw events");
        Prepare(4); Send("monoliths", 5, [new(400, 6)]); Prepare(4);
        check(engine.Fight.Actors[1].Collections!.Monoliths!.Entries.Single().Id == 300, "Pending collections cannot overwrite the previous character");
        engine.Apply(new("local-player", Actor: new(2, "Second", true, false, 0, "Assassin", 1)));
        check(engine.Fight.Actors[2].Collections is { Skins: null, Decorations: null } next && next.Monoliths!.Entries.Single().Id == 400,
            "New character receives only its pending collection snapshots");
        var store = new FightStore(Path.Combine(temp, "collections")); store.Save(before);
        check(store.Load(Path.Combine(store.Root, before.Id + ".json")).Actors[1].Collections!.Decorations!.Entries.Single().Value == 1,
            "Collection counts and equipped decoration slots persist with saved fights");
    }
}
