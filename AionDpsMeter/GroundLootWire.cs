using System.Buffers.Binary;
using System.IO;

namespace AionDpsMeter.Local;

internal sealed record GroundDrop(uint SourceId, uint Reference, uint DropId, int ItemId, uint Quantity, uint OwnerId, ushort Server, ulong ExpiresAt);
internal sealed record GroundRemoval(uint SourceId, uint Reference, uint DropId, uint OwnerId, ushort Server);
internal sealed record GroundDropBatch(GroundDrop[] Drops, byte Flag);
internal sealed record GroundRemovalBatch(GroundRemoval[] Drops, bool PickedUp);

// Independent implementation of the fixed layouts documented in the pinned
// inventory/quest protocol notes listed in THIRD-PARTY-NOTICES. Experimental.
internal static class GroundLootWire
{
    internal static GroundDropBatch Appeared(ref WireReader reader)
    {
        int count = reader.Byte();
        if (count == 0 || reader.Remaining != count * 63 + 1) throw new InvalidDataException("Unknown ground-drop layout.");
        var drops = new GroundDrop[count];
        var ids = new HashSet<uint>();
        for (int i = 0; i < count; i++) {
            var row = reader.Take(63);
            uint id = U32(row, 32), item = U32(row, 36), quantity = U32(row, 40);
            if (id == 0 || !ids.Add(id) || item is 0 or > int.MaxValue || quantity > 1_000_000_000)
                throw new InvalidDataException("Invalid ground-drop identity or quantity.");
            drops[i] = new(U32(row, 0), U32(row, 28), id, (int)item, quantity, U32(row, 45),
                BinaryPrimitives.ReadUInt16LittleEndian(row[51..]), BinaryPrimitives.ReadUInt64LittleEndian(row[53..]));
        }
        byte flag = reader.Byte();
        if (flag is not (1 or 2)) throw new InvalidDataException("Unknown ground-drop flag.");
        return new(drops, flag);
    }
    internal static GroundRemovalBatch Removed(ref WireReader reader)
    {
        int count = reader.Byte();
        if (count == 0 || reader.Remaining != count * 20 + 1) throw new InvalidDataException("Unknown ground-removal layout.");
        var drops = new GroundRemoval[count];
        var ids = new HashSet<uint>();
        for (int i = 0; i < count; i++) {
            var row = reader.Take(20);
            uint id = U32(row, 8);
            if (id == 0 || !ids.Add(id)) throw new InvalidDataException("Invalid ground-removal identity.");
            drops[i] = new(U32(row, 0), U32(row, 4), id, U32(row, 12), BinaryPrimitives.ReadUInt16LittleEndian(row[18..]));
        }
        byte flag = reader.Byte();
        if (flag > 1) throw new InvalidDataException("Unknown ground-removal flag.");
        return new(drops, flag == 1);
    }
    static uint U32(ReadOnlySpan<byte> row, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(row[offset..]);
}
