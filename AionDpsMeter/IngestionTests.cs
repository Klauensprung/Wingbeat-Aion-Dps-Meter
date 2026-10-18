using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class IngestionTests
{
    sealed class OrderedDecoder : IGamePacketDecoder
    {
        int sequence;
        public string Target => "Synthetic ordering test";
        public DecodeResult Decode(CapturedPayload payload)
        {
            sequence++;
            return new(DecodeState.Recognized, [new("damage", Damage: new DamageEvent {
                ActorId = 1, TargetId = 2, Timestamp = 1000 + sequence, Damage = sequence
            })], "");
        }
    }
    static void Hit(CombatEngine engine, long time) => engine.RecordDamage(new() { ActorId = 1, TargetId = 2, Timestamp = time, Damage = 10 }, false);

    public static void Run(Action<bool, string> check, string temp)
    {
        var engine = new CombatEngine(new OrderedDecoder());
        Hit(engine, 100); engine.EndFight("Next");
        using var saving = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var save = Task.Run(() => engine.SaveCompleted(_ => {
            saving.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test save was not released.");
        }));
        try
        {
            check(saving.Wait(TimeSpan.FromSeconds(5)), "Background save reached the simulated slow disk");
            var ingestion = Task.Run(() => {
                for (int i = 0; i < 10000; i++) engine.Process(new(13328, 1, []));
                return engine.Snapshot();
            });
            check(ingestion.Wait(TimeSpan.FromSeconds(5)), "Ingestion and snapshots continue while a completed fight is saving");
            var snapshot = ingestion.Result;
            check(snapshot.Damage.Select(e => e.Damage).SequenceEqual(Enumerable.Range(1, 10000)),
                "Ten thousand decoded events retain order without missing or duplicated hits during a blocked save");
        }
        finally { release.Set(); save.GetAwaiter().GetResult(); }
        check(save.Result.Length == 1 && save.Result[0].Damage.Count == 1, "Save acknowledgment removes only its completed snapshot");

        engine.EndFight("After burst");
        bool failed = false;
        try { engine.SaveCompleted(_ => throw new IOException("Simulated write failure")); }
        catch (IOException) { failed = true; }
        var retried = engine.SaveCompleted(_ => { });
        check(failed && retried.Length == 1 && retried[0].Damage.Count == 10000 && engine.SaveCompleted(_ => { }).Length == 0,
            "Failed saves retain all events for a retry and successful saves are acknowledged once");

        var rollover = new CombatEngine();
        Hit(rollover, 1000); var early = rollover.Snapshot(); Hit(rollover, 2000); rollover.EndFight("Rollover");
        var store = new FightStore(Path.Combine(temp, "save-order"));
        var written = FightPersistence.Save(rollover, early, store.Save);
        check(written.Length == 2 && store.Load(store.Files().Single()).Damage.Count == 2,
            "Final completed snapshot wins when capture rolls over after an autosave snapshot was taken");
        Hit(rollover, 3000);
        check(rollover.Snapshot().Damage.Count == 1 && early.Damage.Count == 1 && written[^1].Damage.Count == 2,
            "Capture, autosave and completed fights retain separate event lists");

        var resets = new CombatEngine();
        Hit(resets, 1000); resets.EndFight("Second");
        int writes = 0;
        var resetSaves = resets.SaveCompleted(_ => {
            if (++writes != 1) return;
            // Simulate further reset clicks while the first write is in flight.
            Hit(resets, 2000); resets.EndFight("Third");
            Hit(resets, 3000); resets.EndFight("Fourth");
        });
        check(resetSaves.Length == 3 && resetSaves.All(f => f.Damage.Count == 1) &&
            resetSaves.Select(f => f.Id).Distinct().Count() == 3 && !resets.HasCompletedFights && !resets.Snapshot().HasEvents,
            "Repeated resets during a save retain each encounter and leave an empty live fight");
        Hit(resets, 4000); resets.EndFight("Retry");
        try { resets.SaveCompleted(_ => throw new IOException("Disk unavailable")); } catch (IOException) { }
        check(resets.HasCompletedFights, "Failed reset saves remain discoverable for retry even without active capture");

        var large = new Fight { Label = "Large encounter – streaming regression" };
        for (int i = 0; i < 50000; i++) large.Damage.Add(new() { ActorId = 1, TargetId = 2, Timestamp = i, Damage = i });
        var largeStore = new FightStore(Path.Combine(temp, "large-save"));
        largeStore.Save(new Fight()); // Warm the serializer before measuring allocations.
        long before = GC.GetAllocatedBytesForCurrentThread();
        largeStore.Save(large);
        long streamedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        string json = JsonSerializer.Serialize(large);
        long stringBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        check(streamedBytes < stringBytes / 2,
            $"Large fight save avoids whole-file JSON allocations (stream {streamedBytes:N0}, string {stringBytes:N0} bytes)");
        var loaded = largeStore.Load(Path.Combine(largeStore.Root, large.Id + ".json"));
        check(loaded.Label == large.Label && loaded.Damage.Count == large.Damage.Count && loaded.Damage[^1].Damage == 49999,
            "Streamed large fights round-trip all events and Unicode labels");
        GC.KeepAlive(json);
    }
}
