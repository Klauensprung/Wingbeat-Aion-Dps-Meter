namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    ControlOverlayWindow? controlOverlay;
    void ConfigureControlAlerts(bool enabled, bool preview)
    {
        preferences.ControlAlertsEnabled = enabled;
        if (!enabled) { controlOverlay?.Close(); SavePreferences(); return; }
        EnsureControlOverlay();
        if (preview) controlOverlay!.Preview();
        SavePreferences(); RefreshCooldownVisibility();
    }
    void EnsureControlOverlay()
    {
        if (controlOverlay != null) return;
        var window = new ControlOverlayWindow(preferences, SavePreferences); controlOverlay = window;
        window.Closed += (_, _) => {
            if (ReferenceEquals(controlOverlay, window)) controlOverlay = null;
            if (!shuttingDown) { preferences.ControlAlertsEnabled = false; SavePreferences(); }
        };
    }
    void RefreshControlOverlay(bool allowed)
    {
        if (!preferences.ControlAlertsEnabled) return;
        EnsureControlOverlay();
        var effects = capture == null ? [] : ControlEffectPresentation.Build(liveEffects.Active(Environment.TickCount64), EffectCatalogue.Default.Find);
        bool show = ControlEffectPresentation.ShouldShow(true, allowed, capture != null, effects.Length, controlOverlay!.PreviewActive);
        if (show) { controlOverlay.Update(effects); if (!controlOverlay.IsVisible) controlOverlay.Show(); }
        else if (controlOverlay.IsVisible) controlOverlay.Hide();
    }
}
