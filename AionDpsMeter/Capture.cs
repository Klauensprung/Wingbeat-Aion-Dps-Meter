using System.Diagnostics;
using System.Threading.Channels;
using System.IO;
using AionDpsMeter.Models;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;

namespace AionDpsMeter.Local;

// One adapter and a single parser worker prevent duplicate capture and concurrent decoding.
public sealed class LocalCapture : IDisposable
{
    readonly ILiveDevice device;
    readonly Action<CapturedPayload> consume;
    readonly Channel<QueuedPacket> queue = Channel.CreateBounded<QueuedPacket>(new BoundedChannelOptions(512) {
        SingleWriter = true, SingleReader = true, AllowSynchronousContinuations = false
    });
    readonly record struct QueuedPacket(RawCapture Raw, long Arrived, double CaptureAgeMs);
    readonly LatencyCounter deliveryLatency = new(), queueLatency = new(), processingLatency = new(), totalLatency = new();
    readonly Dictionary<string, TcpAssembler> streams = new();
    readonly HashSet<string> outbound = new();
    Task? worker;
    CaptureFileWriterDevice? recorder;
    long recordedBytes;
    long received, dropped, errors;
    int peakQueued;
    public string? LastError { get; private set; }
    public long Received => Interlocked.Read(ref received);
    public long Dropped => Interlocked.Read(ref dropped);
    public long Errors => Interlocked.Read(ref errors);
    public CaptureTiming Timing => new(queue.Reader.Count, Volatile.Read(ref peakQueued), deliveryLatency.Snapshot(), queueLatency.Snapshot(), processingLatency.Snapshot(), totalLatency.Snapshot());
    public LocalCapture(ILiveDevice device, Action<CapturedPayload> consume) { this.device = device; this.consume = consume; }
    public void Start(string filter, string? samplePath = null, bool immediate = true)
    {
        try
        {
            // Buffered mode is retained only for the opt-in latency comparison probe.
            device.Open(immediate ? new DeviceConfiguration { Mode = DeviceModes.None, ReadTimeout = 10, Immediate = true }
                : new DeviceConfiguration { Mode = DeviceModes.None, ReadTimeout = 100 });
            device.Filter = filter;
            if (samplePath != null)
            {
                recorder = new CaptureFileWriterDevice(samplePath, FileMode.Create);
                recorder.Open(new DeviceConfiguration { LinkLayerType = device.LinkType, Snaplen = 65535 });
            }
            device.OnPacketArrival += Arrival;
            worker = Task.Run(async () => {
                await foreach (var queued in queue.Reader.ReadAllAsync())
                {
                    var raw = queued.Raw;
                    long processingStarted = Stopwatch.GetTimestamp();
                    queueLatency.Add(Stopwatch.GetElapsedTime(queued.Arrived, processingStarted).TotalMilliseconds);
                    try {
                        if (recorder != null)
                        {
                            if (recordedBytes + raw.Data.Length + 16 <= 256L * 1024 * 1024) { recorder.Write(raw); recordedBytes += raw.Data.Length + 16; }
                            else { recorder.Close(); recorder = null; LastError = "Sample reached 256 MB; recording stopped. Capture diagnostics continue."; }
                        }
                        // Driver timestamps can be a fraction ahead of wall time.
                        // Fall back to callback arrival, never discard the live clock.
                        Process(raw, (long)(Stopwatch.GetElapsedTime(0, queued.Arrived).TotalMilliseconds - Math.Max(0, queued.CaptureAgeMs)));
                    }
                    catch (Exception ex) { LastError = ex.Message; Interlocked.Increment(ref errors); }
                    finally {
                        long finished = Stopwatch.GetTimestamp();
                        processingLatency.Add(Stopwatch.GetElapsedTime(processingStarted, finished).TotalMilliseconds);
                        if (queued.CaptureAgeMs >= 0)
                            totalLatency.Add(queued.CaptureAgeMs + Stopwatch.GetElapsedTime(queued.Arrived, finished).TotalMilliseconds);
                    }
                }
            });
            device.StartCapture();
        }
        catch { Dispose(); throw; }
    }
    void Arrival(object sender, SharpPcap.PacketCapture args)
    {
        long arrived = Stopwatch.GetTimestamp();
        var raw = args.GetPacket();
        double age = (DateTime.UtcNow - raw.Timeval.Date.ToUniversalTime()).TotalMilliseconds;
        // Npcap timestamps use wall time; exclude clock corrections from this metric.
        if (age is >= 0 and < 3600000) deliveryLatency.Add(age); else age = -1;
        Interlocked.Increment(ref received);
        if (!queue.Writer.TryWrite(new(raw, arrived, age))) Interlocked.Increment(ref dropped);
        else Interlocked.Exchange(ref peakQueued, Math.Max(Volatile.Read(ref peakQueued), queue.Reader.Count));
    }
    void Process(RawCapture raw, long? counterTimestampMs)
    {
        var packet = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
        var tcp = packet.Extract<TcpPacket>();
        var ip = packet.Extract<IPPacket>();
        if (tcp == null || ip == null) return;
        string key = $"{ip.SourceAddress}:{tcp.SourcePort}>{ip.DestinationAddress}:{tcp.DestinationPort}";
        if (tcp.Synchronize && !tcp.Acknowledgment)
        {
            if (outbound.Count > 4096) outbound.Clear();
            outbound.Add(key);
            streams.Remove($"{ip.DestinationAddress}:{tcp.DestinationPort}>{ip.SourceAddress}:{tcp.SourcePort}");
        }
        if (outbound.Contains(key)) return;
        if (tcp.Finished || tcp.Reset) { streams.Remove(key); outbound.Remove(key); }
        if (tcp.PayloadData.Length == 0) return;
        if (!streams.TryGetValue(key, out var stream))
        {
            if (streams.Count >= 256) throw new InvalidOperationException("Too many TCP streams; narrow the capture filter and restart.");
            streams[key] = stream = new TcpAssembler();
        }
        long capturedAt = new DateTimeOffset(raw.Timeval.Date.ToUniversalTime()).ToUnixTimeMilliseconds();
        foreach (var chunk in stream.PushTimed(tcp.SequenceNumber, tcp.PayloadData, capturedAt, counterTimestampMs))
            consume(new CapturedPayload(tcp.SourcePort, tcp.DestinationPort, chunk.Data, device.Name) { Connection = key, TimestampMs = chunk.TimestampMs, CounterTimestampMs = chunk.CounterTimestampMs });
    }
    public void Dispose()
    {
        try { device.StopCapture(); } catch { }
        device.OnPacketArrival -= Arrival;
        try { device.Close(); } catch { }
        queue.Writer.TryComplete();
        worker?.GetAwaiter().GetResult();
        recorder?.Close(); recorder = null;
    }
}

public sealed class TcpAssembler
{
    internal readonly record struct Chunk(byte[] Data, long? TimestampMs, long? CounterTimestampMs);
    uint? next;
    readonly Dictionary<uint, Chunk> pending = new();
    public bool HasPending => pending.Count != 0;
    public IEnumerable<byte[]> Push(uint seq, byte[] data) => PushTimed(seq, data, null, null).Select(chunk => chunk.Data);
    internal IEnumerable<Chunk> PushTimed(uint seq, byte[] data, long? timestampMs, long? counterTimestampMs = null)
    {
        next ??= seq;
        if (unchecked((int)(seq - next.Value)) > 0)
        {
            if (!pending.ContainsKey(seq) && pending.Count >= 128) throw new InvalidOperationException("TCP gap exceeded reassembly limit; restart capture.");
            // A duplicate retransmission must not make already captured bytes newer.
            if (!pending.TryGetValue(seq, out var previous) || data.Length > previous.Data.Length)
                pending[seq] = new(data, timestampMs, counterTimestampMs);
            yield break;
        }
        int skip = unchecked((int)(next.Value - seq));
        if (skip < data.Length)
        {
            var bytes = data[skip..]; next = unchecked(next.Value + (uint)bytes.Length); yield return new(bytes, timestampMs, counterTimestampMs);
        }
        while (pending.Count > 0)
        {
            var ready = pending.Where(p => unchecked((int)(p.Key - next.Value)) <= 0).ToArray();
            if (ready.Length == 0) yield break;
            foreach (var entry in ready)
            {
                pending.Remove(entry.Key);
                skip = unchecked((int)(next.Value - entry.Key));
                if (skip >= entry.Value.Data.Length) continue;
                var bytes = entry.Value.Data[skip..]; next = unchecked(next.Value + (uint)bytes.Length);
                // Keep the packet's capture clock across a gap, not the time of
                // the packet that finally makes these buffered bytes readable.
                yield return entry.Value with { Data = bytes };
            }
        }
    }
}
