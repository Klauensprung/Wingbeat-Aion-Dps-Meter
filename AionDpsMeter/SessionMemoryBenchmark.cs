using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class SessionMemoryBenchmark
{
    internal static int Run(string[] args)
    {
        try {
            int outputIndex = Array.IndexOf(args, "--output");
            if (outputIndex < 0 || outputIndex + 1 >= args.Length) throw new ArgumentException("Expected --output path.");
            if (args.Contains("--active-traffic")) return ActiveTraffic(args, args[outputIndex + 1]);
            if (args.Contains("--pending-inventory")) return PendingInventoryMemoryBenchmark.Run(args,args[outputIndex+1]);
            int rounds = 200, roundsIndex = Array.IndexOf(args, "--rounds");
            if (roundsIndex >= 0 && (roundsIndex + 1 >= args.Length || !int.TryParse(args[roundsIndex + 1], out rounds) || rounds is < 10 or > 2000))
                throw new ArgumentException("Rounds must be between 10 and 2000.");
            var engine = new CombatEngine(); var effects = new LiveEffectTracker(); var nearby = new PetNearbyTracker();
            var disk = args.Contains("--persist-fights") ? new FightStore(AppDataPaths.FilePath("benchmark-fights-" + Guid.NewGuid().ToString("N"))) : null;
            int persistedFights = 0; long persistedBytes = 0; bool persistenceValid = true;
            var archive = new List<Fight>(); var samples = new List<object>();
            var watch = Stopwatch.StartNew(); long allocated = GC.GetTotalAllocatedBytes(true);
            const long start = 1700000000000;
            void Network(string kind, long time, object fields) => engine.Apply(new("network", Network: new(kind, time, "synthetic", JsonSerializer.SerializeToElement(fields))));
            engine.Apply(new("local-player", Actor: new(1, "Synthetic player", true, true, 0, "Cleric", 1) { CharacterId = 30 }));
            Network("local-zone", start, new { MapId = 1010, X = 1000, Y = 1000, Z = 100 });
            Network("pet-roster", start, new CapturedPetRoster(1002, [new(1002, 1)], [new(1002, 1)], 0));
            long inventoryCount = 1000;
            Network("inventory", start, new InventoryBatch(true, [new(9000000000L, 110120003, inventoryCount, 1, 1, 0)]));
            for (int round = 0; round < rounds; round++) {
                long time = start + round * 60000L;
                Network("local-zone", time, new { MapId = 600012 });
                Network("dungeon-state", time, new DungeonStateReading(600012, 1, null, null));
                Network("dungeon-state", time, new DungeonStateReading(600012, 2, time, time + 600000));
                Network("map-event-join", time, new MapEventReading(10, "Synthetic dungeon progress", 1, 1, 0));
                for (int actor = 0; actor < 20; actor++) {
                    int id = 100 + round * 20 + actor;
                    engine.Apply(new("actor", Actor: new(id, "Synthetic NPC " + id, false, false, 0, "Unknown", 0) { NpcId = 2000001 }));
                }
                Network("zone-state", time, new ZoneStateReading(600012, 2, start + round / 5 * 300000L, 2, 123));
                for (int hit = 0; hit < 2000; hit++) {
                    long stamp = time + hit * 20;
                    int target = 100 + round * 20 + hit % 20;
                    engine.Apply(new("damage", Damage: new DamageEvent { ActorId = 1, TargetId = target, SkillCode = 17010001 + hit % 10, Timestamp = stamp, Damage = 100 }));
                    if (hit % 10 == 0) Network("abnormal-apply", stamp, new { TargetId = 1, Slot = hit % 40, AbnormalId = 1000, DurationMs = 3000, CasterId = 1 });
                    if (hit % 100 == 0) {
                        bool use = hit / 100 % 2 != 0; inventoryCount += use ? -1 : 3;
                        Network("inventory-update", stamp, new InventoryBatch(false,
                            [new(9000000000L, 110120003, inventoryCount, 1, 1, use ? (byte)0 : (byte)5, use ? (byte)14 : (byte)0)]));
                        Network("map-event-progress", stamp, new MapEventReading(10, null, hit == 1900 ? (byte)3 : (byte)1, 1, hit / 100 + 1));
                        Network("actor-position", stamp, new MovementReading(1, hit, round, 100, false, 0));
                        Network("actor-stats", stamp, new StatUpdateReading(1, new() { [19] = 100 + round }, 10000));
                        Network("pet-progress", stamp, new { Gains = new[] { new CapturedPetProgress(1002, 1) } });
                        Network("inventory-update", stamp, new InventoryBatch(false,
                            [new(5000000000L + hit, 500000001 + hit, round + 1, 1, round == 0 ? (byte)0 : (byte)1, 5)]));
                        effects.Observe(engine.Fight, stamp + 1, stamp - start); _ = effects.Active(stamp - start);
                        _ = nearby.Update(engine.Position.Latest, engine.Fight.Pets, 2); _ = engine.Loot.OverlaySnapshot();
                    }
                }
                Network("dungeon-result", time + 40000, new DungeonResultReading(600012, true, null, 1, []));
                Network("local-zone", time + 40001, new { MapId = 1010 });
                engine.EndFight("Synthetic reset");
                engine.SaveCompleted(f => {
                    if (disk != null) {
                        disk.Save(f);
                        string path = Path.Combine(disk.Root, f.Id + ".json");
                        var loaded = disk.Load(path); persistedFights++; persistedBytes += new FileInfo(path).Length;
                        persistenceValid &= loaded.Id == f.Id && loaded.Damage.Count == f.Damage.Count &&
                            loaded.Damage.Sum(h => h.Damage) == f.Damage.Sum(h => h.Damage) &&
                            loaded.DungeonRun is { ClearedAt: not null, Splits.Length: 20 } run &&
                            run.UsedItems.SingleOrDefault(i => i.ItemId == 110120003)?.Count == 10 &&
                            run.ReceivedItems.SingleOrDefault(i => i.ItemId == 110120003)?.Count == 30;
                    }
                    archive.Add(FightArchiveSummary.Create(f));
                });
                if ((round + 1) % 25 == 0 || round + 1 == rounds) {
                    long managed = GC.GetTotalMemory(true);
                    using var process = Process.GetCurrentProcess(); process.Refresh();
                    samples.Add(new { Fights = round + 1, ManagedBytes = managed, WorkingSetBytes = process.WorkingSet64,
                        PrivateBytes = process.PrivateMemorySize64, Actors = engine.Fight.Actors.Count,
                        ArchiveSearchNames = archive.Sum(f => f.ArchiveSummary!.Names.Length), LootRows = engine.Loot.Rows().Length,
                        DungeonRuns = archive.Select(f => f.DungeonRun?.Id).Where(id => id != null).Distinct().Count(),
                        Checkpoints = archive.Sum(f => f.DungeonRun?.Splits.Length ?? 0),
                        EffectSlots = effects.RetainedSlots, QueuedFights = engine.HasCompletedFights });
                }
            }
            using var final = Process.GetCurrentProcess();
            int rawEvents = archive.Sum(f => f.Damage.Count + f.Healing.Count + f.Network.Count);
            bool summariesValid = archive.Count == rounds && archive.All(f => f.DungeonRun is { Splits.Length: 20, ClearedAt: not null } run &&
                run.UsedItems.SingleOrDefault(i => i.ItemId == 110120003)?.Count == 10 && run.ReceivedItems.SingleOrDefault(i => i.ItemId == 110120003)?.Count == 30);
            File.WriteAllText(args[outputIndex + 1], JsonSerializer.Serialize(new { Rounds = rounds, Hits = rounds * 2000,
                ArchiveRawEvents = rawEvents, SummariesValid = summariesValid,
                Persistence = new { Enabled = disk != null, Directory = disk?.Root, Fights = persistedFights, Bytes = persistedBytes, Valid = persistenceValid,
                    Scope = "Optional real FightStore save/load per completed synthetic fight in a unique isolated directory; excludes UI autosave scheduling, maintenance stores and live Npcap." },
                AllocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated, PeakWorkingSetBytes = final.PeakWorkingSet64,
                ElapsedSeconds = watch.Elapsed.TotalSeconds, Samples = samples }, new JsonSerializerOptions { WriteIndented = true }));
            GC.KeepAlive(engine); GC.KeepAlive(archive); GC.KeepAlive(effects); GC.KeepAlive(nearby);
            return rawEvents == 0 && summariesValid && persistenceValid && (disk == null || persistedFights == rounds) && !engine.HasCompletedFights ? 0 : 1;
        } catch (Exception ex) { LocalLog.Write("Session memory benchmark failed: " + ex); return 1; }
    }
    static int ActiveTraffic(string[] args, string output)
    {
        int iterations = 250000, at = Array.IndexOf(args, "--iterations");
        if (at >= 0 && (at + 1 >= args.Length || !int.TryParse(args[at + 1], out iterations) || iterations is < 10000 or > 2000000))
            throw new ArgumentException("Iterations must be between 10000 and 2000000.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var engine = new CombatEngine();
        engine.Apply(new("local-player", Actor: new(1, "Synthetic", true, true, 0, "Cleric", 1) { PersistentId = 6000000001 }));
        engine.Apply(new("actor", Actor: new(10, "Synthetic target", false, false, 0, "Unknown", 0) { NpcId = 2000001 }));
        const long origin = 1700000000000;
        var cast = new CastReading(1, 17090000, 1, 0, 1000, 0, 0, 0, 0);
        var stop = new CastStopReading(1, 6152, 17090000, 0);
        void Network(string kind, long time, object value) => engine.Apply(new("network", Network: new(kind, time, "benchmark", JsonSerializer.SerializeToElement(value))));
        Network("inventory", origin, new InventoryBatch(true, [new(5000000000, 110120003, 0, 1, 1, 0)]));
        var samples = new List<object>(); var clock = Stopwatch.StartNew(); long allocated = GC.GetTotalAllocatedBytes(true);
        Fight? retainedSnapshot = null; int snapshots = 0; bool snapshotCountsValid = true;
        int interval = Math.Max(1, iterations / 10);
        void Sample(int completed) {
            using var process = Process.GetCurrentProcess(); process.Refresh();
            samples.Add(new { Iterations = completed, ManagedBytes = GC.GetTotalMemory(false), WorkingSetBytes = process.WorkingSet64,
                PrivateBytes = process.PrivateMemorySize64, DamageHits = engine.Fight.Damage.Count,
                RawNetworkEvents = engine.Fight.Network.Count, CastRows = engine.Fight.LiveCasts?.RowCount ?? 0,
                LootRows = engine.Fight.LiveLoot?.Rows().Length ?? 0, Gen2Collections = GC.CollectionCount(2) });
        }
        Sample(0);
        for (int n = 1; n <= iterations; n++) {
            long time = origin + n * 20L;
            Network("cast-start", time, cast); Network("cast-stop", time + 1, stop);
            Network("skill-end", time + 2, new { ActorId = 1, SkillCode = 17090000, Sequence = n % 256, StopReason = 0 });
            Network("skill-use", time + 2, new { ActorId = 1, SkillCode = 17090000, Sequence = n % 256 });
            Network("skill-cooldown", time + 2, new { SkillCode = 17090000, RemainingMs = 5000 });
            Network("inventory-update", time + 2, new InventoryBatch(false, [new(5000000000, 110120003, n, 1, 1, 5)]));
            if (n % 10 == 0) engine.Apply(new("damage", Damage: new() { ActorId = 1, TargetId = 10, SkillCode = 17090000, Timestamp = time + 3, Damage = 100 }));
            if (n % 1000 == 0) {
                var snapshot = engine.Snapshot(); snapshots++;
                snapshotCountsValid &= CastOutcomes.Build(snapshot).Rows.Single().Starts == n && LootLedger.FromFight(snapshot).Rows().Single().Quantity == n;
                snapshotCountsValid &= snapshot.LiveCooldowns?.RetainedCount == 2 && snapshot.LiveCooldowns.Remaining(17090000, time + 2) == 5;
                retainedSnapshot ??= snapshot;
            }
            if (n % interval == 0 || n == iterations) Sample(n);
        }
        var casts = CastOutcomes.Build(engine.Fight); var loot = LootLedger.FromFight(engine.Fight).Rows();
        bool valid = snapshotCountsValid && engine.Fight.Network.Count == 0 && engine.Fight.Damage.Count == iterations / 10 &&
            casts.Rows is [{ Starts: var starts, Interrupted: var interrupted }] && starts == iterations && interrupted == iterations &&
            loot is [{ Quantity: var quantity, Pickups: var pickups }] && quantity == iterations && pickups == iterations &&
            retainedSnapshot != null && CastOutcomes.Build(retainedSnapshot).Rows.Single().Starts == 1000 && LootLedger.FromFight(retainedSnapshot).Rows().Single().Quantity == 1000;
        long retained = GC.GetTotalMemory(true);
        using var final = Process.GetCurrentProcess(); final.Refresh();
        File.WriteAllText(output, JsonSerializer.Serialize(new {
            Mode = "active-traffic", Iterations = iterations, NetworkMessages = iterations * 6 + 1, DamageHits = engine.Fight.Damage.Count,
            CooldownStateEntries = engine.Snapshot().LiveCooldowns?.RetainedCount,
            SimulatedSeconds = iterations * .02, Snapshots = snapshots, SummaryCountsValid = valid, RawNetworkEvents = engine.Fight.Network.Count,
            CastRows = casts.Rows.Length, LootRows = loot.Length, AllocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated,
            FinalRetainedManagedBytes = retained, PeakWorkingSetBytes = final.PeakWorkingSet64, ElapsedSeconds = clock.Elapsed.TotalSeconds, Samples = samples,
            Scope = "One active fight, fresh serialized cast/stop/skill-end/skill-use/cooldown/inventory events, damage every ten iterations, detached snapshots every thousand. Samples use natural GC; only final retained measurement forces GC. Accelerated synthetic timestamps, no live Npcap, WPF, disk autosave or wall-clock soak. Damage history intentionally remains retained."
        }, new JsonSerializerOptions { WriteIndented = true }));
        GC.KeepAlive(engine); GC.KeepAlive(retainedSnapshot);
        return valid ? 0 : 1;
    }

}
