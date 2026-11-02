using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class ServerClockTests
{
    internal static void Run(Action<bool, string> check)
    {
        var decoder = new EuGameDecoder();
        DecodeResult Send(byte a, byte b, byte[] body, long time, string connection = "clock-test") => decoder.Decode(
            new CapturedPayload(13328, 50000, [(byte)(body.Length + 6), a, b, ..body]) { TimestampMs = time, Connection = connection });
        byte[] Buff(long expires) => [1, 1, 0, 7, ..BitConverter.GetBytes(1000), ..BitConverter.GetBytes(10000L),
            ..BitConverter.GetBytes(expires), 1, 0];
        Send(0, 0x36, BitConverter.GetBytes(100000L), 1000);
        var e = Send(0x2a, 0x38, Buff(105000), 2000).Events.Single().Network!;
        check(e.Fields.GetProperty("DurationMs").GetInt64() == 4000 && e.Fields.GetProperty("ExpiresAtMs").GetInt64() == 6000 &&
            EffectDuration.TryRead(e.Fields, out var remaining, out _) && remaining == 4000,
            "Server clock translates buff expiration into remaining local time instead of restarting its original duration");
        var expired = Send(0x2a, 0x38, Buff(99000), 2001).Events.Single().Network!;
        check(expired.Fields.GetProperty("DurationMs").GetInt64() == 0 && !expired.Fields.GetProperty("UntilRemoved").GetBoolean(),
            "Already expired finite buffs stay expired rather than becoming permanent");
        var reconnect = Send(0x2a, 0x38, Buff(105000), 2002, "another-connection").Events.Single().Network!;
        check(reconnect.Fields.GetProperty("DurationMs").GetInt64() == 10000 && reconnect.Fields.GetProperty("ExpiresAtMs").ValueKind == System.Text.Json.JsonValueKind.Null,
            "A new connection cannot reuse the preceding server clock offset");
        check(decoder.UnknownRecords == 0, "Map clock packets are recognized without adding repetitive network events");
    }
}
