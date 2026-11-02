namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    PingOverlayWindow? pingOverlay;
    void TogglePingOverlay()
    {
        if (pingOverlay != null) { pingOverlay.Close(); return; }
        var window = new PingOverlayWindow(preferences, SavePreferences);
        pingOverlay = window;
        window.Closed += (_, _) => { if (ReferenceEquals(pingOverlay, window)) pingOverlay = null; };
        RefreshCooldownVisibility();
    }
    void RefreshPingOverlay(bool allowed)
    {
        if (pingOverlay == null) return;
        PingReading? sample;
        lock (engine.Gate) sample = engine.Fight.LocalPing;
        pingOverlay.Update(sample, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (allowed && !pingOverlay.IsVisible) pingOverlay.Show();
        else if (!allowed && pingOverlay.IsVisible) pingOverlay.Hide();
    }
}
