namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    LootOverlayWindow? lootOverlay;
    void ToggleLootOverlay()
    {
        if (lootOverlay != null) { lootOverlay.Close(); return; }
        var window = new LootOverlayWindow(preferences, SavePreferences, () => ShowLoot(liveOnly: true));
        lootOverlay = window;
        window.Closed += (_, _) => { if (ReferenceEquals(lootOverlay, window)) lootOverlay = null; };
        RefreshCooldownVisibility();
    }
    void RefreshLootOverlay(bool allowed)
    {
        if (lootOverlay == null) return;
        LootLedger.OverlayView snapshot;
        lock (engine.Gate) snapshot = engine.Loot.OverlaySnapshot();
        lootOverlay.Update(snapshot, capture != null);
        if (allowed && !lootOverlay.IsVisible) lootOverlay.Show();
        else if (!allowed && lootOverlay.IsVisible) lootOverlay.Hide();
    }
}
