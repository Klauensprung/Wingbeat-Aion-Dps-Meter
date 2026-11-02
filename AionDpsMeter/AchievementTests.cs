using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class AchievementTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        byte[] Body(bool full, params (int Id, int Count)[] entries) {
            var w = new PartyWireTests.Writer(); w.Var(entries.Length);
            foreach (var e in entries) { w.Int(e.Id); w.Int(e.Count); w.Long(12345); w.Raw(3); }
            if (full) {
                w.Var(2);
                for (int i = 0; i < 2; i++) { w.Int(i + 1); w.Var(5); for (int j = 0; j < 5; j++) w.Bool(j % 2 == 0); }
            }
            return w.Bytes.ToArray();
        }
        byte[] Frame(bool full, byte[] body) { var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw(full ? (byte)0x83 : (byte)0x84, 0x56); w.Raw(body); return w.Bytes.ToArray(); }
        DecodeResult Decode(bool full, byte[] body) => new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, Frame(full, body)) { TimestampMs = 100 });
        foreach (bool full in new[] { true, false }) {
            var body = Body(full, (1, 2), (2, 0), (3, -1), (4, 1), (1, 0));
            var message = Decode(full, body).Events.Single().Network!;
            check(message.Fields.Deserialize<AchievementReading>() is { Completed: [4] } && message.Fields.GetProperty("All").GetBoolean() == full,
                $"Achievement map uses last duplicate value, retains positive completion counts and consumes shared reward bits, full={full}");
            check(Enumerable.Range(0, body.Length).All(n => Decode(full, body[..n]).Events.Count == 0) && Decode(full, [..body, 0]).Events.Count == 0,
                $"Achievement reader rejects every truncation and extra trailer, full={full}");
        }
        NetworkEvent Event(bool full, long at, params int[] ids) => new("achievements", at, "synthetic", JsonSerializer.SerializeToElement(new AchievementReading(full, ids)));
        var partial = CharacterCollections.Apply(null, Event(false, 1, 1));
        var complete = CharacterCollections.Apply(partial, Event(true, 2, 2));
        var added = CharacterCollections.Apply(complete, Event(false, 3, 2, 3));
        check(partial.Achievements is { Complete: false } && complete.Achievements is { Complete: true, Completed: [2] } && added.Achievements!.Completed.SequenceEqual(new[] { 2, 3 }),
            "Achievement deltas are partial until full replacement and merge without duplicate completion IDs");
        check(CharacterCollections.Apply(added, Event(true, 2)).Achievements == added.Achievements &&
            CharacterCollections.Apply(added, Event(true, 4)).Achievements is { Complete: true, Completed.Length: 0 },
            "Stale achievement snapshots are ignored and a new empty full snapshot clears completions");
        var engine = new CombatEngine(); engine.Apply(new("network", Network: Event(true, 1, 1, 2)));
        engine.Apply(new("local-player", Actor: new(1, "Player", true, true, 0, "Cleric", 1)));
        check(engine.Fight.Actors[1].Collections?.Achievements is { Completed.Length: 2 } && !engine.Fight.Network.Any(e => e.Kind == "achievements"),
            "Pre-identity achievement snapshot binds to confirmed character without retained packet history");
        var store = new FightStore(Path.Combine(temp, "achievements-save")); var fight = engine.Snapshot(); store.Save(fight);
        check(store.Load(Path.Combine(store.Root, fight.Id + ".json")).Actors[1].Collections?.Achievements is { Completed.Length: 2 },
            "Completed achievements survive full fight save and load");
        check(FightArchiveSummary.Create(fight).Actors[1].Collections == null && fight.Actors[1].Collections?.Achievements is { Completed.Length: 2 },
            "Compact fight history drops completed achievement arrays without mutating full saved details");
        using var fixtures = new ReferencePacketFixtures(Path.Combine(temp, "reference-achievements-synthetic.jsonl"));
        foreach (bool full in new[] { true, false }) foreach (var body in new[] { Body(full), Body(full, (1, 2), (2, 0), (3, -1), (4, 1), (1, 0)) })
            fixtures.Add(new CapturedPayload(13328, 50000, Frame(full, body)) { TimestampMs = 100 });
        foreach (var (full, body) in new[] { (false, Body(false, (4, 0), (5, 2))), (true, Body(true)), (false, Body(false, (6, 1))) })
            fixtures.Add(new CapturedPayload(13328, 50000, Frame(full, body)) { TimestampMs = 101 });
    }
}
