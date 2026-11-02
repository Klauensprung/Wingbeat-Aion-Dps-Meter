namespace AionDpsMeter.Local;

internal sealed record MapPositionPresentation(CapturedPosition? Position, EstimatedPosition? Estimate, string Status)
{
    internal static MapPositionPresentation Select(CapturedPosition? captured, EstimatedPosition? estimate, bool predict, long now, bool capturing) {
        if (captured?.Timestamp > now) captured = null;
        bool usable = predict && capturing && estimate is { Lost: false, MapId: > 0, Accuracy: >= 0, Timestamp: >= 0 } && estimate.Timestamp <= now && now - estimate.Timestamp <= 300000 &&
            double.IsFinite(estimate.X) && double.IsFinite(estimate.Y) && double.IsFinite(estimate.Accuracy) &&
            (estimate.Z == null || double.IsFinite(estimate.Z.Value) && Math.Abs(estimate.Z.Value) <= 10000000) &&
            Math.Abs(estimate.X) <= 10000000 && Math.Abs(estimate.Y) <= 10000000 && (captured == null || captured.MapId == estimate.MapId);
        if (usable) {
            // View-only adapter for existing XY map/ranking APIs. Never stored on a fight or fed into capture state.
            var point = new CapturedPosition(estimate!.MapId, estimate.X, estimate.Y, estimate.Z ?? 0, estimate.Timestamp);
            return new(point, estimate, $"Estimated position · uncertainty {estimate.Accuracy:N0} world units");
        }
        string prefix = predict && capturing ? "Estimate unavailable · " : "";
        return new(captured, null, captured == null ? prefix + "Waiting for position." : prefix + $"Captured position · {Math.Max(0, now - captured.Timestamp) / 1000:N0}s ago");
    }
}
