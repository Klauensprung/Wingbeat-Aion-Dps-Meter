using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class TitleTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        byte[] Frame(byte[] body) { var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw(0x79, 0x56); w.Raw(body); return w.Bytes.ToArray(); }
        var body = new PartyWireTests.Writer(); body.Var(2); body.Int(100); body.Int(200); body.Var(3);
        body.Raw(1); body.Int(100); body.Raw(2); body.Int(0); body.Raw(3); body.Int(200);
        var bytes = body.Bytes.ToArray();
        DecodeResult Decode(byte[] data) => new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, Frame(data)) { TimestampMs = 1 });
        check(Decode(bytes).Events.Single().Network!.Fields.Deserialize<TitleSnapshot>() is { Owned.Length: 2, Equipped.Length: 3 } &&
            Enumerable.Range(0, bytes.Length).All(n => Decode(bytes[..n]).Events.Count == 0) && Decode([..bytes, 0]).Events.Count == 0,
            "Title lists consume owned IDs and equipped slots exactly and reject truncations/trailers");
        using var fixtures = new ReferencePacketFixtures(Path.Combine(temp, "reference-titles-synthetic.jsonl"));
        foreach (var data in new[] { bytes, new byte[] { 0, 0 } }) fixtures.Add(new CapturedPayload(13328, 50000, Frame(data)) { TimestampMs = 1 });
        var engine = new CombatEngine();
        void Send(long time, int[] owned, EquippedTitle[] equipped) => engine.Apply(new("network", Network: new("titles", time, "test", JsonSerializer.SerializeToElement(new TitleSnapshot(time, owned, equipped)))));
        void Prepare(long time) => engine.Apply(new("network", Network: new("local-zone", time, "test", JsonSerializer.SerializeToElement(new { MapId = 1010, AwaitingIdentity = true }))));
        Send(1, [100, 200], [new(1, 100), new(2, 0), new(3, 200)]); Prepare(2);
        engine.Apply(new("local-player", Actor: new(1, "First", true, false, 0, "Cleric", 1)));
        var before = engine.Snapshot(); Send(3, [], []); Send(2, [300], []);
        check(engine.Fight.Actors[1].Titles is { Owned.Length: 0, Equipped.Length: 0 } && before.Actors[1].Titles!.Owned.Length == 2 && engine.Fight.Network.Count == 0,
            "Titles bind after initial map preparation, empty snapshots replace lists, stale updates cannot roll back, and raw events are not retained");
        Prepare(4); Send(5, [400], [new(1, 400)]); Prepare(4);
        check(engine.Fight.Actors[1].Titles!.Owned.Length == 0, "Pending titles cannot overwrite the prior character");
        engine.Apply(new("local-player", Actor: new(2, "Second", true, false, 0, "Assassin", 1)));
        check(engine.Fight.Actors[2].Titles!.Owned.SequenceEqual([400]), "Duplicate preparation preserves the next character's title snapshot");
        bool rejected = false;
        try { Send(6, [400], [new(1, 400), new(1, 0)]); } catch (InvalidDataException) { rejected = true; }
        check(rejected && engine.Fight.Actors[2].Titles!.Equipped.Single().Id == 400, "Duplicate title slots fail without changing the prior snapshot");
        var store = new FightStore(Path.Combine(temp, "titles")); store.Save(before);
        check(store.Load(Path.Combine(store.Root, before.Id + ".json")).Actors[1].Titles is { Owned.Length: 2, Equipped.Length: 3 }, "Title ownership and equipment persist with saved fights");
    }
}
