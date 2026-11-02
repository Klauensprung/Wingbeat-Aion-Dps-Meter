using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal sealed record EstimatedPosition(int MapId, double X, double Y, double? Z, long Timestamp, double Accuracy, bool Lost);

// Fixed-size probabilistic state. Estimates never replace CapturedPosition.
internal sealed partial class PositionEstimator
{
    internal const int Capacity = 400;
    struct Particle { internal double X, Y, Z, Vx, Vy, Vz, Weight, NeighborDistance; internal bool Still; }
    readonly Particle[] particles = new Particle[Capacity], scratch = new Particle[Capacity];
    readonly Random random;
    readonly Func<double>? comparisonNormal;
    readonly PositionSmoothing smoothing = new();
    readonly VisibilityRanges visibilityRanges = new();
    internal long VisibilityRevision => visibilityRanges.Revision;
    internal VisibilityMemory SnapshotVisibility() => visibilityRanges.Snapshot();
    internal void RestoreVisibility(VisibilityMemory memory) => visibilityRanges.Restore(memory);
    readonly Dictionary<int, (long At, bool Entering)> visibilityEdges = new();
    bool hasNormal;
    double spareNormal;
    int map;
    bool initialized;
    long at = -1;
    long visibilityScanAt = -1;
    long? flightPoints;
    long flightUntil = -1;
    bool networkEvent, networkMeasurement;
    internal void BeginNetworkEvent(long timestamp) { networkEvent = true; networkMeasurement = false; Advance(timestamp); }
    internal void EndNetworkEvent(long timestamp) {
        networkEvent = false;
        if (initialized && timestamp >= at && (networkMeasurement || Current == null || timestamp - Current.Timestamp >= 100)) Publish(networkMeasurement);
        networkMeasurement = false;
    }
    CapturedPosition? measured;
    SpatialConstraint? constraint;
    (long At, int Target, SkillAim Aim)? lastAim;
    (long At, int Target)? pendingCast;
    (long At, float Yaw)? facing;
    internal EstimatedPosition? Current { get; private set; }
    internal PositionMoments? Moments => initialized ? Summarize() : null;
    // On-demand diagnostic; not called by live rendering or ingestion.
    internal int DistinctParticleLocations() => initialized ? particles.Select(p => (p.X, p.Y)).Distinct().Count() : 0;
    internal PositionEstimator(int? seed = null) => random = seed is int value ? new(value) : new();
    internal PositionEstimator(Random source, Func<double>? normal = null) { random = source; comparisonNormal = normal; }
    internal void Clear(int mapId = 0) { map = mapId; initialized = false; at = -1; visibilityScanAt = -1; viewChanges = 0; flightPoints = null; flightUntil = -1; measured = null; constraint = null; lastAim = null; pendingCast = null; facing = null; Current = null; smoothing.Clear(); visibilityEdges.Clear(); }
    internal void ApplyFlight(NetworkEvent message, int localPlayer) {
        if (message.Kind != "resources" || localPlayer <= 0 || message.Timestamp < 0 || message.Timestamp < at || message.Fields.ValueKind != JsonValueKind.Object) return;
        var fields = message.Fields;
        if (!fields.TryGetProperty("EntityId", out var entity) || !entity.TryGetInt32(out int id) || id != localPlayer ||
            !fields.TryGetProperty("Values", out var values) || values.ValueKind != JsonValueKind.Object ||
            !values.TryGetProperty("6", out var fp) || !fp.TryGetInt64(out long points) || points < 0) return;
        Advance(message.Timestamp);
        if (flightPoints is long previous && points < previous) flightUntil = message.Timestamp + 1500;
        flightPoints = points;
    }
    internal void ApplyVisibility(NetworkEvent message, SpatialEvidence scene, CapturedPosition? measurement) {
        if (scene.MapId <= 0 || message.Timestamp < at || message.Fields.ValueKind != JsonValueKind.Object) return;
        ApplyVisibilityEdge(message, scene, measurement);
        if (visibilityScanAt < 0 || message.Timestamp - visibilityScanAt >= 500) {
            visibilityRanges.Classify(scene.MapId, scene.Entities, measurement, Moments, message.Timestamp, scene.Boundary);
            visibilityRanges.DetectEverywhere(scene.MapId, scene.Entities, measurement, message.Timestamp, scene.Boundary);
            UpdateVisibleScene(scene, message.Timestamp, visibilityScanAt < 0 ? message.Timestamp : message.Timestamp - visibilityScanAt);
            visibilityScanAt = message.Timestamp;
        }
    }
    void ApplyVisibilityEdge(NetworkEvent message, SpatialEvidence scene, CapturedPosition? measurement) {
        bool entering = message.Kind is "actor-position" or "world-object";
        if (!entering && message.Kind is not ("entity-removed" or "world-object-removed")) return;
        string idField = message.Kind == "actor-position" ? "ActorId" : "EntityId";
        if (!message.Fields.TryGetProperty(idField, out var field) || !field.TryGetInt32(out int id) || scene.Find(id) is not { } entity) return;
        if ((entering ? entity.EnteredViewAt : entity.LeftViewAt) != message.Timestamp) return;
        var edge = (message.Timestamp, entering);
        if (visibilityEdges.TryGetValue(id, out var previous) && (previous.At > message.Timestamp || previous == edge)) return;
        if (visibilityEdges.Count >= 4096 && !visibilityEdges.ContainsKey(id)) visibilityEdges.Remove(visibilityEdges.Keys.First());
        visibilityEdges[id] = edge;
        var range = visibilityRanges.Find(scene.MapId, entity);
        visibilityRanges.Learn(scene.MapId, entity, entering, measurement, message.Timestamp,
            entity.Kind == "object" && range == null && map == scene.MapId ? Moments : null);
        if (range != null && !entity.Moving && entity.Kind != "player" && visibilityRanges.NpcClass(entity.NpcId) == 0) viewChanges++;
        // Edges refine an existing cloud; periodic visible-entity proposals initialize it.
        if (!initialized || map != scene.MapId || range == null || entity.X is not double x || entity.Y is not double y) return;
        Advance(message.Timestamp); if (!initialized) return;
        var band = entering ? range.Enter : range.Leave;
        bool dense = entity.Kind != "object" && scene.Entities.Count(e => e.Visible && e.Kind != "object") >= 60;
        double movement = entity.Moving ? Math.Min(4000, Math.Max(0, message.Timestamp - entity.PositionAt) / 1000d * 600) : 0;
        double variance = 250000 + movement * movement, total = 0;
        for (int i = 0; i < Capacity; i++) {
            ref var p = ref particles[i];
            double dx = p.X - x, dy = p.Y - y, dz = entity.Z is double z && double.IsFinite(p.Z) ? p.Z - z : 0;
            if (dense) {
                double difference = double.Hypot(dx, dy) - p.NeighborDistance;
                double spread = .09 * p.NeighborDistance * p.NeighborDistance + movement * movement;
                p.Weight *= difference == 0 ? 1 : spread > 0 ? .05 + .95 * Math.Exp(-.5 * difference * difference / spread) : .05;
            } else {
                double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                double below = Math.Max(0, band.Low - distance), above = Math.Max(0, distance - band.High);
                p.Weight *= (.05 + .95 * Math.Exp(-.5 * below * below / variance)) * (.05 + .95 * Math.Exp(-.5 * above * above / variance));
            }
            total += p.Weight;
        }
        double squares = 0;
        for (int i = 0; i < Capacity; i++) { particles[i].Weight /= total; squares += particles[i].Weight * particles[i].Weight; }
        if (1 / squares < Capacity / 2d) Resample();
        at = message.Timestamp; Publish();
    }
    internal void ApplyCast(NetworkEvent message, int localPlayer, SpatialEvidence scene) {
        if (localPlayer <= 0 || scene.MapId <= 0 || message.Timestamp < scene.Boundary || message.Timestamp < at) return;
        int actor, target; SkillAim? aim;
        if (message.Kind == "skill-use") {
            var cast = message.Fields.Deserialize<SkillStartReading>(); if (cast == null) return;
            actor = cast.ActorId; target = cast.TargetId; aim = cast.Aim;
        } else if (message.Kind is "cast-start" or "charge-start") {
            var cast = message.Fields.Deserialize<CastReading>(); if (cast == null) return;
            actor = cast.ActorId; target = cast.TargetId; aim = cast.Aim;
        } else if (message.Kind is "actor-position" or "local-zone") {
            var fields = message.Fields;
            if (fields.ValueKind != JsonValueKind.Object || !fields.TryGetProperty("Direction", out var direction) ||
                direction.ValueKind != JsonValueKind.Number || !direction.TryGetSingle(out float yaw) || !float.IsFinite(yaw)) return;
            if (message.Kind == "actor-position" && (!fields.TryGetProperty("ActorId", out var id) || !id.TryGetInt32(out int value) || value != localPlayer)) return;
            facing = (message.Timestamp, yaw);
            if (pendingCast is not { } pending || pending.At != message.Timestamp) return;
            actor = localPlayer; target = pending.Target; aim = new(yaw, null, null, null);
        } else return;
        if (actor != localPlayer) return;
        if (target > 0 && target != localPlayer) pendingCast = (message.Timestamp, target);
        if (aim == null && facing is { } prior && prior.At == message.Timestamp) aim = new(prior.Yaw, null, null, null);
        if (aim == null || !float.IsFinite(aim.Yaw)) return;
        facing = (message.Timestamp, aim.Yaw);
        pendingCast = null;
        var key = (message.Timestamp, target, aim);
        if (lastAim == key) return;
        SpatialEntity? entity;
        if (aim.X is float x && aim.Y is float y && (x != 0 || y != 0)) {
            if (!float.IsFinite(x) || !float.IsFinite(y) || Math.Abs(x) > 10000000 || Math.Abs(y) > 10000000) return;
            entity = new(target, 0, 0, 0, message.Timestamp, x, y, PositionAt: message.Timestamp);
        } else {
            if (target == localPlayer || scene.Find(target) is not { Visible: true, X: not null, Y: not null } located ||
                located.PositionAt < 0 || located.At > message.Timestamp || located.PositionAt > message.Timestamp) return;
            entity = located;
        }
        double tx = entity.X!.Value, ty = entity.Y!.Value;
        if (map == 0) map = scene.MapId;
        if (map != scene.MapId) return;
        Advance(message.Timestamp);
        double angle = aim.Yaw * Math.PI / 180, ux = Math.Cos(angle), uy = Math.Sin(angle);
        if (!initialized) {
            for (int i = 0; i < Capacity; i++) {
                double distance = 400 + random.NextDouble() * 3100;
                particles[i] = new() { X = tx - ux * distance + Normal() * 200, Y = ty - uy * distance + Normal() * 200,
                    Z = double.NaN, Weight = 1d / Capacity, Still = random.NextDouble() < .5 };
            }
            initialized = true;
        }
        double movement = entity.Moving ? Math.Min(4000, (message.Timestamp - entity.PositionAt) / 1000d * 600) : 0;
        double sideVariance = 40000 + movement * movement, rangeVariance = 250000 + movement * movement;
        static double Likelihood(double distance, double variance) => .05 + .95 * Math.Exp(-.5 * distance * distance / variance);
        double total = 0;
        for (int i = 0; i < Capacity; i++) {
            ref var p = ref particles[i]; double dx = tx - p.X, dy = ty - p.Y, forward = dx * ux + dy * uy;
            p.Weight *= Likelihood(dx * uy - dy * ux, sideVariance) * Likelihood(Math.Max(0, 400 - forward), rangeVariance) * Likelihood(Math.Max(0, forward - 3500), rangeVariance);
            total += p.Weight;
        }
        double squares = 0;
        for (int i = 0; i < Capacity; i++) { particles[i].Weight /= total; squares += particles[i].Weight * particles[i].Weight; }
        if (1 / squares < Capacity / 2d) Resample();
        lastAim = key; at = message.Timestamp; Publish();
    }
    double Normal() {
        if (comparisonNormal != null) return comparisonNormal();
        if (hasNormal) { hasNormal = false; return spareNormal; }
        double x, y, radius;
        do { x = 2 * random.NextDouble() - 1; y = 2 * random.NextDouble() - 1; radius = x * x + y * y; } while (radius is <= 0 or >= 1);
        double scale = Math.Sqrt(-2 * Math.Log(radius) / radius);
        spareNormal = y * scale; hasNormal = true; return x * scale;
    }
    internal void Observe(CapturedPosition position) {
        LocalPositionTracker.Validate(position);
        if (map == 0) map = position.MapId;
        if (map != position.MapId) return;
        if (position.Timestamp < at || position == measured) return;
        Advance(position.Timestamp);
        double vx = 0, vy = 0, vz = 0;
        bool velocityKnown = false;
        if (measured is { } previous && position.Timestamp > previous.Timestamp && position.Timestamp - previous.Timestamp <= 5000) {
            double seconds = (position.Timestamp - previous.Timestamp) / 1000d;
            vx = (position.X - previous.X) / seconds; vy = (position.Y - previous.Y) / seconds; vz = (position.Z - previous.Z) / seconds;
            double speed = Math.Sqrt(vx * vx + vy * vy); if (speed > 1600) { vx *= 1600 / speed; vy *= 1600 / speed; }
            velocityKnown = true;
        }
        double stillProbability = velocityKnown ? vx * vx + vy * vy < 10000 ? 1 : .125 : .5;
        for (int i = 0; i < Capacity; i++) particles[i] = new() { X = position.X + Normal() * 50, Y = position.Y + Normal() * 50,
            Z = position.Z, Vx = velocityKnown ? vx + Normal() * 225 : 0, Vy = velocityKnown ? vy + Normal() * 225 : 0, Vz = vz,
            Weight = 1d / Capacity, Still = random.NextDouble() < stillProbability, NeighborDistance = particles[i].NeighborDistance };
        measured = position; at = position.Timestamp; initialized = true; Publish(measurement: true);
    }
    internal void Constrain(SpatialConstraint value) {
        if (value.MapId <= 0 || value.Timestamp < 0 || value.PositionAt < 0 || value.PositionAt > value.Timestamp || value.Radius <= 0 ||
            !double.IsFinite(value.X) || !double.IsFinite(value.Y) || Math.Abs(value.X) > 10000000 || Math.Abs(value.Y) > 10000000) return;
        if (map == 0) map = value.MapId;
        if (map != value.MapId) return;
        if (value.Timestamp < at || value == constraint) return;
        Advance(value.Timestamp);
        if (!initialized) {
            for (int i = 0; i < Capacity; i++) {
                double angle = random.NextDouble() * 2 * Math.PI, distance = Math.Sqrt(random.NextDouble()) * value.Radius;
                particles[i] = new() { X = value.X + Math.Cos(angle) * distance, Y = value.Y + Math.Sin(angle) * distance,
                    Z = double.NaN, Weight = 1d / Capacity, Still = random.NextDouble() < .5 };
            }
            initialized = true;
        }
        double movement = value.Moving ? Math.Min(4000, (value.Timestamp - value.PositionAt) / 1000d * 600) : 0;
        double softness = value.Radius * .3;
        double likelihoodScale = -.5 / (softness * softness + movement * movement);
        double total = 0;
        for (int i = 0; i < Capacity; i++) {
            ref var p = ref particles[i]; double dx = p.X - value.X, dy = p.Y - value.Y;
            double outside = Math.Max(0, Math.Sqrt(dx * dx + dy * dy) - value.Radius);
            if (outside > 0) p.Weight *= .05 + .95 * Math.Exp(likelihoodScale * outside * outside);
            total += p.Weight;
        }
        double squares = 0;
        for (int i = 0; i < Capacity; i++) { particles[i].Weight /= total; squares += particles[i].Weight * particles[i].Weight; }
        if (1 / squares < Capacity / 2d) Resample();
        constraint = value; at = value.Timestamp; Publish();
    }
    void Resample() {
        double offset = random.NextDouble() / Capacity, cumulative = particles[0].Weight; int selected = 0;
        for (int i = 0; i < Capacity; i++) {
            double threshold = offset + i / (double)Capacity;
            while (selected < Capacity - 1 && threshold > cumulative) cumulative += particles[++selected].Weight;
            scratch[i] = particles[selected]; scratch[i].Weight = 1d / Capacity;
            // Rejuvenate duplicated samples horizontally; retain measured height and motion.
            scratch[i].X += Normal() * 40; scratch[i].Y += Normal() * 40;
        }
        Array.Copy(scratch, particles, Capacity);
    }
    internal void Advance(long timestamp) {
        if (timestamp < 0 || timestamp <= at) return;
        if (!initialized) { at = timestamp; return; }
        if (timestamp - at > 300000) {
            initialized = false; at = timestamp;
            if (Current != null) Current = Current with { Lost = true };
            return;
        }
        while (at < timestamp) {
            double seconds = Math.Min(1000, timestamp - at) / 1000d;
            double decay = Math.Exp(-seconds / 4), noise = Math.Sqrt(1 - decay * decay);
            double beginMoving = 1 - Math.Exp(-seconds / 10), stopMoving = 1 - Math.Exp(-seconds / 20);
            double horizontalNoise = 450 * noise, verticalNoise = (at + (long)Math.Round(seconds * 1000) <= flightUntil ? 300 : 150) * noise;
            for (int i = 0; i < Capacity; i++) {
                ref var p = ref particles[i];
                if (random.NextDouble() < (p.Still ? beginMoving : stopMoving)) {
                    p.Still = !p.Still;
                    if (p.Still) p.Vx = p.Vy = p.Vz = 0;
                }
                if (p.Still) continue;
                double vx = p.Vx * decay + Normal() * horizontalNoise, vy = p.Vy * decay + Normal() * horizontalNoise;
                double speedSquared = vx * vx + vy * vy;
                if (speedSquared > 2560000) { double scale = 1600 / Math.Sqrt(speedSquared); vx *= scale; vy *= scale; }
                p.X += (p.Vx + vx) * .5 * seconds; p.Y += (p.Vy + vy) * .5 * seconds; p.Vx = vx; p.Vy = vy;
                double vz = p.Vz * decay + Normal() * verticalNoise;
                p.Z += (p.Vz + vz) * .5 * seconds; p.Vz = vz;
            }
            at += (long)Math.Round(seconds * 1000);
        }
        Publish();
    }
    PositionMoments Summarize() {
        double x = 0, y = 0, z = 0, zWeight = 0, vx = 0, vy = 0, still = 0;
        foreach (var p in particles) { x += p.X * p.Weight; y += p.Y * p.Weight; vx += p.Vx * p.Weight; vy += p.Vy * p.Weight; if (p.Still) still += p.Weight; if (double.IsFinite(p.Z)) { z += p.Z * p.Weight; zWeight += p.Weight; } }
        double variance = 0;
        foreach (var p in particles) variance += ((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y)) * p.Weight;
        return new(x, y, zWeight > 0 ? z / zWeight : null, vx, vy, Math.Sqrt(variance), still);
    }
    void Publish(bool measurement = false) {
        if (networkEvent) { networkMeasurement |= measurement; return; }
        Current = smoothing.Update(map, at, Summarize(), measurement);
    }
}
