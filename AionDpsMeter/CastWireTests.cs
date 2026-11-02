using System.IO;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class CastWireTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        byte[] Body(int flags, int targetFlags, bool charge) {
            var b = new List<byte> { 1, (byte)flags }; b.AddRange(BitConverter.GetBytes(17090000)); b.Add(2);
            b.Add((byte)targetFlags); b.Add(3);
            if ((targetFlags & 1) != 0) b.AddRange(new byte[4]);
            b.AddRange(new byte[4]); if ((targetFlags & 2) != 0) b.AddRange(new byte[12]);
            if (charge) { if ((flags & 1) != 0) b.AddRange(BitConverter.GetBytes(0.5f)); }
            else b.Add(100);
            b.Add(4);
            if ((flags & (charge ? 2 : 1)) != 0) b.Add(5);
            if (!charge) {
                if ((flags & 2) != 0) b.Add(6);
                if ((flags & 4) != 0) { b.Add(1); b.Add(7); b.AddRange(new byte[12]); }
            }
            return b.ToArray();
        }
        bool layouts = true;
        foreach (bool charge in new[] { false, true })
        for (int flags = 0; flags < (charge ? 4 : 8); flags++)
        for (int target = 0; target < 4; target++) {
            var r = new WireReader(Body(flags, target, charge)); var v = CastWire.Start(ref r, charge);
            layouts &= v.TargetId == 3 && v.Level == 2 && v.CastingMs == (charge ? 0 : 100) &&
                v.ChargeRate == (charge && (flags & 1) != 0 ? 0.5f : 0) && r.Remaining == 0;
        }
        check(layouts, "All cast/charge optional flags and target forms decode with their timing and action fields");
        bool truncated = true;
        foreach (bool charge in new[] { false, true }) {
            byte[] full = Body(charge ? 3 : 7, 3, charge);
            for (int n = 0; n < full.Length; n++) {
                try { var r = new WireReader(full.AsSpan(0, n)); CastWire.Start(ref r, charge); truncated = false; }
                catch (InvalidDataException) { }
            }
        }
        check(truncated, "Every truncation of complete cast and charge payloads is rejected");
        byte[] stop = [1, 2, 0, ..BitConverter.GetBytes(17090000), 100];
        var stopReader = new WireReader(stop); var stopped = CastWire.Stop(ref stopReader);
        check(stopped is { ActorId: 1, Result: 2, SkillCode: 17090000, CooldownMs: 100 },
            "Cast stop preserves result and cooldown without inventing a damage or defense event");
        using var fixtures = new ReferencePacketFixtures(Path.Combine(temp, "reference-cast-stops.jsonl"));
        foreach (ushort opcode in new ushort[] { 0x0c38, 0x2138 })
        foreach (ushort result in new ushort[] { 0, 1, 6151, 6152, 6153, ushort.MaxValue }) {
            var w = new PartyWireTests.Writer(); w.Var(1); w.Raw(BitConverter.GetBytes(result)); w.Int(17090000); w.Var(1234);
            byte[] body = w.Bytes.ToArray(); var frame = new PartyWireTests.Writer(); frame.Var(body.Length + 6); frame.Raw((byte)(opcode >> 8), (byte)opcode); frame.Raw(body);
            var packet = new CapturedPayload(13328, 50000, frame.Bytes.ToArray()) { TimestampMs = 1000 }; fixtures.Add(packet);
            var decoded = new EuGameDecoder().Decode(packet);
            var fields = decoded.Events.Single().Network!.Fields;
            check(fields.GetProperty("Reason").GetString() == (result == 0 ? "cancelled" : result == 6152 ? "interrupted" : "failed") && fields.GetProperty("CooldownMs").GetInt32() == 1234,
                "Cast/charge stop distinguishes interruption, cancellation and failure while preserving cooldown: " + opcode + "/" + result);
        }
    }
}
