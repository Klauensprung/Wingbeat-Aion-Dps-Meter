using System.IO;

namespace AionDpsMeter.Local;

internal static class AbnormalWireTests
{
    internal static void Run(Action<bool, string> check)
    {
        byte[] Body(int flags, bool refresh, int count = 1) {
            var b = new List<byte> { 1 }; if (!refresh) b.Add((byte)count);
            int bits = 8, bitAt = 0;
            void Bit(bool v) { if (bits == 8) { bitAt = b.Count; b.Add(0); bits = 0; } if (v) b[bitAt] |= (byte)(1 << bits); bits++; }
            for (int i = 0; i < count; i++) {
                b.Add((byte)flags); b.Add((byte)i); b.AddRange(BitConverter.GetBytes(1000));
                b.AddRange(BitConverter.GetBytes(4294970296L)); b.AddRange(BitConverter.GetBytes(1700000000000L));
                if ((flags & 1) != 0) b.Add(2);
                b.Add(3);
                if ((flags & 2) != 0) b.AddRange(BitConverter.GetBytes(17090000));
                Bit(i % 2 == 0);
                if ((flags & 4) != 0) b.Add(4);
                if ((flags & 8) != 0) b.AddRange(BitConverter.GetBytes(-5));
                if ((flags & 16) != 0) b.AddRange(new byte[12]);
            }
            if (refresh) Bit(true);
            return b.ToArray();
        }
        bool layouts = true;
        for (int flags = 0; flags < 32; flags++) foreach (bool refresh in new[] { false, true }) {
            var r = new WireReader(Body(flags, refresh)); var b = AbnormalWire.Read(ref r, refresh);
            layouts &= b.Values[0].Duration == 4294970296 && b.Values[0].Level == 3 &&
                b.Values[0].Skill == ((flags & 2) != 0 ? 17090000 : 0) && r.Remaining == 0;
        }
        check(layouts, "All abnormal optional flag combinations decode for additions and refreshes");
        var batchReader = new WireReader(Body(31, false, 9)); var batch = AbnormalWire.Read(ref batchReader, false);
        check(batch.Values.Length == 9 && batch.Values.Select(v => v.ReadyToRemove).SequenceEqual(Enumerable.Range(0, 9).Select(i => i % 2 == 0)),
            "Batched abnormal entries share boolean storage across entries and byte boundaries");
        byte[] full = Body(31, false); bool truncation = true;
        for (int i = 0; i < full.Length; i++) {
            try { var r = new WireReader(full.AsSpan(0, i)); AbnormalWire.Read(ref r, false); truncation = false; }
            catch (InvalidDataException) { }
        }
        check(truncation, "Every truncated complete abnormal addition is rejected atomically");
        bool removal = true;
        for (int flags = 0; flags < 8; flags++) {
            var bytes = new List<byte> { 1, 1, (byte)flags, 2, 3 };
            if ((flags & 1) != 0) bytes.Add(4);
            if ((flags & 2) != 0) bytes.AddRange(BitConverter.GetBytes(5));
            if ((flags & 4) != 0) bytes.AddRange(BitConverter.GetBytes(6));
            var r = new WireReader(bytes.ToArray()); var v = AbnormalWire.Remove(ref r).Values.Single();
            removal &= v.Reason == 3 && v.Caster == ((flags & 1) != 0 ? 4 : 0) && v.Effect == ((flags & 4) != 0 ? 6 : 0);
        }
        check(removal, "All dispel optional field combinations preserve removal reason and source");
    }
}
