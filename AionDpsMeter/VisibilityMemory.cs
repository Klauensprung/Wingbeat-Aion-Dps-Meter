using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal sealed record VisibilitySamples(int Id, double[] Enter, double[] Leave);
internal sealed record VisibilityClass(int Npc, int Class);
internal sealed record VisibilityMapKinds(int Map, string[] Kinds);
internal sealed record VisibilityMemory(int Version, VisibilitySamples[] Maps, VisibilitySamples[] Objects, VisibilityClass[] Classes, VisibilityMapKinds[] Everywhere);

internal sealed partial class VisibilityRanges
{
    internal VisibilityMemory Snapshot() => new(1, maps.Select(p => new VisibilitySamples(p.Key, p.Value.Enter.ToArray(), p.Value.Leave.ToArray())).ToArray(),
        objects.Select(p => new VisibilitySamples(p.Key, p.Value.Enter.ToArray(), p.Value.Leave.ToArray())).ToArray(),
        npcClasses.Select(p => new VisibilityClass(p.Key, p.Value)).ToArray(), everywhere.Select(p => new VisibilityMapKinds(p.Key, p.Value.ToArray())).ToArray());
    internal void Restore(VisibilityMemory memory) {
        Validate(memory);
        maps.Clear(); objects.Clear(); npcClasses.Clear(); everywhere.Clear(); checkedEntry = null;
        void Populate(VisibilitySamples[] rows, Dictionary<int, Samples> target) {
            foreach (var row in rows) { var sample = new Samples(); sample.Enter.AddRange(row.Enter); sample.Leave.AddRange(row.Leave); target.Add(row.Id, sample); }
        }
        Populate(memory.Maps, maps); Populate(memory.Objects, objects);
        foreach (var row in memory.Classes) npcClasses.Add(row.Npc, row.Class);
        foreach (var row in memory.Everywhere) everywhere.Add(row.Map, row.Kinds.ToHashSet());
        Revision++;
    }
    internal static void Validate(VisibilityMemory memory) {
        static bool SamplesValid(VisibilitySamples[]? rows, int count, int samples) => rows != null && rows.Length <= count &&
            rows.All(r => r != null && r.Id > 0 && r.Enter != null && r.Leave != null && r.Enter.Length <= samples && r.Leave.Length <= samples &&
                r.Enter.Concat(r.Leave).All(v => double.IsFinite(v) && v is >= 0 and <= 40000000 && v == Math.Truncate(v))) && rows.Select(r => r.Id).Distinct().Count() == rows.Length;
        if (memory == null || memory.Version != 1 || !SamplesValid(memory.Maps, 256, 200) || !SamplesValid(memory.Objects, 4096, 15) ||
            memory.Classes == null || memory.Classes.Length > 4096 || memory.Classes.Any(r => r == null || r.Npc <= 0 || r.Class is < 1 or > 3) || memory.Classes.Select(r => r.Npc).Distinct().Count() != memory.Classes.Length ||
            memory.Everywhere == null || memory.Everywhere.Length > 256 || memory.Everywhere.Any(r => r == null || r.Map <= 0 || r.Kinds == null || r.Kinds.Length > 2 || r.Kinds.Any(k => k is not ("npc" or "player")) || r.Kinds.Distinct().Count() != r.Kinds.Length) || memory.Everywhere.Select(r => r.Map).Distinct().Count() != memory.Everywhere.Length)
            throw new InvalidDataException("Invalid learned visibility memory.");
    }
}

internal static class VisibilityMemoryStore
{
    const int Limit = 4 * 1024 * 1024;
    internal static VisibilityMemory? Load(string path) {
        if (!File.Exists(path)) return null;
        try {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > Limit) throw new InvalidDataException();
            var memory = JsonSerializer.Deserialize<VisibilityMemory>(stream) ?? throw new InvalidDataException();
            VisibilityRanges.Validate(memory); return memory;
        } catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) {
            LocalLog.Write("Learned visibility memory could not be loaded; original file retained."); return null;
        }
    }
    internal static void Save(string path, VisibilityMemory memory) {
        VisibilityRanges.Validate(memory);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(memory);
        if (bytes.Length > Limit) throw new InvalidDataException("Learned visibility memory exceeds storage limit.");
        path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, path, true);
        } finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
