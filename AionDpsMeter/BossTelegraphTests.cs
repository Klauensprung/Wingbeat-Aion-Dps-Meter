using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class BossTelegraphTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        byte[] Body(byte action, ushort duration) {
            var w = new PartyWireTests.Writer(); w.Var(1234); w.Int(98765); w.Raw(action);
            w.Int(12); w.Raw(3, 0); foreach (float n in new[] { 1f, 2f, 3f, 4f }) w.Raw(BitConverter.GetBytes(n));
            w.Raw(BitConverter.GetBytes(duration)); return w.Bytes.ToArray();
        }
        CapturedPayload Packet(byte[] body) { var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw(0x48, 0x38); w.Raw(body); return new(13328, 50000, w.Bytes.ToArray()) { TimestampMs = 100 }; }
        DecodeResult Decode(byte[] body) => new EuGameDecoder().Decode(Packet(body));
        var bytes = Body(255, 65535);
        check(Decode(bytes).Events.Single().Network!.Fields.Deserialize<TelegraphNotice>() is { EntityId: 1234, Skill: 98765, Action: 255, DurationMs: 65535 }, "Telegraph reader preserves entity, skill, action and unsigned duration");
        check(Enumerable.Range(0, bytes.Length).All(n => Decode(bytes[..n]).Events.Count == 0) && Decode([..bytes, 0]).Events.Count == 0, "Telegraph rejects every truncated body and unexpected trailer");
        using var fixtures = new ReferencePacketFixtures(Path.Combine(temp, "reference-telegraphs.jsonl"));
        fixtures.Add(Packet(bytes)); fixtures.Add(Packet(Body(0, 0))); fixtures.Add(Packet(Body(1, 1500)));
        int npc = BossCatalogue.Default.Bosses.SelectMany(b => b.Variants).First(v => BossCatalogue.Default.IsEncounterBoss(v.NpcId)).NpcId;
        var actor = new Actor(1234, "Boss", false, false, 0, "Unknown", 0) { NpcId = npc };
        var engine = new CombatEngine(); engine.Apply(new("actor", Actor: actor));
        void Apply(long at, byte action = 1, int entity = 1234) => engine.Apply(new("network", Network: new("telegraph", at, "4838", JsonSerializer.SerializeToElement(new TelegraphNotice(entity, 98765, action, 1500)))));
        Apply(100); Apply(1099); Apply(1100); Apply(1101, 2); Apply(1102, 2, 9999);
        check(engine.Fight.Telegraphs.Count == 3 && engine.Fight.Telegraphs[0].Areas == 2 && engine.Fight.Telegraphs[0].Timestamp == 100, "Repeated areas merge within one second of the first notice; action changes, exact boundary and unknown entities remain distinct");
        check(engine.Fight.Network.Count == 0 && engine.Fight.HasRecordedData && engine.Loot.Rows().Length == 0, "Telegraphs retain compact summaries without raw history or invented loot");
        var snapshot = engine.Snapshot(); Apply(1103, 2);
        check(snapshot.Telegraphs[^1].Areas == 1 && engine.Fight.Telegraphs[^1].Areas == 2, "Telegraph snapshots are independent of live grouping");
        var encounter = BossEncounterAnalysis.Build(snapshot, id => id.ToString()).Single();
        check(encounter.Moments.Count(m => m.Kind == "Telegraph") == 3 && encounter.Casts == 0 && encounter.DamageFromBoss == 0 && encounter.Abilities.Single().SkillCode == 98765, "Boss timeline shows area warnings without manufacturing casts or damage");
        var store = new FightStore(Path.Combine(temp, "telegraph-save")); store.Save(snapshot);
        var restored = store.Load(Path.Combine(store.Root, snapshot.Id + ".json"));
        check(restored.Telegraphs.SequenceEqual(snapshot.Telegraphs) && FightArchiveSummary.Create(snapshot).Telegraphs.SequenceEqual(snapshot.Telegraphs), "Telegraph summaries survive save/reload and compact archives");
        engine.Apply(new("actor", Actor: actor with { NpcId = npc + 1, Name = "Replacement" }));
        check(engine.Fight.Telegraphs.All(t => t.EntityId != actor.Id && engine.Fight.Actors[t.EntityId].NpcId == npc), "Reused entity IDs cannot transfer earlier boss telegraphs to a replacement");
        engine.NewFight("Reset"); check(engine.Fight.Telegraphs.Count == 0, "Manual new fight clears previous telegraph summaries");
        var first = snapshot.Copy(); var later = new Fight(); later.Telegraphs.Add(new(1104, 1234, npc, 98765, 2, 1500, 3));
        BossTelegraphs.Merge(first, later);
        check(first.Telegraphs[^1].Areas == 4 && snapshot.Telegraphs[^1].Areas == 1, "Recovery merges adjacent summary areas without changing prior snapshots");
        var bounded = new Fight(); bounded.Actors[1234] = actor;
        for (int i = 0; i < BossTelegraphs.Limit + 10; i++) BossTelegraphs.Apply(bounded, new("telegraph", i * 1000L, "4838", JsonSerializer.SerializeToElement(new TelegraphNotice(1234, 1, 1, 100))));
        check(bounded.Telegraphs.Count == BossTelegraphs.Limit && bounded.OmittedTelegraphs == 10, "Long fights cap telegraph summaries and count omitted area notices");
        bounded.Telegraphs.Add(bounded.Telegraphs[0]); bool rejected = false;
        try { BossTelegraphs.Validate(bounded); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "Saved telegraph lists exceeding the cap are rejected");
    }
}
