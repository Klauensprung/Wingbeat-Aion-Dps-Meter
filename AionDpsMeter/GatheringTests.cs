using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class GatheringTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        byte[] Body(int basic, int bonus, byte result = 0) {
            var w = new PartyWireTests.Writer(); w.Raw(result, 0); w.Var(1); w.Int(123); w.Int(basic); w.Int(bonus); w.Int(456); w.Int(9); return w.Bytes.ToArray();
        }
        byte[] Frame(ushort op, byte[] body) { var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw((byte)(op >> 8), (byte)op); w.Raw(body); return w.Bytes.ToArray(); }
        DecodeResult Decode(byte[] body) => new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, Frame(0x338d, body)) { TimestampMs = 20 });
        check(Decode(Body(0, 0)).Events.Count == 0 && Decode(Body(1, -1)).Events.Count == 0 && Decode(Body(0, 1, 17)).Events.Single().Network!.Kind == "gathered" &&
            Decode(Body(int.MaxValue, int.MaxValue)).Events.Count == 1, "Gather completion follows summed main reward counts without overflow or inventing success from bonus items/result byte");
        var bytes = Body(1, 0);
        check(Enumerable.Range(0, bytes.Length).All(n => Decode(bytes[..n]).Events.Count == 0) && Decode([..bytes, 0]).Events.Count == 0,
            "Gather completion rejects every truncation and unknown trailer");
        var engine = new CombatEngine();
        var spawn = new NetworkEvent("world-object", 10, "test", JsonSerializer.SerializeToElement(new WorldObjectReading(1, 1234, 1, 2, 3, 0, true, 0, 0, 0, 0)));
        engine.Apply(new("network", Network: spawn)); foreach (var e in Decode(bytes).Events) engine.Apply(e);
        var snapshot = engine.Snapshot();
        check(snapshot.GatheredObjects.GetValueOrDefault(1234) == 1 && snapshot.Network.Count == 0 && snapshot.HasRecordedData,
            "Confirmed gathering creates a saveable compact summary without raw packets or invented loot");
        foreach (var e in Decode(bytes).Events) engine.Apply(e);
        check(snapshot.GatheredObjects[1234] == 1 && engine.Fight.GatheredObjects[1234] == 2, "Gathering snapshots do not share mutable counters");
        var store = new FightStore(Path.Combine(temp, "gathering-save")); store.Save(snapshot);
        check(store.Load(Path.Combine(store.Root, snapshot.Id + ".json")).GatheredObjects[1234] == 1 && FightArchiveSummary.Create(snapshot).GatheredObjects[1234] == 1,
            "Gathering summary survives full save/load and compact archive storage");
        check(SavedHistory.Includes(FightArchiveSummary.Create(snapshot)) && SavedHistory.Activity(snapshot) == "Gathering",
            "Gathering-only records remain discoverable in saved history without combat or loot packets");
        engine.Apply(new("network", Network: new("world-object-removed", 30, "test", JsonSerializer.SerializeToElement(new WorldObjectRemoval(1, 0, 0, 0)))));
        foreach (var e in Decode(bytes).Events) engine.Apply(e);
        check(engine.Fight.GatheredObjects[1234] == 2, "Unknown or removed gather objects cannot inflate resolved gathering counts");
        var merged = snapshot.Copy(); Gathering.Merge(merged, snapshot);
        check(merged.GatheredObjects[1234] == 2 && snapshot.GatheredObjects[1234] == 1, "Fight recovery merges gathering totals without mutating previous snapshots");
        var other = new Fight(); other.GatheredObjects[1234] = 3; other.GatheredObjects[5678] = 4;
        var overview = Gathering.Summarize([snapshot, other, merged]);
        check(overview.Records == 2 && overview.Rows.Single(r => r.ObjectId == 1234) is { Count: 5, Records: 2 } &&
            overview.Rows.Single(r => r.ObjectId == 5678).Count == 4,
            "Cumulative gathering replaces duplicate fight IDs with the latest snapshot and combines distinct records once");
        var wide = new Fight(); wide.GatheredObjects[1234] = long.MaxValue;
        check(Gathering.Summarize([wide, other]).Rows.Single(r => r.ObjectId == 1234).Count == (decimal)long.MaxValue + 3,
            "Cumulative gathering counts do not overflow signed 64-bit storage totals");
        var many = Enumerable.Range(0, 5).Select(group => new Fight { GatheredObjects = Enumerable.Range(1 + group * 4096, 4096).ToDictionary(id => id, _ => 1L) }).ToArray();
        var bounded = Gathering.Summarize(many);
        check(bounded.Rows.Length == 16384 && bounded.OmittedGathers == 4096,
            "Cumulative gathering bounds displayed categories and reports omitted totals explicitly");
        var limited = many[0].Copy(); Gathering.Merge(limited, many[1]);
        var limitedStore = new FightStore(Path.Combine(temp, "limited-gathering")); limitedStore.Save(limited);
        var limitedRestored = limitedStore.Load(Path.Combine(limitedStore.Root, limited.Id + ".json"));
        check(limitedRestored.OmittedGathers == 4096 && Gathering.Summarize([FightArchiveSummary.Create(limitedRestored)]).OmittedGathers == 4096,
            "Per-record gathering omissions survive recovery merge, save/load and archive aggregation");
        engine = new CombatEngine();
        var original = new Actor(10, "Original", true, true, 0, "Cleric", 2301);
        engine.Apply(new("local-player", Actor: original));
        void Send(string kind, long at, object fields) => engine.Apply(new("network", Network: new(kind, at, "synthetic", JsonSerializer.SerializeToElement(fields))));
        void Spawn(long at, int id) => Send("world-object", at, new WorldObjectReading(1, id, 1, 2, 3, 0, true, 0, 0, 0, 0));
        void Gather(long at) => Send("gathered", at, new { EntityId = 1 });
        Spawn(10, 1500045); Gather(11);
        Send("account-login", 20, new LoginReading(0, 2301, []));
        Send("local-zone", 21, new { MapId = 1010, AwaitingIdentity = true });
        Spawn(22, 1590001); Gather(23);
        check(engine.Fight.GatheredObjects.Count == 1 && engine.Fight.GatheredObjects.GetValueOrDefault(1500045) == 1,
            "Incoming gather completion cannot be credited to the previous character during identity handoff");
        engine.Apply(new("local-player", Actor: original with { Id = 20, Name = "Incoming" }));
        check(engine.Fight.GatheredObjects.Count == 1 && engine.Fight.GatheredObjects.GetValueOrDefault(1590001) == 1,
            "Pending gathering totals survive identity confirmation under the incoming character");
        var prior = engine.SaveCompleted(_ => { });
        check(prior.Any(f => f.LocalPlayer == 10 && f.GatheredObjects.Count == 1 && f.GatheredObjects.GetValueOrDefault(1500045) == 1),
            "Previous character gathering autosave excludes incoming totals");
        Gather(19); Gather(24);
        check(engine.Fight.GatheredObjects[1590001] == 2, "Stale gathers stay rejected while fresh gathers continue after confirmation");
        var confirmedGathers = engine.Snapshot(); engine.NewFight("New fight");
        check(engine.Fight.GatheredObjects.Count == 0 && confirmedGathers.GatheredObjects[1590001] == 2,
            "DPS reset starts a fresh gathering summary without altering captured totals");
        Send("local-zone", 30, new { MapId = 1110, AwaitingIdentity = true });
        Spawn(31, 1500045); Gather(32); engine.NewFight("Reset while loading");
        engine.Apply(new("local-player", Actor: original));
        check(engine.Fight.GatheredObjects.GetValueOrDefault(1500045) == 1,
            "Gathering during map preparation survives a manual DPS reset before identity confirmation");
        Send("account-login", 40, new LoginReading(0, 2301, []));
        Spawn(41, 1590001); Gather(42);
        Send("account-login", 40, new LoginReading(0, 2301, []));
        Send("local-zone", 43, new { MapId = 1010, AwaitingIdentity = true });
        engine.Apply(new("local-player", Actor: original));
        check(engine.Fight.GatheredObjects.Count == 1 && engine.Fight.GatheredObjects[1590001] == 1,
            "Duplicate login and first prepared map preserve the incoming gathering summary");
        using var fixtures = new ReferencePacketFixtures(Path.Combine(temp, "reference-gathering-synthetic.jsonl"));
        var objectBody = new PartyWireTests.Writer(); objectBody.Var(1); objectBody.Raw(0, 0); objectBody.Int(1234); objectBody.Zero(20); objectBody.Raw(0); objectBody.Bool(true); objectBody.Raw(0); objectBody.Long(0); objectBody.Zero(8); objectBody.Raw(0, 0); objectBody.Var(0);
        fixtures.Add(new CapturedPayload(13328, 50000, Frame(0x3436, objectBody.Bytes.ToArray())) { TimestampMs = 10 });
        foreach (var b in new[] { Body(1, 0), Body(0, 0), Body(1, -1), Body(0, 1, 17), Body(int.MaxValue, int.MaxValue) })
            fixtures.Add(new CapturedPayload(13328, 50000, Frame(0x338d, b)) { TimestampMs = 20 });
        fixtures.Add(new CapturedPayload(13328, 50000, Frame(0x3536, [1, 0, 0, 0])) { TimestampMs = 30 });
        fixtures.Add(new CapturedPayload(13328, 50000, Frame(0x338d, bytes)) { TimestampMs = 40 });
    }
}
