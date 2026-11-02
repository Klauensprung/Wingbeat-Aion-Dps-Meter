namespace AionDpsMeter.Local;

internal static class CooldownVisibility
{
    internal static bool ForegroundAllows(bool gameOnly, bool allowedForeground) => !gameOnly || allowedForeground;
    internal static bool ShouldShow(bool requested, bool combatOnly, bool capturing, bool combat, bool training) =>
        requested && (!combatOnly || capturing && (combat || training));
}

public sealed partial class MainWindow
{
    bool cooldownWanted, meterWanted;
    void RefreshCooldownVisibility()
    {
        bool foregroundAllowed=demoMode||(!(preferences.OverlaysGameOnly||refreshGatheringMapVisibility!=null||preferences.BossAlertsEnabled))||GameForeground.AllowsOverlays();
        refreshGatheringMapVisibility?.Invoke(foregroundAllowed);
        RefreshBossAlerts(foregroundAllowed);
        bool allowed = CooldownVisibility.ForegroundAllows(preferences.OverlaysGameOnly,foregroundAllowed);
        RefreshPingOverlay(allowed);
        RefreshLootOverlay(allowed);
        RefreshFarmingOverlay(allowed);
        RefreshCurrencyOverlay(allowed);
        RefreshTicketOverlay(allowed);
        RefreshPetTracker(allowed);
        RefreshControlOverlay(allowed);
        UpdateToolbarState();
        if (overlay != null) {
            if (meterWanted && allowed && !overlay.IsVisible) overlay.Show();
            else if ((!meterWanted || !allowed) && overlay.IsVisible) overlay.Hide();
        }
        if(secondMeter!=null){
            SyncSecondMeterAppearance();
            if(SecondMeterOptions.Enabled&&allowed&&!secondMeter.IsVisible)secondMeter.Show();
            else if((!SecondMeterOptions.Enabled||!allowed)&&secondMeter.IsVisible)secondMeter.Hide();
        }
        if (cooldownOverlay == null) return;
        bool show = CooldownVisibility.ShouldShow(cooldownWanted, preferences.CooldownCombatOnly, capture != null,
            engine.HasRecentPartyCombat(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), engine.TrainingActive) && allowed;
        if (show && !cooldownOverlay.IsVisible) cooldownOverlay.Show();
        else if (!show && cooldownOverlay.IsVisible) cooldownOverlay.Hide();
    }
}
