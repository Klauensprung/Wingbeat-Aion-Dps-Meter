namespace AionDpsMeter.Local;

public sealed record LatencyReading(long Samples, double AverageMs, double MaxMs);
public sealed record CaptureTiming(int QueuedPackets, int PeakQueuedPackets, LatencyReading DriverDelivery,
    LatencyReading QueueWait, LatencyReading Processing, LatencyReading CaptureToProcessed);

// Fixed memory and no sample lists on the capture path. Each counter has one writer;
// UI/diagnostics can read concurrently. Times are rounded to microseconds.
internal sealed class LatencyCounter
{
    long count, total, maximum;
    public void Add(double milliseconds)
    {
        long value = Math.Max(0, (long)Math.Round(milliseconds * 1000));
        Interlocked.Add(ref total, value);
        Interlocked.Exchange(ref maximum, Math.Max(Interlocked.Read(ref maximum), value));
        Interlocked.Increment(ref count);
    }
    public LatencyReading Snapshot()
    {
        long samples = Interlocked.Read(ref count);
        return new(samples, samples == 0 ? 0 : Interlocked.Read(ref total) / (samples * 1000d), Interlocked.Read(ref maximum) / 1000d);
    }
}
