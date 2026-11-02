using System.Buffers.Binary;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class GroundLootWireTests
{
    internal static void Run(Action<bool, string> check)
    {
        DecodeResult Zone(uint map, int size = 46) {
            var body = new byte[size]; if (size >= 8) BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), map);
            return new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, [(byte)(body.Length + 6), 0x21, 0x36, ..body]));
        }
        check(Zone(1010).Events.Single().Network!.Fields.GetProperty("MapId").GetInt32() == 1010 && Zone(0).Events.Count == 0 && Zone(1010, 8).Events.Count == 0,
            "Experimental zone decoding reads the documented map offset and rejects zero IDs and incomplete layouts");
        byte[] Var(int value) { var bytes = new List<byte>(); do { byte b = (byte)(value & 127); value >>= 7; bytes.Add((byte)(b | (value == 0 ? 0 : 128))); } while (value > 0); return bytes.ToArray(); }
        DecodeResult Decode(byte op, byte count, byte[] rows, byte flag) {
            byte[] body = [count, ..rows, flag];
            return new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, [..Var(body.Length + 6), op, 0x56, ..body]) { TimestampMs = 1700000000000 });
        }
        void Set(byte[] row, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(offset), value);
        byte[] Drop(uint id, uint quantity) {
            var row = new byte[63]; Set(row, 0, 10); Set(row, 28, 20); Set(row, 32, id); Set(row, 36, 500000001);
            Set(row, 40, quantity); Set(row, 45, 30); BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(51), 1);
            BinaryPrimitives.WriteUInt64LittleEndian(row.AsSpan(53), 1700000120000); return row;
        }
        byte[] Gone(uint id, uint owner = 30) {
            var row = new byte[20]; Set(row, 0, 10); Set(row, 4, 20); Set(row, 8, id); Set(row, 12, owner);
            BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(18), 1); return row;
        }
        var appeared = Decode(0x1e, 2, [..Drop(100, 3), ..Drop(101, 0)], 2).Events.Single().Network!.Fields.Deserialize<GroundDropBatch>()!;
        check(appeared.Flag == 2 && appeared.Drops[0] == new GroundDrop(10, 20, 100, 500000001, 3, 30, 1, 1700000120000) && appeared.Drops[1].Quantity == 0,
            "Ground drops preserve item quantities, owner/server and correlation fields, including zero-count event objects");
        var removed = Decode(0x1f, 2, [..Gone(100), ..Gone(101, 0)], 1).Events.Single().Network!.Fields.Deserialize<GroundRemovalBatch>()!;
        check(removed.PickedUp && removed.Drops[0] == new GroundRemoval(10, 20, 100, 30, 1) && removed.Drops[1].OwnerId == 0,
            "Ground pickup messages retain every drop and allow an unknown owner without attributing it to the local character");
        check(!Decode(0x1f, 1, Gone(100), 0).Events.Single().Network!.Fields.GetProperty("PickedUp").GetBoolean(),
            "A drop disappearing with flag zero is not reported as a pickup");
        check(Decode(0x1e, 2, [..Drop(100, 1), ..Drop(100, 2)], 1).Events.Count == 0 &&
            Decode(0x1f, 2, [..Gone(100), ..Gone(100)], 1).Events.Count == 0,
            "Duplicate drop identities reject the entire batch without partial events");
        check(Decode(0x1e, 1, Drop(100, 1)[..^1], 1).Events.Count == 0 && Decode(0x1f, 1, [..Gone(100), 0], 1).Events.Count == 0 &&
            Decode(0x1e, 0, [], 1).Events.Count == 0 && Decode(0x1f, 1, Gone(100), 2).Events.Count == 0,
            "Truncated, extended, empty and unknown-flag loot layouts are rejected atomically");
        check(Decode(0x1e, 1, Drop(0, 1), 1).Events.Count == 0 && Decode(0x1e, 1, Drop(100, uint.MaxValue), 1).Events.Count == 0,
            "Invalid drop identities and implausibly large quantities cannot enter loot tracking");
        check(Decode(0x1e, 255, Enumerable.Range(1, 255).SelectMany(i => Drop((uint)i, 1)).ToArray(), 1).Events.Single().Network!.Fields.GetProperty("Drops").GetArrayLength() == 255,
            "The maximum byte-count ground batch remains bounded and decodes through multi-byte record framing");
    }
}
