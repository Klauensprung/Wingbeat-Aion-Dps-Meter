namespace AionDpsMeter.Local;

internal sealed record VisibilityBand(double Low, double High);
internal sealed record VisibilityRange(VisibilityBand Enter, VisibilityBand Leave);

// Bounded learned distances. No network history or character identity is retained.
internal sealed partial class VisibilityRanges
{
    sealed class Samples { internal readonly List<double> Enter = [], Leave = []; }
    readonly Dictionary<int, Samples> maps = new(), objects = new();
    readonly Dictionary<int, int> npcClasses = new();
    readonly Dictionary<int, HashSet<string>> everywhere = new();
    (int Map, long Boundary)? checkedEntry;
    internal long Revision { get; private set; }
    internal static readonly VisibilityRange Default = new(new(3800, 6100), new(6300, 7800));
    static readonly VisibilityRange[] Extended = [new(new(9000, 11800), new(11500, 13500)), new(new(20000, 25500), new(26000, 29500))];
    internal int NpcClass(int npc) => npcClasses.GetValueOrDefault(npc);
    internal bool Everywhere(int map, string kind) => everywhere.TryGetValue(map, out var kinds) && kinds.Contains(kind);
    internal void DetectEverywhere(int map, IEnumerable<SpatialEntity> entities, CapturedPosition? measurement, long timestamp, long boundary) {
        if (map <= 0 || boundary < 0 || timestamp - boundary < 4000 || measurement == null || measurement.MapId != map ||
            measurement.Timestamp > timestamp || timestamp - measurement.Timestamp > 8000 || checkedEntry == (map, boundary)) return;
        checkedEntry = (map, boundary);
        maps.TryGetValue(map, out var sample);
        double threshold = MapBand(sample?.Leave, Default.Leave).High * 1.3;
        foreach (string kind in new[] { "npc", "player" }) {
            int count = 0, distant = 0;
            foreach (var entity in entities) {
                if (!entity.Visible || entity.Moving || entity.Kind != kind || entity.At > timestamp || NpcClass(entity.NpcId) != 0 ||
                    entity.X is not double x || entity.Y is not double y) continue;
                double dz = entity.Z is double z ? z - measurement.Z : 0;
                double distance = Math.Sqrt((x - measurement.X) * (x - measurement.X) + (y - measurement.Y) * (y - measurement.Y) + dz * dz);
                count++; if (distance > threshold) distant++;
            }
            if (count < 10) continue;
            bool global = distant / (double)count >= .5;
            if (!everywhere.TryGetValue(map, out var kinds)) {
                if (!global) continue;
                if (everywhere.Count >= 256) everywhere.Remove(everywhere.Keys.First());
                everywhere[map] = kinds = new();
            }
            if (global ? kinds.Add(kind) : kinds.Remove(kind)) Revision++;
        }
    }
    internal void Classify(int map, IEnumerable<SpatialEntity> entities, CapturedPosition? measurement, PositionMoments? estimate, long timestamp, long boundary) {
        bool direct = measurement != null && measurement.MapId == map && measurement.Timestamp <= timestamp &&
            (timestamp - measurement.Timestamp <= 500 || boundary >= 0 && Math.Abs(measurement.Timestamp - boundary) < 4000 && timestamp - measurement.Timestamp < 8000);
        if (!direct && (estimate == null || estimate.Accuracy > 300)) return;
        double x = direct ? measurement!.X : estimate!.X, y = direct ? measurement!.Y : estimate!.Y;
        double? z = direct ? measurement!.Z : estimate!.Z;
        maps.TryGetValue(map, out var sample);
        double[] limits = [MapBand(sample?.Leave, Default.Leave).High, Extended[0].Leave.High, Extended[1].Leave.High];
        foreach (var entity in entities) {
            if (!entity.Visible || entity.Moving || entity.NpcId <= 0 || entity.At > timestamp || entity.X is not double ex || entity.Y is not double ey) continue;
            int current = NpcClass(entity.NpcId); if (current >= limits.Length) continue;
            double dz = entity.Z is double ez && z is double pz ? ez - pz : 0;
            double distance = Math.Sqrt((ex - x) * (ex - x) + (ey - y) * (ey - y) + dz * dz);
            if (distance <= limits[current] * (direct ? 1.05 : 1.4)) continue;
            int next = Array.FindIndex(limits, limit => distance <= limit * 1.05);
            if (next < 0) next = limits.Length;
            if (next <= current) continue;
            if (npcClasses.Count >= 4096 && !npcClasses.ContainsKey(entity.NpcId)) npcClasses.Remove(npcClasses.Keys.First());
            npcClasses[entity.NpcId] = next;
            Revision++;
        }
    }
    static double Quantile(IEnumerable<double> values, double fraction) {
        var sorted = values.Order().ToArray(); return sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * fraction))];
    }
    static VisibilityBand MapBand(List<double>? values, VisibilityBand fallback) {
        if (values == null || values.Count < 20) return fallback;
        double median = Quantile(values, .5);
        var filtered = values.Where(v => v > median / 2 && v < median * 1.5).ToArray();
        return filtered.Length < 20 ? fallback : new(Quantile(filtered, .1), Quantile(filtered, .9));
    }
    internal VisibilityRange? Find(int map, SpatialEntity entity) {
        if (entity.Kind != "object") {
            if (Everywhere(map, entity.Kind == "unknown" ? "npc" : entity.Kind)) return null;
            int category = NpcClass(entity.NpcId);
            if (category > 0) return category <= Extended.Length ? Extended[category - 1] : null;
            maps.TryGetValue(map, out var sample);
            return new(MapBand(sample?.Enter, Default.Enter), MapBand(sample?.Leave, Default.Leave));
        }
        if (!objects.TryGetValue(entity.ObjectId, out var data) || data.Enter.Count < 2 || data.Leave.Count < 2) return null;
        double enter = Quantile(data.Enter, .5), leave = Quantile(data.Leave, .5);
        double center = (enter + leave) / 2, spread = Math.Max(500, Math.Abs(leave - enter) / 2);
        return new(new(center - 2 * spread, center + spread), new(center - spread, center + 2 * spread));
    }
    internal void Learn(int map, SpatialEntity entity, bool entering, CapturedPosition? measured, long timestamp, PositionMoments? inferred = null) {
        if (map <= 0 || entity.Moving || entity.Kind != "object" && (NpcClass(entity.NpcId) != 0 || Everywhere(map, entity.Kind == "unknown" ? "npc" : entity.Kind)) ||
            entity.X is not double x || entity.Y is not double y) return;
        bool direct = measured != null && measured.MapId == map && Math.Abs(timestamp - measured.Timestamp) <= 500;
        if (!direct && (entity.Kind != "object" || Find(map, entity) != null || inferred == null || !double.IsFinite(inferred.Accuracy) || inferred.Accuracy is < 0 or > 300)) return;
        double px = direct ? measured!.X : inferred!.X, py = direct ? measured!.Y : inferred!.Y;
        double? pz = direct ? measured!.Z : inferred!.Z;
        double dz = entity.Z is double z && pz is double height ? z - height : 0;
        double distance = Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py) + dz * dz);
        if (!double.IsFinite(distance)) return;
        bool worldObject = entity.Kind == "object";
        int key = worldObject ? entity.ObjectId : map;
        if (key <= 0) return;
        var entries = worldObject ? objects : maps;
        if (!entries.TryGetValue(key, out var samples)) {
            if (entries.Count >= (worldObject ? 4096 : 256)) entries.Remove(entries.Keys.First());
            entries[key] = samples = new();
        }
        var list = entering ? samples.Enter : samples.Leave;
        int limit = worldObject ? 15 : 200;
        if (list.Count >= limit) list.RemoveAt(0);
        list.Add(Math.Round(distance, MidpointRounding.AwayFromZero));
        Revision++;
    }
}
