using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

public sealed record TrainingInfo(long Start, long End, int LimitSeconds, string Group, string Notes);

public sealed partial class CombatEngine
{
    Fight? training;
    Fight? trainingData, trainingPreview;
    int trainingSnapshotHits = -1;
    long trainingLastHit;
    long trainingClock;
    public Fight? TrainingSnapshot(long timestamp)
    {
        lock (Gate) {
            if (training?.Training is not { } info) return null;
            if (trainingData == null || trainingData.Id != training.Id || trainingSnapshotHits != training.Damage.Count) {
                trainingData = training.Copy(); trainingSnapshotHits = training.Damage.Count; trainingPreview = null;
                trainingLastHit = trainingData.Damage.Select(h => h.Timestamp).DefaultIfEmpty(info.Start).Max();
            }
            long end = Math.Max(info.Start, timestamp / 1000 * 1000);
            if (trainingData.Damage.Count > 0) end = Math.Max(end, trainingLastHit + 1);
            if (info.LimitSeconds > 0) end = Math.Min(end, info.Start + info.LimitSeconds * 1000L);
            if (trainingPreview?.Training?.End == end) return trainingPreview;
            // Clock-only refreshes share an immutable event snapshot. Idle seconds
            // update DPS without copying an unlimited attempt's growing history.
            return trainingPreview = new Fight { Id = trainingData.Id, Label = trainingData.Label, Created = trainingData.Created,
                LocalPlayer = trainingData.LocalPlayer, Demo = trainingData.Demo, Actors = trainingData.Actors,
                Damage = trainingData.Damage, Builds = trainingData.Builds, Training = info with { End = end } };
        }
    }
    public bool TrainingActive { get { lock (Gate) return training != null; } }
    public void StartTraining(long timestamp, int seconds, string name, string group, string notes)
    {
        lock (Gate) {
            if (training != null) throw new InvalidOperationException("Stop the current training attempt first.");
            if (Fight.LocalPlayer <= 0 || !Fight.Actors.TryGetValue(Fight.LocalPlayer, out var local))
                throw new InvalidOperationException("Identify your character by entering a zone while capturing before starting training.");
            if (timestamp < 0 || seconds != 0 && (seconds < 30 || seconds > 300)) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (string.IsNullOrWhiteSpace(name) || name.Length > 120 || group.Length > 120 || notes.Length > 4000)
                throw new ArgumentException("Use a name and group up to 120 characters, and notes up to 4,000 characters.");
            training = new Fight { Label = name.Trim(), LocalPlayer = local.Id,
                Training = new(timestamp, timestamp, seconds, group.Trim(), notes.Trim()), Demo = Fight.Demo };
            trainingClock = timestamp;
            training.Actors[local.Id] = local with { Party = false };
            if (Fight.Builds.TryGetValue(local.Id, out var build)) training.Builds[local.Id] = FightMetadata.Copy(build);
        }
    }
    public string TrainingStatus(long timestamp)
    {
        lock (Gate) {
            if (training?.Training is not { } info) return "Training…";
            long elapsed = Math.Max(0, timestamp - info.Start) / 1000;
            return info.LimitSeconds == 0 ? $"Training {MeterPresentation.Duration(elapsed)} · stop" :
                $"Training {MeterPresentation.Duration(Math.Max(0, info.LimitSeconds - elapsed))} left · stop";
        }
    }
    public void StopTraining(long timestamp)
    {
        lock (Gate) {
            if (training?.Training is not { } info) return;
            long end = Math.Max(info.Start, timestamp);
            if (info.LimitSeconds > 0) end = Math.Min(end, info.Start + info.LimitSeconds * 1000L);
            training.Training = info with { End = end };
            // A manually stopped attempt can exclude queued future-timestamp hits.
            training.Damage.RemoveAll(h => h.Timestamp >= end);
            completed.Enqueue(training); training = null; trainingData = null; trainingPreview = null; trainingSnapshotHits = -1;
        }
    }
    void AdvanceTraining(long timestamp)
    {
        if (training != null) trainingClock = Math.Max(trainingClock, timestamp);
        if (training?.Training is { LimitSeconds: > 0 } info && timestamp >= info.Start + info.LimitSeconds * 1000L)
            StopTraining(info.Start + info.LimitSeconds * 1000L);
    }
    static bool DifferentTrainingActor(Actor a, Actor b) =>
        a.Player && b.Player && a.Name != b.Name ||
        a.Server > 0 && b.Server > 0 && a.Server != b.Server ||
        a.CharacterId > 0 && b.CharacterId > 0 && a.CharacterId != b.CharacterId ||
        a.NpcId > 0 && b.NpcId > 0 && a.NpcId != b.NpcId ||
        a.Player && b.NpcId > 0 || a.NpcId > 0 && b.Player;
    void CheckTrainingIdentity()
    {
        if (training == null) return;
        var expected = training.Actors[training.LocalPlayer];
        var current = Fight.Actors.GetValueOrDefault(Fight.LocalPlayer);
        if (Fight.LocalPlayer != training.LocalPlayer || current == null || DifferentTrainingActor(expected, current)) {
            // Identity packets have no event timestamp. Use the latest capture/UI clock,
            // including the last accepted hit, rather than wall time during replay.
            StopTraining(trainingClock); return;
        }
        var updated = current with { Party = false };
        if (updated != expected) {
            training.Actors[training.LocalPlayer] = updated;
            trainingData = null; trainingPreview = null;
        }
    }
    void ObserveTraining(DamageEvent hit, bool healing)
    {
        if (training?.Training is not { } info || healing || hit.ActorId != training.LocalPlayer || hit.Timestamp < info.Start) return;
        if (info.LimitSeconds > 0 && hit.Timestamp >= info.Start + info.LimitSeconds * 1000L) return;
        CheckTrainingIdentity();
        if (training == null) return;
        trainingClock = Math.Max(trainingClock, hit.Timestamp + 1);
        if (Fight.Actors.TryGetValue(hit.TargetId, out var target)) {
            if (training.Actors.TryGetValue(target.Id, out var previous) && DifferentTrainingActor(previous, target)) {
                int archived = int.MaxValue;
                while (training.Actors.ContainsKey(archived) || Fight.Actors.ContainsKey(archived)) archived--;
                training.Actors[archived] = previous with { Id = archived, OriginalEntityId = target.Id, Party = false };
                for (int i = 0; i < training.Damage.Count; i++) {
                    var old = training.Damage[i];
                    if (old.TargetId == target.Id) training.Damage[i] = old with { TargetId = archived };
                }
            }
            training.Actors[hit.TargetId] = target with { Party = false };
        }
        training.Damage.Add(hit);
    }
}
