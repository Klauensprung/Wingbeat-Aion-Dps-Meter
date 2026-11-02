using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class RevivalTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        byte[] Frame(byte[] body) { var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw(0x26, 0x8d); w.Raw(body); return w.Bytes.ToArray(); }
        DecodeResult Decode(byte[] body) => new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, Frame(body)) { TimestampMs = 300 });
        var revival = Decode([1]).Events.Single().Network!;
        check(revival.Kind == "entity-revived" && revival.Fields.GetProperty("EntityId").GetInt32() == 1 &&
            Decode([]).Events.Count == 0 && Decode([128]).Events.Count == 0 && Decode([0]).Events.Count == 0 && Decode([1, 0]).Events.Count == 0,
            "Revival wire identifies an entity without HP and rejects zero, truncated or extended records");
        var fight = new Fight(); fight.Actors[1] = new(1, "Player", true, true, 0, "Cleric", 1);
        void Hp(long at, int hp) => fight.Network.Add(new("hp", at, "synthetic", JsonSerializer.SerializeToElement(new { EntityId = 1, CurrentHp = hp })));
        fight.Deaths.Add(new(1, 200)); Hp(100, 50); fight.Network.Add(revival);
        check(CapturedHealth.Build(fight)[1] is { Value: null, Timestamp: 300, Label: "Revived (HP not captured)" } && fight.Deaths.Count == 1,
            "Revival clears defeated display without inventing HP or erasing death history");
        Hp(400, 75);
        check(CapturedHealth.Build(fight)[1].Value == 75, "Newer captured health supersedes revival with actual HP");
        fight.Deaths.Add(new(1, 500));
        check(CapturedHealth.Build(fight)[1].Value == 0, "Later death supersedes older revival");
        fight.Network.Add(revival with { Timestamp = 500 });
        check(CapturedHealth.Build(fight)[1].Value == 0, "Tied death and revival remain conservatively defeated");
        fight.Network.Add(revival with { Timestamp = 600 });
        var store = new FightStore(Path.Combine(temp, "revival-save")); store.Save(fight);
        var restored = store.Load(Path.Combine(store.Root, fight.Id + ".json"));
        check(CapturedHealth.Build(restored)[1] == CapturedHealth.Build(fight)[1] && restored.Deaths.Count == 2,
            "Compact fight save preserves revival health state and historical deaths");
        fight.Actors[2] = fight.Actors[1] with { Id = 2, OriginalEntityId = 1 }; fight.Deaths.Clear();
        check(!CapturedHealth.Build(fight).ContainsKey(1), "Reused entity identity does not inherit ambiguous revival state");
        using var fixtures = new ReferencePacketFixtures(Path.Combine(temp, "reference-revival-synthetic.jsonl"));
        foreach (int id in new[] { 1, 128, 100000 }) {
            var body = new PartyWireTests.Writer(); body.Var(id);
            fixtures.Add(new CapturedPayload(13328, 50000, Frame(body.Bytes.ToArray())) { TimestampMs = 300 });
        }
    }
}
