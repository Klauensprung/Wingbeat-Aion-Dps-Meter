namespace AionDpsMeter.Local;

internal static class MapPositionPresentationTests
{
    internal static void Run(Action<bool, string> check) {
        var captured = new CapturedPosition(1010, 100, 200, 10, 1000);
        var estimate = new EstimatedPosition(1010, 300, 400, null, 2000, 500, false);
        var display = MapPositionPresentation.Select(captured, estimate, true, 2500, true);
        check(display.Position is { X: 300, Y: 400 } && display.Estimate == estimate && display.Status.StartsWith("Estimated position") && captured.X == 100,
            "Map projection uses estimated XY with explicit uncertainty while captured coordinates remain unchanged");
        check(MapPositionPresentation.Select(captured, estimate, false, 2500, true).Position == captured &&
            MapPositionPresentation.Select(captured, estimate, true, 2500, false).Position == captured,
            "Disabling prediction or stopping capture restores the captured map position");
        check(new[] { estimate with { Lost = true }, estimate with { MapId = 1110 }, estimate with { Timestamp = 3000 },
            estimate with { Accuracy = double.NaN }, estimate with { Z = double.PositiveInfinity } }
            .All(e => MapPositionPresentation.Select(captured, e, true, 2500, true) is { Estimate: null, Position: var p } && p == captured),
            "Lost, wrong-map, future and invalid estimates cannot displace the captured map marker");
        check(MapPositionPresentation.Select(null, estimate, true, 2500, true).Position != null &&
            MapPositionPresentation.Select(null, null, true, 2500, true).Position == null,
            "A valid estimate can supply a map marker without captured coordinates, while missing evidence stays empty");
        check(MapPositionPresentation.Select(captured, estimate, true, 302001, true).Estimate == null,
            "The map falls back from an old displayed estimate without advancing the network filter clock");
    }
}
