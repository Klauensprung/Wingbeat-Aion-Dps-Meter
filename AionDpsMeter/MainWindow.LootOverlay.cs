namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    LootOverlayWindow? lootOverlay;
    void ToggleLootOverlay()
    {
        preferences.LootOverlayEnabled=!preferences.LootOverlayEnabled;
        if(!preferences.LootOverlayEnabled)lootOverlay?.Close();
        SavePreferences();RefreshCooldownVisibility();
    }
    void EnsureLootOverlay()
    {
        if(lootOverlay!=null||!preferences.LootOverlayEnabled||shuttingDown)return;
        var window=new LootOverlayWindow(preferences,SavePreferences,()=>ShowLoot(liveOnly:true));lootOverlay=window;
        window.Closed+=(_,_)=>{
            if(ReferenceEquals(lootOverlay,window))lootOverlay=null;
            if(!shuttingDown){preferences.LootOverlayEnabled=false;SavePreferences();}
        };
    }
    void RefreshLootOverlay(bool allowed)
    {
        EnsureLootOverlay();
        if (lootOverlay == null) return;
        LootLedger.OverlayView snapshot;
        lock (engine.Gate) snapshot = engine.Loot.OverlaySnapshot();
        lootOverlay.Update(snapshot, capture != null);
        allowed=allowed&&lootOverlay.HasRecentItems&&OverlayHideControls.Allows(lootOverlay);
        if (allowed && !lootOverlay.IsVisible) lootOverlay.Show();
        else if (!allowed && lootOverlay.IsVisible) lootOverlay.Hide();
    }
}
