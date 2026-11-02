using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class BarrierWireTests
{
    internal static void Run(Action<bool, string> check)
    {
        byte[] Var(ulong value) {
            var b = new List<byte>();
            do { byte next = (byte)(value & 127); value >>= 7; b.Add((byte)(next | (value == 0 ? 0 : 128))); } while (value != 0);
            return b.ToArray();
        }
        DecodeResult Decode(byte[] body) => new EuGameDecoder().Decode(new CapturedPayload(13328, 50000,
            [..Var((ulong)body.Length + 6), 0x46, 0x36, ..body]));
        byte[] full = [1, 2, 7, ..Var(5000000000), 8, 0];
        var decoded = Decode(full);
        check(decoded.Events.Count == 2 && decoded.Events.All(e => e.Kind == "network" && e.Network?.Kind == "barrier") &&
            decoded.Events[0].Network!.Fields.GetProperty("Remaining").GetInt64() == 5000000000 &&
            decoded.Events[1].Network!.Fields.GetProperty("Remaining").GetInt64() == 0,
            "Dedicated barrier packets retain 64-bit capacity and explicit zero without emitting healing");
        check(Enumerable.Range(0, full.Length).All(i => Decode(full[..i]).Events.Count == 0) && Decode([..full, 0]).Events.Count == 0,
            "All truncated barrier lists and unknown trailers emit no partial state");
        check(Decode([1, 2, 7, 1, 7, 2]).Events.Count == 0 && Decode([1, 1, 7, ..Var(ulong.MaxValue)]).Events.Count == 0,
            "Duplicate shield identities and negative capacity are rejected");
        var empty = new EuGameDecoder(); empty.Decode(new CapturedPayload(13328, 50000, [8, 0x46, 0x36, 1, 0]));
        check(empty.RejectedRecords == 0, "An empty barrier update list is valid");
    }
}
