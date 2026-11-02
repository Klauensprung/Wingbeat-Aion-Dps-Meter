namespace AionDpsMeter.Local;

public sealed partial class CombatEngine
{
    readonly object visibilitySaveGate = new();
    string? visibilityMemoryPath;
    long savedVisibilityRevision;
    void InitializeVisibilityMemory(string? path) {
        visibilityMemoryPath = path;
        if (path != null && VisibilityMemoryStore.Load(path) is { } memory) PositionPrediction.RestoreVisibility(memory);
        savedVisibilityRevision = PositionPrediction.VisibilityRevision;
    }
    internal void SaveVisibilityMemory() {
        if (visibilityMemoryPath == null) return;
        lock (visibilitySaveGate) {
            VisibilityMemory snapshot; long revision;
            lock (Gate) {
                revision = PositionPrediction.VisibilityRevision; if (revision == savedVisibilityRevision) return;
                snapshot = PositionPrediction.SnapshotVisibility();
            }
            VisibilityMemoryStore.Save(visibilityMemoryPath, snapshot);
            savedVisibilityRevision = revision;
        }
    }
}
