namespace AionDpsMeter.Local;

internal static class PingPresentationTests
{
    internal static void Run(Action<bool, string> check)
    {
        var sample = new PingReading(31, 1000, "Synthetic clock echo estimate");
        var fresh = PingPresentation.Build(sample, 1000);
        check(fresh.Bars == 4 && fresh.Text.Contains("~31 ms") && fresh.Detail.Contains("estimate"),
            "Ping bars retain the estimate marker instead of asserting a measured round trip");
        check(PingPresentation.Build(sample, 15999).Bars == 4 && PingPresentation.Build(sample, 16000).Bars == 0 &&
            PingPresentation.Build(sample, 16000).Text.Contains("stale") && PingPresentation.Build(sample, 999).Bars == 0,
            "Ping expires at fifteen seconds and future timestamps never appear current");
        check(new[] { 59, 60, 99, 100, 199, 200, 1000 }.Select(ms => PingPresentation.Build(sample with { Milliseconds = ms }, 1000).Bars)
            .SequenceEqual(new[] { 4, 3, 3, 2, 2, 1, 1 }), "Ping quality bands switch exactly at documented boundaries");
        check(PingPresentation.Build(null, 1000).Bars == 0 && PingPresentation.Build(sample with { Milliseconds = -1 }, 1000).Text == "Ping --" &&
            PingPresentation.Build(sample with { Milliseconds = 1001 }, 1000).Text == "Ping --",
            "Missing and invalid ping samples show unknown rather than a healthy connection");
    }
}
