using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class VisibilityMemoryTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        string path = Path.Combine(temp, "visibility-memory.json");
        var memory = new VisibilityMemory(1, [new(1010, Enumerable.Repeat(5000d, 20).ToArray(), [])],
            [new(123, [5000, 5000], [7000, 7000])], [new(2000001, 2)], [new(1110, ["player"])]);
        VisibilityMemoryStore.Save(path, memory); var loaded = VisibilityMemoryStore.Load(path)!;
        var ranges = new VisibilityRanges(); ranges.Restore(loaded);
        var npc = new SpatialEntity(100, 2000001, 0, 0, 1000, Kind: "npc");
        check(ranges.NpcClass(2000001) == 2 && ranges.Find(1010, npc)?.Enter.Low == 20000 && ranges.Everywhere(1110, "player") &&
            ranges.Find(1010, npc with { NpcId = 0 })?.Enter.Low == 5000 && ranges.Find(1010, npc with { Kind = "object", ObjectId = 123 })?.Leave.High == 8000,
            "Learned map/object distances, NPC classes and global-visibility exclusions survive a local storage round trip");
        var original = File.ReadAllBytes(path); bool failed = false;
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            try { VisibilityMemoryStore.Save(path, memory with { Classes = [new(2000001, 3)] }); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
        }
        check(failed && original.SequenceEqual(File.ReadAllBytes(path)) && Directory.GetFiles(temp, "visibility-memory.json.*.tmp").Length == 0,
            "Failed atomic replacement preserves the previous visibility memory and cleans its temporary file");
        bool rejected = true;
        foreach (var invalid in new[] {
            memory with { Version = 2 }, memory with { Classes = [new(1, 4)] }, memory with { Classes = [new(1, 1), new(1, 2)] },
            memory with { Maps = [new(1010, new double[201], [])] }, memory with { Objects = [new(1, [-1], [])] },
            memory with { Everywhere = [new(1, ["npc", "npc"])] }, memory with { Everywhere = [new(1, ["unexpected"])] }
        }) {
            try { ranges.Restore(invalid); rejected = false; } catch (InvalidDataException) { }
        }
        check(rejected && ranges.NpcClass(2000001) == 2, "Invalid visibility memory is rejected before replacing valid in-memory learning");
        File.WriteAllText(path, "{broken");
        check(VisibilityMemoryStore.Load(path) == null && File.ReadAllText(path) == "{broken", "Malformed visibility memory is ignored without deleting the original file");
        File.WriteAllText(path, JsonSerializer.Serialize(memory with { Classes = [new(1, 4)] }));
        check(VisibilityMemoryStore.Load(path) == null, "Structurally valid but unsupported visibility data does not enter the estimator");
        using (var large = new FileStream(path, FileMode.Create)) large.SetLength(4 * 1024 * 1024 + 1);
        check(VisibilityMemoryStore.Load(path) == null && new FileInfo(path).Length == 4 * 1024 * 1024 + 1,
            "Oversized visibility files are rejected before parsing and retained for inspection");
        string enginePath = Path.Combine(temp, "engine-visibility.json");
        var engine = new CombatEngine(visibilityMemoryPath: enginePath); engine.SaveVisibilityMemory();
        check(!File.Exists(enginePath), "An engine with no learned changes does not create or rewrite a visibility file");
        engine.PositionPrediction.RestoreVisibility(memory); engine.SaveVisibilityMemory();
        File.SetLastWriteTimeUtc(enginePath, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)); engine.SaveVisibilityMemory();
        var restarted = new CombatEngine(visibilityMemoryPath: enginePath);
        check(File.GetLastWriteTimeUtc(enginePath).Year == 2020 && restarted.PositionPrediction.SnapshotVisibility().Classes.Single().Class == 2,
            "Engine restart restores learning and unchanged autosaves avoid repeated disk writes");
    }
}
