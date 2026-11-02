using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class DaevanionTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var body = new PartyWireTests.Writer(); body.Var(2); body.Int(100); body.Var(2); body.Int(1); body.Int(2); body.Int(200); body.Var(0);
        byte[] Frame(byte[] bytes) { var frame = new PartyWireTests.Writer(); frame.Var(bytes.Length + 6); frame.Raw(0x26, 0xe2); frame.Raw(bytes); return frame.Bytes.ToArray(); }
        DecodeResult Decode(byte[] bytes) => new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, Frame(bytes)));
        var bytes = body.Bytes.ToArray();
        check(Decode(bytes).Events.Single().Network!.Fields.GetProperty("Boards").GetArrayLength() == 2 &&
            Enumerable.Range(0, bytes.Length).All(n => Decode(bytes[..n]).Events.Count == 0) && Decode([..bytes, 0]).Events.Count == 0,
            "Daevanion board lists consume all nested nodes and reject every truncation and trailer");
        var engine = new CombatEngine();
        void Send(long time, DaevanionBoard[] boards) => engine.Apply(new("network", Network: new("daevanion", time, "test", JsonSerializer.SerializeToElement(new { Boards = boards }))));
        void Prepare(long time) => engine.Apply(new("network", Network: new("local-zone", time, "test", JsonSerializer.SerializeToElement(new { MapId = 1010, AwaitingIdentity = true }))));
        Send(1, [new(100, [1, 2])]); Prepare(2);
        engine.Apply(new("local-player", Actor: new(1, "First", true, false, 0, "Cleric", 1)));
        var before = engine.Snapshot();
        Send(3, [new(200, [3])]); Send(2, []);
        check(engine.Fight.Actors[1].Daevanion!.Boards.Single().Board == 200 && before.Actors[1].Daevanion!.Boards.Single().Board == 100 && engine.Fight.Network.Count == 0,
            "Daevanion initial login state attaches to identity, replaces snapshots, rejects stale updates and retains no raw history");
        Prepare(4); Send(5, [new(300, [4])]); Prepare(4);
        check(engine.Fight.Actors[1].Daevanion!.Boards.Single().Board == 200, "Pending Daevanion data cannot overwrite the prior character");
        engine.Apply(new("local-player", Actor: new(2, "Second", true, false, 0, "Assassin", 1)));
        check(engine.Fight.Actors[2].Daevanion!.Boards.Single().Board == 300, "Duplicate preparation preserves pending Daevanion data for the new identity");
        Send(6, []);
        check(engine.Fight.Actors[2].Daevanion is { Boards.Length: 0 }, "Empty Daevanion snapshots clear previous nodes");
        var store = new FightStore(Path.Combine(temp, "daevanion")); store.Save(before);
        check(store.Load(Path.Combine(store.Root, before.Id + ".json")).Actors[1].Daevanion!.Boards.Single().Nodes.SequenceEqual([1, 2]), "Daevanion nodes persist with saved character summaries");
        bool invalid = false;
        try { Send(7, [new(1, [2, 2])]); } catch (InvalidDataException) { invalid = true; }
        check(invalid && engine.Fight.Actors[2].Daevanion!.Boards.Length == 0, "Duplicate Daevanion nodes fail without changing the snapshot");
        using var fixtures = new ReferencePacketFixtures(Path.Combine(temp, "reference-daevanion-synthetic.jsonl"));
        fixtures.Add(new CapturedPayload(13328, 50000, Frame(bytes)) { TimestampMs = 1 });
        fixtures.Add(new CapturedPayload(13328, 50000, Frame([0])) { TimestampMs = 2 });
    }
}
