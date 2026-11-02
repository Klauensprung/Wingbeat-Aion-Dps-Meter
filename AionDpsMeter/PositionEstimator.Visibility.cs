namespace AionDpsMeter.Local;

internal sealed partial class PositionEstimator
{
    readonly record struct ViewSource(SpatialEntity Entity, VisibilityBand Band, double Error, bool Ordinary);
    readonly double[] likelihoods = new double[Capacity], neighborScratch = new double[120];
    int viewChanges;
    List<T> Sample<T>(List<T> values) {
        if (values.Count <= 120) return values;
        for (int i = values.Count - 1; i > 0; i--) { int j = random.Next(i + 1); (values[i], values[j]) = (values[j], values[i]); }
        return values.GetRange(0, 120);
    }
    static double LogLikelihood(double outside, double variance) => outside == 0 ? 0 : Math.Log(.05 + .95 * Math.Exp(-.5 * outside * outside / variance));
    void UpdateVisibleScene(SpatialEvidence scene, long timestamp, long elapsed) {
        int changes = viewChanges; viewChanges = 0;
        var visible = new List<ViewSource>(); var departed = new List<ViewSource>(); var ordinary = new List<ViewSource>();
        double density = 0;
        foreach (var entity in scene.Entities) {
            if (entity.At > timestamp || entity.X == null || entity.Y == null || entity.LeftViewAt is long left && timestamp - left > 120000) continue;
            var range = visibilityRanges.Find(scene.MapId, entity); if (range == null) continue;
            bool regular = entity.Kind != "object" && visibilityRanges.NpcClass(entity.NpcId) == 0;
            double error = entity.Moving ? Math.Min(4000, Math.Max(0, timestamp - entity.PositionAt) / 1000d * 600) : 0;
            if (entity.Visible) {
                var source = new ViewSource(entity, new(0, range.Leave.High), error, regular);
                visible.Add(source); if (regular) ordinary.Add(source);
                if (regular && entity.Kind != "player" && !entity.Moving)
                    density += 4 / (Math.PI * (range.Enter.Low + range.Enter.High + range.Leave.Low + range.Leave.High) / 4);
            } else if (entity.LeftViewAt != null && !entity.Moving)
                departed.Add(new(entity, new((range.Enter.Low + range.Enter.High) / 2, double.PositiveInfinity), error, regular));
        }
        if (visible.Count == 0) return;
        if (map == 0) map = scene.MapId; if (map != scene.MapId) return;
        Advance(timestamp);
        bool dense = ordinary.Count >= 60, neighbors = ordinary.Count >= 4;
        double radius = visibilityRanges.Find(scene.MapId, new(0, 0, 0, 0, timestamp))?.Enter.High ?? VisibilityRanges.Default.Enter.High;
        void Propose(int index, List<ViewSource> sources, double distanceLimit) {
            var source = sources[random.Next(sources.Count)].Entity;
            double distance = distanceLimit * Math.Sqrt(random.NextDouble()), angle = random.NextDouble() * Math.PI * 2;
            particles[index] = new() { X = source.X!.Value + Math.Cos(angle) * distance, Y = source.Y!.Value + Math.Sin(angle) * distance,
                Z = source.Z ?? double.NaN, Still = random.NextDouble() < .5, Weight = 1 };
        }
        if (!initialized) { for (int i = 0; i < Capacity; i++) Propose(i, visible, radius); initialized = true; }
        else {
            double maxWeight = particles.Max(p => p.Weight);
            for (int i = 0; i < Capacity; i++) particles[i].Weight /= maxWeight;
            var weakest = Enumerable.Range(0, Capacity).OrderBy(i => particles[i].Weight).Take(40).ToArray();
            foreach (int i in weakest) Propose(i, dense ? ordinary : visible, dense ? radius / 4 : radius);
        }
        if (neighbors) {
            var selected = Sample(ordinary);
            for (int i = 0; i < Capacity; i++) {
                for (int j = 0; j < selected.Count; j++) neighborScratch[j] = double.Hypot(particles[i].X - selected[j].Entity.X!.Value, particles[i].Y - selected[j].Entity.Y!.Value);
                Array.Sort(neighborScratch, 0, selected.Count);
                particles[i].NeighborDistance = neighborScratch[selected.Count / 2] / Math.Sqrt(.5);
            }
        }
        double minimum = particles.Min(p => p.NeighborDistance);
        var enteringSources = Sample(visible);
        var leavingSources = Sample(dense ? departed.Where(v => !v.Ordinary).ToList() : departed);
        var distanceSources = enteringSources.Concat(leavingSources).ToArray();
        var denseDepartures = dense ? Sample(departed.Where(v => v.Ordinary).ToList()) : [];
        bool useArrivals = !dense && elapsed is > 0 and <= 5000 && timestamp - scene.Boundary >= 4000;
        double peak = double.NegativeInfinity;
        for (int i = 0; i < Capacity; i++) {
            ref var p = ref particles[i]; double score = Math.Log(p.Weight);
            if (useArrivals) { double expected = (.5 + density * (p.Still ? 0 : double.Hypot(p.Vx, p.Vy))) * elapsed / 1000d; score += changes * Math.Log(expected) - expected; }
            if (neighbors) score -= (p.NeighborDistance / Math.Max(1, minimum) - 1) / (dense ? 1 : .6);
            foreach (var source in denseDepartures) {
                double distance = double.Hypot(p.X - source.Entity.X!.Value, p.Y - source.Entity.Y!.Value);
                score += LogLikelihood(Math.Max(0, p.NeighborDistance - distance), Math.Max(1e-12, .09 * p.NeighborDistance * p.NeighborDistance));
            }
            foreach (var source in distanceSources) {
                var entity = source.Entity; double dz = entity.Z is double z && double.IsFinite(p.Z) ? p.Z - z : 0;
                double dx = p.X - entity.X!.Value, dy = p.Y - entity.Y!.Value;
                double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz), variance = 250000 + source.Error * source.Error;
                score += LogLikelihood(Math.Max(0, source.Band.Low - distance), variance) + LogLikelihood(Math.Max(0, distance - source.Band.High), variance);
            }
            likelihoods[i] = score; peak = Math.Max(peak, score);
        }
        double total = 0, squares = 0;
        for (int i = 0; i < Capacity; i++) { particles[i].Weight = Math.Exp(likelihoods[i] - peak); total += particles[i].Weight; }
        for (int i = 0; i < Capacity; i++) { particles[i].Weight /= total; squares += particles[i].Weight * particles[i].Weight; }
        if (1 / squares < Capacity / 2d) Resample();
        at = timestamp; Publish();
    }
}
