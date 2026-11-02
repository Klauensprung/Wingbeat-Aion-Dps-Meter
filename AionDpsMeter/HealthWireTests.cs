using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class HealthWireTests
{
    internal static void Run(Action<bool, string> check)
    {
        DecodeResult Decode(byte[] fields) => new EuGameDecoder().Decode(new CapturedPayload(13328, 50000,
            [(byte)(fields.Length + 6), 0x00, 0x8d, ..fields]) { TimestampMs = 1000 });
        byte[] hp = [1, 2, 2, 7, ..BitConverter.GetBytes(100000UL), 0, ..BitConverter.GetBytes(25000UL)];
        var eventFields = Decode(hp).Events.Single().Network!.Fields;
        check(eventFields.GetProperty("CurrentHp").GetInt64() == 25000 && eventFields.GetProperty("MaximumHp").GetInt64() == 100000,
            "Point type 7 supplies explicit maximum HP alongside current HP");
        var combined = Decode([1, 3, 1, 6, ..BitConverter.GetBytes(900u), 1, 0, ..BitConverter.GetBytes(22000UL)]);
        check(combined.Events.Single(e => e.Network?.Kind == "hp").Network!.Fields.GetProperty("CurrentHp").GetInt64() == 22000,
            "32-bit resource maps do not shift the following 64-bit HP map");
        var decoder = new EuGameDecoder();
        decoder.Decode(new CapturedPayload(13328, 50000, [14, 0x00, 0x8d, 1, 1, 1, 6, ..BitConverter.GetBytes(900u)]) { TimestampMs = 1000 });
        check(decoder.Records == 1 && decoder.RejectedRecords == 0 && decoder.EventKinds.GetValueOrDefault("resources") == 1 && !decoder.EventKinds.ContainsKey("hp"),
            "Flight-resource updates remain separate from HP readings");
        check(Decode([1, 1, 1, 0, ..BitConverter.GetBytes(1234)]).Events.Single().Network!.Fields.GetProperty("CurrentHp").GetInt64() == 1234 &&
            Decode([1, 3, 1, 0, ..BitConverter.GetBytes(1234), 1, 0, ..BitConverter.GetBytes(5000L)]).Events.Single().Network!.Fields.GetProperty("CurrentHp").GetInt64() == 1234,
            "32-bit HP maps are decoded and take precedence when both widths contain the same point");
        check(Decode([1, 2, 1, 0, ..BitConverter.GetBytes(1234)]).Events.Count == 0,
            "A truncated 64-bit HP value is not accepted as a guessed compact variant");
        check(Decode(hp[..^1]).Events.Count == 0 && Decode([..hp, 0]).Events.Count == 0 &&
            Decode([1, 2, 2, 0, ..BitConverter.GetBytes(1UL), 0, ..BitConverter.GetBytes(2UL)]).Events.Count == 0 &&
            Decode([1, 2, 1, 0, ..BitConverter.GetBytes(ulong.MaxValue)]).Events.Count == 0,
            "Health decoding rejects inconsistent counts, duplicate current values and signed overflow atomically");
    }
}
