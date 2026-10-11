using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AionDpsMeter.Models;
using PacketDotNet;

namespace AionDpsMeter.Local;

public static class PacketReplay
{
    // Reads classic PCAP directly; replay works without installing or opening Npcap.
    public static IEnumerable<CapturedPayload> Read(string path, int serverPort)
    {
        using var input = File.OpenRead(path);
        if (input.Length > 256L * 1024 * 1024) throw new InvalidDataException("Replay capture exceeds 256 MB.");
        byte[] header = new byte[24]; input.ReadExactly(header);
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(header);
        bool little = magic is 0xa1b2c3d4 or 0xa1b23c4d, nano = magic is 0xa1b23c4d or 0x4d3cb2a1;
        if (magic is not (0xa1b2c3d4 or 0xd4c3b2a1 or 0xa1b23c4d or 0x4d3cb2a1)) throw new InvalidDataException("Expected classic PCAP, not PCAPNG.");
        uint U32(byte[] bytes, int at) => little ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at, 4)) : BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at, 4));
        uint link = U32(header, 20);
        if (link != 1) throw new InvalidDataException("Replay currently supports Ethernet PCAPs only.");
        var streams = new Dictionary<string, TcpAssembler>();
        byte[] packetHeader = new byte[16];
        while (input.Position < input.Length)
        {
            input.ReadExactly(packetHeader);
            uint length = U32(packetHeader, 8), original = U32(packetHeader, 12);
            if (length > 1024 * 1024 || length > original || length > input.Length - input.Position) throw new InvalidDataException("Invalid PCAP packet size.");
            if (length != original) throw new InvalidDataException("Capture contains truncated packets; cannot verify decoder completeness.");
            byte[] data = new byte[length]; input.ReadExactly(data);
            var packet = Packet.ParsePacket(LinkLayers.Ethernet, data);
            var tcp = packet.Extract<TcpPacket>(); var ip = packet.Extract<IPPacket>();
            if (tcp == null || ip == null || tcp.SourcePort != serverPort) continue;
            string key = $"{ip.SourceAddress}:{tcp.SourcePort}>{ip.DestinationAddress}:{tcp.DestinationPort}";
            if (tcp.Synchronize) streams.Remove(key);
            if (!streams.TryGetValue(key, out var stream))
            {
                if (streams.Count >= 256) throw new InvalidDataException("Too many replay connections.");
                streams[key] = stream = new();
            }
            long time = U32(packetHeader, 0) * 1000L + U32(packetHeader, 4) / (nano ? 1000000 : 1000);
            if (tcp.PayloadData.Length > 0)
                foreach (var bytes in stream.Push(tcp.SequenceNumber + (tcp.Synchronize ? 1u : 0u), tcp.PayloadData))
                    yield return new(tcp.SourcePort, tcp.DestinationPort, bytes, "pcap") { Connection = key, TimestampMs = time };
            if (tcp.Finished || tcp.Reset) streams.Remove(key);
        }
        if (streams.Values.Any(s => s.HasPending)) throw new InvalidDataException("Unresolved TCP gaps remain at end of capture.");
    }

    public static int Run(string[] args)
    {
        string Required(string option) { int at = Array.IndexOf(args, option); return at >= 0 && at + 1 < args.Length ? args[at + 1] : throw new ArgumentException($"Missing {option}"); }
        string output = Path.GetFullPath(Required("--output")); Directory.CreateDirectory(output);
        try
        {
            string path = Path.GetFullPath(Required("--pcap"));
            int portAt = Array.IndexOf(args, "--server-port"), port = portAt >= 0 ? int.Parse(args[portAt + 1]) : 13328;
            if (port is < 1 or > 65535) throw new ArgumentException("Invalid game server port.");
            var decoder = new EuGameDecoder(port); var splitDecoder = new EuGameDecoder(port);
            using var baselineHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var splitHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var eventsFile = new StreamWriter(Path.Combine(output, "events.jsonl"), false, new UTF8Encoding(false));
            long payloads = 0, bytes = 0, eventCount = 0;
            foreach (var payload in Read(path, port))
            {
                payloads++; bytes += payload.Data.Length;
                var result = decoder.Decode(payload);
                foreach (var item in result.Events)
                {
                    string line = JsonSerializer.Serialize(item); eventsFile.WriteLine(line); baselineHash.AppendData(Encoding.UTF8.GetBytes(line + "\n")); eventCount++;
                }
                // Deliberately split length prefixes, opcodes and compressed blocks across calls.
                for (int at = 0; at < payload.Data.Length;)
                {
                    int count = Math.Min(1 + (at % 17), payload.Data.Length - at);
                    var fragment = payload with { Data = payload.Data.AsSpan(at, count).ToArray() };
                    foreach (var item in splitDecoder.Decode(fragment).Events)
                        splitHash.AppendData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(item) + "\n"));
                    at += count;
                }
            }
            eventsFile.Flush();
            bool sameEvents = baselineHash.GetHashAndReset().SequenceEqual(splitHash.GetHashAndReset());
            bool sameMetrics = decoder.Records == splitDecoder.Records && decoder.RejectedRecords == splitDecoder.RejectedRecords && decoder.UnknownRecords == splitDecoder.UnknownRecords && decoder.FramingErrors == splitDecoder.FramingErrors && decoder.PendingBytes == splitDecoder.PendingBytes;
            var report = new {
                Capture = Path.GetFileName(path), CaptureSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                ServerPort = port, Payloads = payloads, PayloadBytes = bytes, Events = eventCount,
                decoder.Records, decoder.Bundles, decoder.UnknownRecords, decoder.RejectedRecords, decoder.FramingErrors, decoder.PendingBytes,
                FragmentationInvariant = sameEvents && sameMetrics, decoder.Opcodes, decoder.EventKinds, decoder.Rejections,
                FullEventParityVerified = false,
                Limitations = "Experimental subset only. No reference-oracle or on-screen totals comparison. Player identities and NPC spawn template/name bindings are supported subsets; full lifecycle, DoT, roster and gear remain incomplete. NPC names require a local catalog and observed spawn. Supported direct recovery and periodic ticks are classified locally; effective healing, potions and all skill variants remain unverified. No raw captures or event dumps belong in a public source release."
            };
            File.WriteAllText(Path.Combine(output, "replay-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return payloads > 0 && sameEvents && sameMetrics && decoder.FramingErrors == 0 && decoder.PendingBytes == 0 ? (args.Contains("--require-parity") ? 2 : 0) : 2;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(output, "replay-error.txt"), ex.ToString()); return 1; }
    }
}
