using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class NetworkRetentionTests
{
    internal static void Run(Action<bool, string> check)
    {
        var castEngine = new CombatEngine();
        castEngine.Apply(new("local-player", Actor: new(1, "Caster", true, false, 0, "Cleric", 2301)));
        castEngine.Apply(new("network", Network: new("charge-start", 1000, "test", JsonSerializer.SerializeToElement(
            new CastReading(1, 17010001, 1, 2, 0, 1, 0, 0, 0)))));
        check(castEngine.LiveCast.Snapshot(1000) is { Status: "Charging" }, "Skill-end retention regression begins with an active charge");
        for (int i = 0; i < 10000; i++) castEngine.Apply(new("network", Network: new("skill-end", 1001 + i, "test",
            JsonSerializer.SerializeToElement(new { ActorId = 1, SkillCode = 17010001, Sequence = i % 256, StopReason = 0 }))));
        check(castEngine.LiveCast.Snapshot(11001) == null && castEngine.Fight.Network.Count == 0 &&
            CastOutcomes.Build(castEngine.Fight).Rows is [{ Starts: 1 }],
            "Ten thousand skill-end messages clear live casting without retaining raw events or losing cast summaries");
        NetworkEvent Guard(int actor, long time, int value) => new("guard", time, "test",
            JsonSerializer.SerializeToElement(new GuardReading(actor, 1, 10000, value, 0, 0, 0, false, 0, true)));
        var guards = new CombatEngine(); guards.Apply(new("network", Network: Guard(1, 1, 9999)));
        var oldGuard = guards.Snapshot();
        for (int i = 2; i <= 10000; i++) guards.Apply(new("network", Network: Guard(1, i, 10000 - i)));
        guards.Apply(new("network", Network: Guard(1, 2, 9998)));
        check(guards.Fight.Network is [{ Timestamp: 10000 }] &&
            guards.Fight.Network[0].Fields.GetProperty("CurrentGuard").GetInt32() == 0 && oldGuard.Network[0].Timestamp == 1,
            "Repeated guard updates retain the latest actor state without stale replacement or snapshot mutation");
        for (int i = 2; i <= 4200; i++) guards.Apply(new("network", Network: Guard(i, 10000 + i, i)));
        check(guards.Fight.Network.Count == 4096 && guards.Fight.Network.All(e => e.Fields.GetProperty("ActorId").GetInt32() > 104),
            "Guard snapshots remain bounded to 4096 actors under identity churn");
        var recoveredGuards = new List<NetworkEvent> { Guard(1, 10, 100) };
        NetworkSnapshotRetention.Merge(recoveredGuards, [Guard(1, 20, 50), Guard(2, 20, 25), Guard(1, 15, 75)]);
        check(recoveredGuards.Count == 2 && recoveredGuards[0].Timestamp == 20,
            "Encounter recovery merges latest guard readings without reviving stale values");
        guards.NewFight("Guard reset"); guards.Apply(new("network", Network: Guard(1, 20000, 100)));
        check(guards.Fight.Network is [{ Timestamp: 20000 }], "New fight resets guard retention indices");
        NetworkEvent Map(int id, long time) => new("field-boss-list", time, "synthetic", JsonSerializer.SerializeToElement(new FieldBossList(id, [new(1, false, null, null, null, 1700000600000)])));
        var engine = new CombatEngine(); var initial = Map(1010, 1000);
        engine.Apply(new("network", Network: initial)); var before = engine.Snapshot();
        for (int i = 0; i < 10000; i++) engine.Apply(new("network", Network: initial with { Timestamp = 1001 + i }));
        check(engine.Fight.Network.Count == 1 && engine.Fight.Network[0].Timestamp == 11000 && before.Network.Single().Timestamp == 1000,
            "Ten thousand repeated map observations retain one latest event without mutating an earlier fight snapshot");
        engine.Apply(new("network", Network: initial));
        check(engine.Fight.Network.Single().Timestamp == 11000, "Stale field-boss snapshots cannot replace the retained latest map event");
        var combat = new NetworkEvent("abnormal-remove", 11001, "synthetic", JsonSerializer.SerializeToElement(new { TargetId = 1, Slot = 1 }));
        engine.Apply(new("network", Network: combat)); engine.Apply(new("network", Network: combat));
        for (int i = 0; i < 50; i++) engine.Apply(new("network", Network: Map(2000 + i, 12000 + i)));
        check(engine.Fight.Network.Count == 34 && engine.Fight.Network.Count(e => e.Kind == "abnormal-remove") == 2,
            "Map retention is capped at 32 while ordinary combat network events remain intact");
        var merged = new List<NetworkEvent> { Map(1010, 1000), combat };
        NetworkSnapshotRetention.Merge(merged, [Map(1010, 2000), Map(1020, 2000), combat]);
        check(merged.Count == 4 && merged[0].Timestamp == 2000 && merged.Count(e => e.Kind == "abnormal-remove") == 2,
            "Encounter recovery merges latest map snapshots without duplicating them or dropping combat events");
        engine.NewFight("Next"); engine.Apply(new("network", Network: Map(1010, 13000)));
        check(engine.Fight.Network.Count == 1 && before.Network.Count == 1, "New fight resets map retention indices and releases the old list");
        var fight = FeatureTests.Sample(); var effects = new LiveEffectTracker();
        for (int i = 1; i <= 1500; i++) fight.Network.Add(new("abnormal-apply", 10000 + i, "synthetic",
            JsonSerializer.SerializeToElement(new { TargetId = 1, Slot = i, AbnormalId = 100, DurationMs = uint.MaxValue })));
        effects.Observe(fight, 12000, 1000);
        check(effects.RetainedSlots == 1024 && effects.Active(1000).Length == 1024, "Live local effect state remains bounded under unique unknown-duration slots");
        var replay = fight.Copy(); replay.Id = "synthetic-recovery"; effects.Observe(replay, 13000, 2000);
        check(effects.RetainedSlots == 1024 && effects.Active(2000).All(e => e.Slot > 476),
            "Encounter replay cannot revive effect entries discarded by the retention limit");
        effects.Reset(); check(effects.RetainedSlots == 0, "Capture reset releases live effect retention state");
        var pets = new CombatEngine();
        pets.Apply(new("local-player", Actor: new(1, "Player", true, false, 0, "Cleric", 1)));
        pets.Apply(new("network", Network: new("pet-roster", 1000, "test", JsonSerializer.SerializeToElement(
            new CapturedPetRoster(1002, [new(1002, 1)], [new(1002, 0)], 0)))));
        var originalPets = pets.Snapshot();
        for (int i = 0; i < 1000; i++) pets.Apply(new("network", Network: new("pet-progress", 1001 + i, "test",
            JsonSerializer.SerializeToElement(new { Gains = new[] { new CapturedPetProgress(1002, 1) } }))));
        check(pets.Fight.Network.Count == 0 && pets.Fight.Pets!.Pets.Single().Souls == 1000 &&
            pets.Fight.Pets.Pets.Single().ObservedGains == 1000 && originalPets.Pets!.Pets.Single().Souls == 0,
            "Repeated pet updates retain one current snapshot without raw packet history or mutating earlier snapshots");
        var dungeon = new CombatEngine();
        const long start = 1700000000000;
        void Dungeon(string kind, long time, object value) => dungeon.Apply(new("network", Network: new(kind, time, "test", JsonSerializer.SerializeToElement(value))));
        Dungeon("dungeon-state", start, new DungeonStateReading(600002, 2, start, start + 60000));
        var beforeCompletion = dungeon.Snapshot();
        var preview = new DungeonResultReading(600002, true, 1, 2, Enumerable.Range(1, 128).Select(i => new DungeonReward(i, 10)).ToArray());
        for (int i = 1; i <= 1000; i++) {
            Dungeon("dungeon-result", start + i * 2, preview);
            Dungeon("dungeon-state", start + i * 2 + 1, new DungeonStateReading(600002, 3, start + i, start + 60000 + i));
        }
        var compact = dungeon.Snapshot().ForStorage();
        check(dungeon.Fight.Network.Count == 0 && compact.Network.Count == 0 &&
            compact.DungeonRun is { MapId: 600002, ClearLevel: 2, ReceivedItems.Length: 0 } &&
            compact.DungeonRun.ClearedAt == start + 2 && compact.DungeonRun.StartedAt == start && beforeCompletion.DungeonRun?.ClearedAt == null,
            "Repeated dungeon timers and reward previews retain only compact run metadata without raw history or invented loot");
        var legacy = dungeon.Snapshot();
        legacy.Network.Add(new("dungeon-result", start + 1, "legacy", JsonSerializer.SerializeToElement(preview)));
        check(legacy.ForStorage().Network.Count == 0 && legacy.ForStorage().DungeonRun == compact.DungeonRun,
            "Resaving legacy completion evidence drops redundant reward previews while preserving the run summary");
    }
}
