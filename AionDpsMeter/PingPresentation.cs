namespace AionDpsMeter.Local;

internal sealed record PingDisplay(string Text, int Bars, string Color, string Detail);
internal static class PingPresentation
{
    internal static PingDisplay Build(PingReading? sample, long now)
    {
        if (sample == null || sample.Milliseconds is < 0 or > 1000 || sample.Timestamp < 0)
            return new("Ping --", 0, "#93A2B8", "No local client-clock echo estimate captured.");
        if (now < sample.Timestamp || now - sample.Timestamp >= 15000)
            return new($"Ping ~{sample.Milliseconds} ms (stale)", 0, "#93A2B8", "Last captured estimate is not current. Waiting for a fresh client-clock echo.");
        int bars = sample.Milliseconds switch { < 60 => 4, < 100 => 3, < 200 => 2, _ => 1 };
        string color = bars switch { 4 => "#55D6BE", 3 => "#EABF70", 2 => "#EFAB78", _ => "#EF8792" };
        return new($"Ping ~{sample.Milliseconds} ms", bars, color,
            "Local client-clock echo estimate, not a separately measured round trip. Display bands: <60 / <100 / <200 / 200+ ms. Samples expire after 15 seconds.");
    }
}
