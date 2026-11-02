using System.IO;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class PeriodicWireTests
{
    internal static void Run(Action<bool, string> check)
    {
        byte[] Body(int flags, int effect = 1234) {
            var b = new List<byte> { 100, (byte)flags, 1, 7 };
            void I(int n) => b.AddRange(BitConverter.GetBytes(n));
            void V(long value) { ulong n = (ulong)value; do { byte v = (byte)(n & 127); n >>= 7; b.Add((byte)(v | (n == 0 ? 0 : 128))); } while (n > 0); }
            I(effect);
            if ((flags & 1) != 0) V(9000000000);
            if ((flags & 2) != 0) V(5000000000);
            if ((flags & 4) != 0) { b.Add(2); I(4); I(5); }
            if ((flags & 8) != 0) I(17090000);
            if ((flags & 16) != 0) V(2);
            if ((flags & 32) != 0) I(12000000);
            if ((flags & 64) != 0) I(13000000);
            return b.ToArray();
        }
        bool all = true;
        for (int flags = 0; flags < 128; flags++) {
            var r = new WireReader(Body(flags)); var v = PeriodicWire.Read(ref r);
            all &= r.Remaining == 0 && v.Amount == ((flags & 2) != 0 ? 5000000000 : 0) &&
                v.Remaining == ((flags & 1) != 0 ? 9000000000 : 0) && v.BarrierIds.Length == ((flags & 4) != 0 ? 2 : 0);
        }
        check(all, "All 128 periodic flag combinations preserve 64-bit amounts and optional field boundaries");
        byte[] full = Body(127); bool truncated = true;
        for (int n = 0; n < full.Length; n++) {
            try { var r = new WireReader(full.AsSpan(0, n)); PeriodicWire.Read(ref r); truncated = false; }
            catch (InvalidDataException) { }
        }
        check(truncated, "Every truncated full periodic record is rejected");
        DecodeResult Decode(int flags, int effect = 1234) {
            byte[] b = Body(flags, effect);
            return new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, [(byte)(b.Length + 6), 5, 0x38, ..b]));
        }
        check(Decode(74).Events.Single().Damage is { Damage: 5000000000, SkillCode: 13000000, IsHeal: false },
            "Reflected periodic damage uses the reflection skill and retains 64-bit damage");
        check(Decode(58).Events.Single().Defense is { ActorId: 2, TargetId: 100, SkillCode: 12000000 },
            "Periodic immunity attributes the avoided attack to its blocked caster");
        check(Decode(10, 30001).Events.Count == 0 && Decode(10, 2981).Events.Count == 0,
            "Periodic non-health and resource effects never enter combat totals");
        check(Decode(3, 4461).Events.Single().Network!.Fields.GetProperty("Remaining").GetInt64() == 9000000000,
            "Shield updates preserve remaining capacity without emitting damage or healing");
    }
}
