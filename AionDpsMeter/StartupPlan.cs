namespace AionDpsMeter.Local;

internal sealed record StartupPlan(bool StartCapture, bool OpenMeter, bool OpenCooldowns, string? SetupMessage)
{
    internal string? CaptureAdapter { get; init; }
    internal static StartupPlan Create(Preferences settings, IEnumerable<string> adapters, bool driverMissing, bool demo = false, AdapterDetection? detection = null)
    {
        if (demo || !settings.AutoStartConfigured) return new(false, false, false, null);
        bool automatic = GameAdapterDetector.Automatic(settings);
        string? selected = automatic ? detection is { Status: AdapterDetectionStatus.Found } ? detection.Adapter : null : settings.Adapter;
        string? message = driverMissing ? "Install Npcap, then select your game adapter to enable automatic startup."
            : automatic && string.IsNullOrWhiteSpace(selected) ? detection?.Message ?? "Connect Aion to a server, then use Detect game adapter or Start capture."
            : string.IsNullOrWhiteSpace(selected) ? "Choose your game adapter and start capture once. Wingbeat will remember your setup."
            : !adapters.Contains(selected, StringComparer.OrdinalIgnoreCase) ? "Your capture adapter is unavailable. Select an adapter or retry detection."
            : string.IsNullOrWhiteSpace(settings.Filter) ? "Set a capture filter in Advanced capture, then start capture again."
            : null;
        bool positioned = settings.OverlayLeft is double x && double.IsFinite(x) && settings.OverlayTop is double y && double.IsFinite(y);
        return new(message == null, settings.OpenMeterOnStartup && positioned,
            settings.OpenCooldownsOnStartup && (settings.CooldownSkills?.Any(id => id > 0) ?? false), message) { CaptureAdapter = message == null ? selected : null };
    }
}
