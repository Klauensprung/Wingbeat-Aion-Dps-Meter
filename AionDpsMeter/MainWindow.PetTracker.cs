namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    PetTrackerOverlayWindow? petTrackerOverlay;
    PetMapOverlayWindow? petMapOverlay;
    PetNearbyTracker? petNearbyTracker;
    readonly PetNearbyTracker petMapNearbyTracker = new();
    void TogglePetMap()
    {
        if (petMapOverlay != null) { petMapOverlay.Close(); return; }
        petNearbyTracker ??= new();
        var window = new PetMapOverlayWindow(preferences, SavePreferences); petMapOverlay = window;
        window.Closed += (_, _) => {
            if (ReferenceEquals(petMapOverlay, window)) { petMapOverlay = null; if (petTrackerOverlay == null) petNearbyTracker = null; }
        };
        RefreshCooldownVisibility();
    }
    void TogglePetTracker()
    {
        if (petTrackerOverlay != null) { petTrackerOverlay.Close(); return; }
        petNearbyTracker ??= new();
        var window = new PetTrackerOverlayWindow(preferences, SavePreferences, id => {
            CapturedPosition? position; lock (engine.Gate) position = engine.Position.Latest;
            ShowPetSpawns(id, this, null, LocalPositionTracker.MapKey(position?.MapId ?? 0));
        }, () => ShowNearbyPets(this, null, preferences.PetTrackerGoal));
        petTrackerOverlay = window;
        window.Closed += (_, _) => { if (ReferenceEquals(petTrackerOverlay, window)) { petTrackerOverlay = null; if (petMapOverlay == null) petNearbyTracker = null; } };
        RefreshCooldownVisibility();
    }
    void RefreshPetTracker(bool allowed)
    {
        if ((petTrackerOverlay == null && petMapOverlay == null) || petNearbyTracker == null) return;
        CapturedPosition? position; PetCollectionSnapshot? collection; EstimatedPosition? prediction;
        lock (engine.Gate) { position = engine.Position.Latest; collection = engine.CurrentPets(); prediction = engine.PositionPrediction.Current; }
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (position?.Timestamp > now) position = null;
        var rows = petNearbyTracker.Update(position, collection, preferences.PetTrackerGoal);
        if (petTrackerOverlay != null) {
            petTrackerOverlay.Update(rows, position, now, capture != null, preferences.PetTrackerGoal);
            if (allowed && !petTrackerOverlay.IsVisible) petTrackerOverlay.Show();
            else if (!allowed && petTrackerOverlay.IsVisible) petTrackerOverlay.Hide();
        }
        if (petMapOverlay != null) {
            var presentation = MapPositionPresentation.Select(position, prediction, preferences.PetMapPredictPosition, now, capture != null);
            var mapRows = petMapNearbyTracker.Update(presentation.Position, collection, preferences.PetTrackerGoal);
            petMapOverlay.Update(mapRows, presentation.Position, now, capture != null, preferences.PetTrackerGoal, presentation);
            if (allowed && !petMapOverlay.IsVisible) petMapOverlay.Show();
            else if (!allowed && petMapOverlay.IsVisible) petMapOverlay.Hide();
        }
    }
}
