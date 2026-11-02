using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class AbnormalClockTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        const long at = 1700000000000;
        static byte[] Frame(byte high, byte low, byte[] body) => [(byte)(body.Length + 6), high, low, ..body];
        static CapturedPayload Payload(byte[] bytes, long time, string connection = "clock") =>
            new(13328, 50000, bytes) { Connection = connection, TimestampMs = time };
        var cases = new List<object>();
        foreach (long? offset in new long?[] { null, -5000, 0, 90000 })
        foreach (long duration in new long[] { -1, 0, 3000, 4294970296 })
        foreach (long remaining in new long[] { -500, 0, 1500, 6000 })
        foreach (bool refresh in new[] { false, true }) {
            var decoder = new EuGameDecoder();
            if (offset is long delta)
                decoder.Decode(Payload(Frame(0, 0x36, BitConverter.GetBytes(at + delta)), at));
            long expires = at + (offset ?? 0) + remaining;
            byte[] body = [1, ..(refresh ? Array.Empty<byte>() : new byte[] { 1 }), 0, 2,
                ..BitConverter.GetBytes(1000), ..BitConverter.GetBytes(duration), ..BitConverter.GetBytes(expires), 1, 0];
            var fields = decoder.Decode(Payload(Frame(refresh ? (byte)0x2b : (byte)0x2a, 0x38, body), at)).Events.Single().Network!.Fields;
            long? deadline = duration > 0 && offset.HasValue ? at + remaining : null;
            long expected = duration <= 0 ? uint.MaxValue : deadline.HasValue ? Math.Max(0, remaining) : duration;
            check(fields.GetProperty("DurationMs").GetInt64() == expected &&
                fields.GetProperty("UntilRemoved").GetBoolean() == (duration <= 0) &&
                fields.GetProperty("ExpiresAtMs").Deserialize<long?>() == deadline,
                $"Abnormal clock offset {offset}, duration {duration}, remaining {remaining}, refresh {refresh}");
            cases.Add(new { At = at, Offset = offset, Duration = duration, Expires = expires, Refresh = refresh, Fields = fields });
        }
        File.WriteAllText(Path.Combine(temp, "abnormal-clock-fixtures.json"), JsonSerializer.Serialize(cases));
    }
}
