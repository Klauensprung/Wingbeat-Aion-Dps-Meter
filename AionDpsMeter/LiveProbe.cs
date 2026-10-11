using System.IO;
using System.Diagnostics;
using System.Text.Json;
using SharpPcap.LibPcap;

namespace AionDpsMeter.Local;

public static class LiveProbe
{
    public static int Run(string[] args)
    {
        string Required(string option) { int i = Array.IndexOf(args, option); return i >= 0 && i + 1 < args.Length ? args[i + 1] : throw new ArgumentException($"Missing {option}"); }
        string output = Path.GetFullPath(Required("--output"));
        Directory.CreateDirectory(output);
        try
        {
            string adapter = Required("--adapter"), filter = Required("--filter");
            int seconds = Math.Clamp(int.Parse(Required("--seconds")), 1, 120);
            int signatureAt = Array.IndexOf(args, "--signatures");
            string signaturePath = signatureAt >= 0 ? Required("--signatures") : Path.Combine(AppContext.BaseDirectory, "protocol", "opcodes.json");
            using var catalog = JsonDocument.Parse(File.ReadAllText(signaturePath));
            var signatures = catalog.RootElement.GetProperty("signatures").EnumerateArray()
                .Where(e => e.GetProperty("kind").GetString() != "tunnel-tag")
                .Select(e => (Name: e.GetProperty("name").GetString()!, Bytes: e.GetProperty("bytes").EnumerateArray().Select(b => b.GetByte()).ToArray(), Hex: e.GetProperty("hex").GetString()!))
                .Where(e => e.Bytes.Length >= 2).ToArray();
            int carrySize = signatures.Max(e => e.Bytes.Length) - 1;
            var tails = new Dictionary<string, byte[]>();
            var connections = new HashSet<string>();
            var matches = new long[signatures.Length];
            long payloads = 0, bytes = 0;
            var devices = LibPcapLiveDeviceList.Instance.Where(d => d.Name.Equals(adapter, StringComparison.OrdinalIgnoreCase) || (d.Description ?? "").Contains(adapter, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (devices.Length != 1) throw new InvalidOperationException($"Expected one adapter matching '{adapter}', found {devices.Length}.");
            var start = DateTime.UtcNow;
            var cpuStart = Process.GetCurrentProcess().TotalProcessorTime;
            bool latencyOnly = args.Contains("--latency-only"), immediate = !args.Contains("--buffered-capture");
            var decoder = args.Contains("--decode") ? new EuGameDecoder() : null;
            using var capture = new LocalCapture(devices[0], payload => {
                payloads++; bytes += payload.Data.Length;
                connections.Add(payload.Connection);
                decoder?.Decode(payload);
                if (latencyOnly) return;
                var prefix = tails.GetValueOrDefault(payload.Connection) ?? [];
                byte[] combined = [..prefix, ..payload.Data];
                for (int s = 0; s < signatures.Length; s++)
                {
                    var pattern = signatures[s].Bytes;
                    for (int at = Math.Max(0, prefix.Length - pattern.Length + 1); at + pattern.Length <= combined.Length; at++)
                        if (combined.AsSpan(at, pattern.Length).SequenceEqual(pattern)) matches[s]++;
                }
                tails[payload.Connection] = combined.TakeLast(carrySize).ToArray();
            });
            capture.Start(filter, latencyOnly ? null : Path.Combine(output, "game-sample.pcap"), immediate);
            File.WriteAllText(Path.Combine(output, "running.txt"), $"Started UTC {start:O}; duration {seconds}s; filter {filter}");
            for (int i = 0; i < seconds; i++) Thread.Sleep(1000);
            capture.Dispose();
            var report = new {
                StartedUtc = start, FinishedUtc = DateTime.UtcNow, Adapter = devices[0].Description, Filter = filter,
                Packets = capture.Received, QueueDrops = capture.Dropped, CaptureErrors = capture.Errors, capture.LastError,
                CaptureMode = immediate ? "Immediate" : "Buffered (previous default)", Latency = capture.Timing,
                CpuMs = (Process.GetCurrentProcess().TotalProcessorTime - cpuStart).TotalMilliseconds,
                ReassembledPayloads = payloads, PayloadBytes = bytes, Connections = connections.Count,
                Interpretation = latencyOnly ? "Npcap timestamp to decoder callback completion; excludes server/network transit and WPF display time. No PCAP recording or signature scan. Driver timestamp age uses wall time; queue and processing use a monotonic clock."
                    : "Raw byte-pattern occurrences only, including possible coincidences inside fields or compressed/encrypted data. No verified record framing, damage decoding or DPS comparison.",
                Matches = signatures.Select((s, i) => new { s.Name, s.Hex, Occurrences = matches[i] }).Where(m => m.Occurrences > 0).OrderByDescending(m => m.Occurrences).ToArray()
                , Decoding = decoder == null ? null : new {
                    decoder.Records, decoder.Bundles, decoder.UnknownRecords, decoder.RejectedRecords, decoder.FramingErrors,
                    decoder.PendingBytes, decoder.EventKinds, decoder.Rejections, FullEventParityVerified = false
                }
            };
            File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return capture.Received > 0 && capture.Errors == 0 && capture.Dropped == 0 ? 0 : 2;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString()); return 1; }
    }
}
