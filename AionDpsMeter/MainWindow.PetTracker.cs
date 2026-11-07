namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    PetTrackerOverlayWindow? petTrackerOverlay;
    System.Windows.Controls.Button? petFarmingButton;
    PetMapOverlayWindow? petMapOverlay;
    System.Windows.Controls.Button? petMapButton;
    PetNearbyTracker? petNearbyTracker;
    readonly PetNearbyTracker petMapNearbyTracker = new();
    long petGatheringRevision = -1;
    void TogglePetMap()
    {
        if(petMapOverlay!=null||preferences.SetupOverlayEnabled.GetValueOrDefault("PetMap")){
            preferences.SetupOverlayEnabled["PetMap"]=false;petMapOverlay?.Close();
        }else OpenPetMap();
        SavePreferences();UpdateToolbarState();
    }
    void OpenPetMap()
    {
        preferences.SetupOverlayEnabled["PetMap"]=true;
        if(petMapOverlay!=null)return;
        petNearbyTracker ??= new();
        var window = new PetMapOverlayWindow(preferences, SavePreferences); petMapOverlay = window;
        window.Closed += (_, _) => {
            if (ReferenceEquals(petMapOverlay, window)) { petMapOverlay = null; if (petTrackerOverlay == null) petNearbyTracker = null; }
            if(!shuttingDown){preferences.SetupOverlayEnabled["PetMap"]=false;SavePreferences();UpdateToolbarState();}
        };
        RefreshCooldownVisibility();
    }
    void TogglePetTracker()
    {
        preferences.PetGatheringAutoShow=!preferences.PetGatheringAutoShow;
        preferences.SetupOverlayEnabled["PetTracker"]=preferences.PetGatheringAutoShow;
        if(preferences.PetGatheringAutoShow)OpenPetTracker();else petTrackerOverlay?.Close();
        SavePreferences();UpdateToolbarState();
    }
    void OpenPetTracker()
    {
        if(!preferences.PetGatheringAutoShow||petTrackerOverlay!=null)return;
        petNearbyTracker ??= new();
        var window = new PetTrackerOverlayWindow(preferences, SavePreferences, id => {
            CapturedPosition? position; lock (engine.Gate) position = engine.Position.Latest;
            ShowPetSpawns(id, this, null, LocalPositionTracker.MapKey(position?.MapId ?? 0));
        }, () => ShowNearbyPets(this, null, preferences.PetTrackerGoal));
        petTrackerOverlay = window;
        window.Closed += (_, _) => {
            if (ReferenceEquals(petTrackerOverlay, window)) { petTrackerOverlay = null; if (petMapOverlay == null) petNearbyTracker = null; }
            if(!shuttingDown){preferences.PetGatheringAutoShow=false;preferences.SetupOverlayEnabled["PetTracker"]=false;SavePreferences();UpdateToolbarState();}
        };
        RefreshCooldownVisibility();
    }
    void RefreshPetTracker(bool allowed)
    {
        if(!preferences.PetGatheringAutoShow)petTrackerOverlay?.Close();
        PetGatheringRow[] collected; long collectionRevision;
        lock (engine.Gate) { collected = engine.PetGathering.Rows(engine.CurrentPets()); collectionRevision = engine.PetGathering.Revision; }
        bool collectedNow = collectionRevision != petGatheringRevision && collected.Length > 0;
        petGatheringRevision = collectionRevision;
        if (capture != null && collectedNow && preferences.PetGatheringAutoShow && petTrackerOverlay == null) OpenPetTracker();
        if ((petTrackerOverlay == null && petMapOverlay == null) || petNearbyTracker == null) return;
        CapturedPosition? position; PetCollectionSnapshot? collection; EstimatedPosition? prediction;
        lock (engine.Gate) { position = engine.Position.Latest; collection = engine.CurrentPets(); prediction = engine.PositionPrediction.Current; }
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (position?.Timestamp > now) position = null;
        var rows = petNearbyTracker.Update(position, collection, preferences.PetTrackerGoal);
        if (petTrackerOverlay != null) {
            petTrackerOverlay.UpdateCollected(collected);
            petTrackerOverlay.Update(rows, position, now, capture != null, preferences.PetTrackerGoal);
            bool trackerAllowed=allowed&&petTrackerOverlay.HasPets&&OverlayHideControls.Allows(petTrackerOverlay);
            if (trackerAllowed && !petTrackerOverlay.IsVisible) petTrackerOverlay.Show();
            else if (!trackerAllowed && petTrackerOverlay.IsVisible) petTrackerOverlay.Hide();
        }
        if (petMapOverlay != null) {
            var presentation = MapPositionPresentation.Select(position, prediction, preferences.PetMapPredictPosition, now, capture != null);
            var mapRows = petMapNearbyTracker.Update(presentation.Position, collection, preferences.PetTrackerGoal);
            petMapOverlay.Update(mapRows, presentation.Position, now, capture != null, preferences.PetTrackerGoal, presentation);
            bool mapAllowed=allowed&&OverlayHideControls.Allows(petMapOverlay);
            if (mapAllowed && !petMapOverlay.IsVisible) petMapOverlay.Show();
            else if (!mapAllowed && petMapOverlay.IsVisible) petMapOverlay.Hide();
        }
    }
}
