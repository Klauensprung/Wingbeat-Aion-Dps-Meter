namespace AionDpsMeter.Local;
public sealed partial class MainWindow
{
    RadarOverlayWindow? radarOverlay;
    string? radarMaterialKey;
    Dictionary<int,string> radarMaterials = new();
    void ToggleRadarOverlay()
    {
        if (radarOverlay != null) { radarOverlay.Close(); return; }
        trackedTargets ??= new(demoMode ? null : AppDataPaths.FilePath("farming-targets.json"));
        var window = new RadarOverlayWindow(preferences, SavePreferences); radarOverlay = window;
        var materials = new System.Windows.Controls.MenuItem { Header = Localization.T("Choose materials") };
        materials.Click += (_, _) => ShowRadarTracking();
        window.TrackingRequested += ShowRadarTracking; window.ContextMenu.Items.Insert(0, materials);
        preferences.SetupOverlayEnabled["Radar"] = true; SavePreferences();
        window.Closed += (_, _) => { if (ReferenceEquals(radarOverlay, window)) radarOverlay = null; if (!shuttingDown) { preferences.SetupOverlayEnabled["Radar"] = false; SavePreferences(); } };
        RefreshCooldownVisibility();
    }
    void RefreshRadarOverlay(bool allowed)
    {
        if (radarOverlay == null) return;
        if (!allowed) { if (radarOverlay.IsVisible) radarOverlay.Hide(); return; }
        var selected = trackedTargets?.Snapshot() ?? [];
        string key = preferences.RadarShowMaterials + ":" + preferences.RadarMaterialMode + ":" + string.Join(",",preferences.RadarMaterialObjects ?? []) + "|" + string.Join("|", selected.Select(t => t.Kind + ":" + t.Id));
        if (radarMaterialKey != key) {
            radarMaterialKey = key;
            var items = selected.Where(t => t.Kind == "item").Select(t => t.Id).ToHashSet();
            radarMaterials = RadarTracking.Materials(preferences, selected, MapMarkers.Default.Groups,
                ItemFarmLinks.Default.Items.Where(i => items.Contains(i.Id)).SelectMany(i => i.Gatherables));
        }
        RadarFrame frame = new(null, []);
        if (capture != null) lock (engine.Gate) {
            if (engine.LiveIdentityReady) frame = RadarSpace.Build(engine.Fight, engine.Spatial, engine.Position.Latest,
                engine.WorldObjects.Snapshot(), engine.WorldObjects.MapId, radarMaterials, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), preferences.RadarShowEnemies);
        }
        radarOverlay.Update(frame); if (!radarOverlay.IsVisible) radarOverlay.Show();
    }
}
