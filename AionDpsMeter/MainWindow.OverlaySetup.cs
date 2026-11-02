namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    OverlayPlacementSetupWindow? placementSetup;

    void ShowOverlayPlacementSetup()
    {
        if (placementSetup != null) { placementSetup.Activate(); return; }
        bool meterLocked = preferences.OverlayLocked, alertsLocked = preferences.BossAlertsLocked;
        bool alertsEnabled = preferences.BossAlertsEnabled;
        bool completed = false;
        preferences.OverlayLocked = preferences.BossAlertsLocked = false;
        preferences.BossAlertsEnabled = true;
        OpenOverlay(); EnsureBossAlerts(); bossAlertOverlay!.Preview();
        var window = new OverlayPlacementSetupWindow(preferences, () => {
            // Save default placements too, even if the user did not drag either window.
            if (overlay == null || bossAlertOverlay == null) {
                OpenOverlay(); EnsureBossAlerts(); bossAlertOverlay!.Preview(); return;
            }
            preferences.OverlayLeft = overlay.Left; preferences.OverlayTop = overlay.Top;
            preferences.OverlayWidth = overlay.Width; preferences.OverlayHeight = overlay.Height;
            preferences.BossAlertsLeft = bossAlertOverlay.Left; preferences.BossAlertsTop = bossAlertOverlay.Top;
            preferences.OpenMeterOnStartup = true;
            preferences.BossAlertsEnabled = true;
            preferences.OverlayPlacementSetupCompleted = completed = true;
            placementSetup!.Close();
        }, () => placementSetup!.Close()) { Owner = this };
        placementSetup = window;
        window.Closed += (_,_) => {
            placementSetup = null;
            preferences.OverlayLocked = meterLocked; preferences.BossAlertsLocked = alertsLocked;
            bossAlertOverlay?.EndPreview();
            if (!completed) preferences.BossAlertsEnabled = alertsEnabled;
            if (!preferences.BossAlertsEnabled) bossAlertOverlay?.Hide();
            SavePreferences();
            if (!shuttingDown) { Refresh(); RefreshCooldownVisibility(); }
        };
        window.Show(); RefreshCooldownVisibility();
    }
}
