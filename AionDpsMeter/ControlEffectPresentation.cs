namespace AionDpsMeter.Local;

internal sealed record ControlEffectDisplay(string Name, double? Seconds)
{
    public string Text => Name + " · " + (Seconds is double seconds ? $"{Math.Ceiling(seconds * 10) / 10:0.0} s" : "duration unknown");
}
internal static class ControlEffectPresentation
{
    internal static ControlEffectDisplay[] Build(IEnumerable<LiveEffect> active, Func<int, EffectDefinition?> lookup) => active
        .Where(e => e.RemainingSeconds == null || double.IsFinite(e.RemainingSeconds.Value) && e.RemainingSeconds > 0)
        .Select(e => (Name: EffectCatalogue.ControlName(lookup(e.EffectId)), e.RemainingSeconds)).Where(e => e.Name != null)
        .GroupBy(e => e.Name!).Select(g => new ControlEffectDisplay(g.Key, g.Any(e => e.RemainingSeconds == null) ? null : g.Max(e => e.RemainingSeconds)))
        .OrderBy(e => e.Name).ToArray();
    internal static bool ShouldShow(bool enabled, bool allowedForeground, bool capturing, int effectCount, bool preview) =>
        enabled && allowedForeground && (preview || capturing && effectCount > 0);
}
