using System.Buffers.Binary;
using System.IO;

namespace AionDpsMeter.Local;

// Protocol facts and their provenance are documented in THIRD-PARTY-NOTICES.txt.
// This reader owns its bounds checks; handlers never read past a framed record.
internal ref struct WireReader(ReadOnlySpan<byte> bytes)
{
    readonly ReadOnlySpan<byte> bytes = bytes;
    public int Position { get; private set; }
    public int Remaining => bytes.Length - Position;
    byte bitBuffer;
    int bitsLeft;
    public bool Bit()
    {
        if (bitsLeft == 0) { bitBuffer = Byte(); bitsLeft = 8; }
        bool value = (bitBuffer & 1) != 0; bitBuffer >>= 1; bitsLeft--; return value;
    }
    public ReadOnlySpan<byte> Take(int length)
    {
        if (length < 0 || length > Remaining) throw new InvalidDataException("Truncated game record.");
        var value = bytes.Slice(Position, length); Position += length; return value;
    }
    public byte Byte() => Take(1)[0];
    public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
    public int Var()
    {
        uint value = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            byte b = Byte();
            if (shift == 28 && b > 7) throw new InvalidDataException("Varint exceeds Int32.");
            value |= (uint)(b & 127) << shift;
            if (b < 128) return (int)value;
        }
        throw new InvalidDataException("Unterminated varint.");
    }
}

internal sealed class GameRecordStream
{
    public const int Limit = 1024 * 1024;
    byte[] pending = [];
    public int PendingBytes => pending.Length;
    public List<byte[]> Push(ReadOnlySpan<byte> incoming)
    {
        if (incoming.Length + pending.Length > Limit) { pending = []; throw new InvalidDataException("Game stream buffer limit exceeded."); }
        byte[] joined = [..pending, ..incoming];
        var records = new List<byte[]>();
        int at = 0;
        while (at < joined.Length)
        {
            int prefix = 0, declared = 0;
            while (prefix < 4 && at + prefix < joined.Length)
            {
                byte b = joined[at + prefix]; declared |= (b & 127) << (prefix * 7); prefix++;
                if (b < 128) break;
            }
            if ((joined[at + prefix - 1] & 128) != 0)
            {
                if (prefix < 4) break;
                pending = []; throw new InvalidDataException("Invalid game record length prefix.");
            }
            // The wire length excludes its own varint and includes four virtual header bytes.
            int length = prefix + declared - 4;
            if (length < prefix + 2 || length > Limit) { pending = []; throw new InvalidDataException("Invalid game record length; restart at a record boundary."); }
            if (length > joined.Length - at) break;
            records.Add(joined.AsSpan(at, length).ToArray()); at += length;
        }
        pending = joined[at..];
        return records;
    }
}

internal static class Lz4Block
{
    // Raw LZ4 block format: token, literal extension, literals, LE offset, match extension.
    public static byte[] Expand(ReadOnlySpan<byte> input, int outputLength)
    {
        if (outputLength is < 1 or > GameRecordStream.Limit) throw new InvalidDataException("Invalid LZ4 output size.");
        byte[] output = new byte[outputLength];
        int source = 0, dest = 0;
        while (source < input.Length)
        {
            byte token = input[source++];
            int literal = token >> 4;
            if (literal == 15) literal = Extend(input, ref source, literal);
            if (literal > input.Length - source || literal > output.Length - dest) throw new InvalidDataException("LZ4 literal overrun.");
            input.Slice(source, literal).CopyTo(output.AsSpan(dest)); source += literal; dest += literal;
            if (source == input.Length) break;
            if (input.Length - source < 2) throw new InvalidDataException("Missing LZ4 match offset.");
            int distance = BinaryPrimitives.ReadUInt16LittleEndian(input[source..]); source += 2;
            if (distance == 0 || distance > dest) throw new InvalidDataException("Invalid LZ4 match offset.");
            int match = token & 15;
            if (match == 15) match = Extend(input, ref source, match);
            match += 4;
            if (match > output.Length - dest) throw new InvalidDataException("LZ4 match overrun.");
            for (int i = 0; i < match; i++) { output[dest] = output[dest - distance]; dest++; }
        }
        if (dest != output.Length) throw new InvalidDataException("LZ4 output size mismatch.");
        return output;
    }
    static int Extend(ReadOnlySpan<byte> input, ref int at, int length)
    {
        byte value;
        do
        {
            if (at >= input.Length) throw new InvalidDataException("Truncated LZ4 length.");
            value = input[at++]; length += value;
            if (length > GameRecordStream.Limit) throw new InvalidDataException("LZ4 length limit exceeded.");
        } while (value == 255);
        return length;
    }
}
