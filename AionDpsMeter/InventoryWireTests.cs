using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class InventoryWireTests
{
    internal static void Run(Action<bool, string> check)
    {
        // Independently generated synthetic packets. Booleans intentionally share
        // a byte across fields and rows, including optional equipment detail.
        byte[] Body(bool snapshot, int count, bool equipment = false, byte container = 1) {
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            if (snapshot) writer.Write((ushort)0);
            writer.Write((byte)count);
            int bits = 0;
            void Flag() { if (bits++ % 8 == 0) writer.Write((byte)0); }
            for (int i = 0; i < count; i++) {
                if (!snapshot) writer.Write((byte)0);
                writer.Write((byte)(equipment ? 1 : 0)); writer.Write(5000000000L + i);
                writer.Write(110120003); writer.Write(6000000000L + i); writer.Write(container); writer.Write(i);
                Flag(); writer.Write((byte)0); writer.Write(0L); Flag();
                if (equipment) {
                    writer.Write((byte)2); writer.Write(new byte[8]); writer.Write((byte)3); writer.Write(0); writer.Write(100); Flag(); writer.Write((byte)5);
                    writer.Write((byte)1); writer.Write(123); writer.Write((ushort)19); writer.Write((byte)2); writer.Write(new byte[9]);
                    foreach (int width in new[] { 4, 6, 6, 6, 5, 5, 6, 5 }) {
                        writer.Write((byte)1);
                        if (width == 4) writer.Write(456);
                        else if (width == 6) { writer.Write((ushort)19); writer.Write(-5); }
                        else { writer.Write(17010000); writer.Write((byte)3); }
                    }
                    writer.Write((byte)0);
                }
                if (!snapshot) writer.Write(new byte[] { 5, 0, 0 });
            }
            if (snapshot) writer.Write(new byte[6]);
            return stream.ToArray();
        }
        DecodeResult Decode(byte[] body, bool snapshot) {
            var prefix = new List<byte>(); int length = body.Length + 6;
            do { byte b = (byte)(length & 127); length >>= 7; prefix.Add((byte)(b | (length == 0 ? 0 : 128))); } while (length > 0);
            return new EuGameDecoder().Decode(new CapturedPayload(13328, 50000,
                [..prefix, snapshot ? (byte)0x11 : (byte)0x1b, 0x56, ..body]));
        }
        foreach (bool snapshot in new[] { true, false }) {
            foreach (bool equipment in new[] { false, true }) {
                var body = Body(snapshot, 5, equipment);
                var decoded = Decode(body, snapshot).Events.Single().Network!.Fields.Deserialize<InventoryBatch>()!;
                check(decoded.Snapshot == snapshot && decoded.Stacks.Length == 5 && decoded.Stacks[4].InstanceId == 5000000004L &&
                    decoded.Stacks[4].Count == 6000000004L && (snapshot || decoded.Stacks.All(s => s.IncreaseReason == 5 && s.Modification == 0)),
                    $"Inventory {(snapshot ? "snapshot" : "updates")} preserves 64-bit stacks and shared boolean alignment, equipment={equipment}");
                check(Enumerable.Range(0, body.Length).All(n => Decode(body[..n], snapshot).Events.Count == 0) &&
                    Decode([..body, 0], snapshot).Events.Count == 0,
                    $"Inventory rejects every truncated packet and extra trailer atomically, snapshot={snapshot}, equipment={equipment}");
            }
        }
        var equippedBody = Body(true, 2, true, 11);
        var gear = Decode(equippedBody, true).Events.Single().Network!.Fields.Deserialize<InventoryBatch>()!.Stacks[0].Equipment;
        check(gear is { Slot: 5, Enchant: 2, Surpass: 3, ItemLevel: 100, MainStats.Length: 1, Stats.Length: 3, SoulStats.Length: 1, Skills.Length: 3 } &&
            gear.Stats.All(s => s.Value == -5) && gear.Manastones.Single() is { Item: 123, Stat: 19, Grade: 2 } && gear.Godstones.Single() == 456,
            "Equipped inventory details retain signed stats, skill levels and socketed stones with distinct soul/main stats");
        check(Enumerable.Range(0, equippedBody.Length).All(n => Decode(equippedBody[..n], true).Events.Count == 0) && Decode([..equippedBody, 0], true).Events.Count == 0,
            "Retained gear-detail path rejects every truncated equipped packet and unknown trailer atomically");
        check(Decode(Body(true, 1, true), true).Events.Single().Network!.Fields.Deserialize<InventoryBatch>()!.Stacks[0].Equipment == null,
            "Unequipped inventory rows validate equipment layout without retaining unused gear arrays");
        foreach (bool snapshot in new[] { true, false }) {
            var malformed = Body(snapshot, 2, true, 11);
            byte[] skillBytes = BitConverter.GetBytes(17010000);
            int offset = malformed.AsSpan().LastIndexOf(skillBytes);
            BitConverter.GetBytes(-1).CopyTo(malformed, offset);
            check(Decode(malformed, snapshot).Events.Count == 0,
                $"Invalid retained equipment skill rejects the entire inventory batch before engine mutation, snapshot={snapshot}");
        }
        check(Decode(Body(true, 0), true).Events.Count == 1 && Decode(Body(false, 0), false).Events.Count == 1,
            "Empty inventory snapshots and update lists remain valid");
        var invalid = Body(false, 1); invalid[1] = 3;
        check(Decode(invalid, false).Events.Count == 0, "Unknown inventory modification types are rejected");
    }
}
