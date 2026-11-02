using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class FieldBossWireTests
{
    internal static void Run(Action<bool, string> check)
    {
        byte[] Var(int value) { var b = new List<byte>(); do { byte n = (byte)(value & 127); value >>= 7; b.Add((byte)(n | (value == 0 ? 0 : 128))); } while (value > 0); return b.ToArray(); }
        byte[] Body(int count, bool extras = false) {
            var b = new List<byte> { 0, 0 }; b.AddRange(BitConverter.GetBytes(1000)); b.AddRange(Var(count));
            int bit = 8, at = 0;
            for (int i = 1; i <= count; i++) {
                bool position = i % 2 == 0, alive = i % 3 == 0;
                b.Add((byte)(position ? 1 : 0)); b.AddRange(Var(100000 + i));
                if (position) { b.AddRange(BitConverter.GetBytes(12.5f)); b.AddRange(BitConverter.GetBytes(-3f)); b.AddRange(BitConverter.GetBytes(8f)); }
                if (bit == 8) { at = b.Count; b.Add(0); bit = 0; }
                if (alive) b[at] |= (byte)(1 << bit); bit++;
                b.AddRange(BitConverter.GetBytes(1700000600000L));
            }
            if (extras) {
                b.Add(1); b.Add(1); b.AddRange(BitConverter.GetBytes(123)); b.AddRange(new byte[12]);
                b.Add(1); b.Add(2); b.AddRange(BitConverter.GetBytes(456)); b.AddRange(BitConverter.GetBytes(789));
                b.AddRange(new byte[12]); b.Add(3); b.AddRange(new byte[8]);
                b.Add(1); b.Add(4); b.AddRange(BitConverter.GetBytes(999)); b.AddRange(new byte[12]);
            } else b.AddRange(new byte[3]);
            return b.ToArray();
        }
        DecodeResult Decode(byte[] body) {
            int size = body.Length + 6; byte[] length = Var(size);
            return new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, [..length, 0x01, 0x91, ..body]) { TimestampMs = 1700000000000 });
        }
        var decoded = Decode(Body(9, true));
        var list = decoded.Events.Single().Network!.Fields.Deserialize<FieldBossList>()!;
        check(list.MapId == 1000 && list.Slots.Length == 9 && list.Slots[1] is { Alive: false, X: 12.5f } &&
            list.Slots[2] is { Alive: true, X: null } && list.Slots[8].Alive,
            "Field boss position and spawn state are independent, with booleans shared across entries");
        check(Decode(Body(0)).Events.Single().Network!.Fields.GetProperty("Slots").GetArrayLength() == 0,
            "An empty field boss response is a valid authoritative snapshot");
        byte[] full = Body(9, true);
        check(Enumerable.Range(0, full.Length).All(i => Decode(full[..i]).Events.Count == 0) && Decode([..full, 0]).Events.Count == 0,
            "Every truncation of populated field map lists and unexpected trailing data is rejected atomically");
        check(Decode(Body(99)).Events.Single().Network!.Fields.GetProperty("Slots").GetArrayLength() == 99 &&
            Decode(Body(100)).Events.Count == 0,
            "Bounded named slots decode without combinatorial candidate guessing");
        byte[] error = Body(0); error[0] = 1;
        check(Decode(error).Events.Count == 0, "Unsuccessful field boss replies do not overwrite timers");
        var tracker = new FieldBossTracker(); tracker.Apply(decoded.Events.Single().Network!);
        tracker.Apply(Decode(Body(0)).Events.Single().Network!);
        check(tracker.Rows(1700000000000).Length == 0, "An empty current field boss snapshot clears stale map timers");
    }
}
