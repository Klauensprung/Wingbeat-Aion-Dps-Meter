using System.IO;

namespace AionDpsMeter.Local;

internal sealed record CharacterStateReading(float X, float Y, float Z, long Hp, long MaximumHp,
    int Mp, int MaximumMp, int Sp, int MaximumSp, int Fp, int MaximumFp, int WingId = 0, int WingSkinId = 0, float Direction = 0)
{
    internal Dictionary<byte, long> Resources() => new Dictionary<byte, long> {
        [1] = Mp, [8] = MaximumMp, [3] = Sp, [10] = MaximumSp, [6] = Fp, [13] = MaximumFp
    }.Where(p => p.Value >= 0).ToDictionary();
}
internal static class CharacterStateWire
{
    internal static CharacterStateReading Read(ref WireReader r)
    {
        ushort state = r.U16(); float x = Float(ref r), y = Float(ref r), z = Float(ref r);
        float direction = Float(ref r); r.Take(2); // Movement direction.
        if ((state & 1) != 0) r.Take(12);
        if ((state & 2) != 0) r.Take(4);
        if ((state & 4) != 0) r.Take(2);
        if ((state & 8) != 0) r.Take(12);
        if ((state & 16) != 0) r.Take(12);
        if ((state & 32) != 0) r.Take(4);
        r.Byte(); long hp = HitWire.Number(ref r), maximum = HitWire.Number(ref r);
        int mp = unchecked((int)r.U32()), maxMp = unchecked((int)r.U32()); r.Take(8);
        int sp = unchecked((int)r.U32()), maxSp = unchecked((int)r.U32()); r.Take(20);
        int fp = unchecked((int)r.U32()), maxFp = unchecked((int)r.U32());
        if ((state & 64) != 0) r.Byte();
        if ((state & 128) != 0) r.Take(4);
        if ((state & 256) != 0) r.Byte();
        if ((state & 512) != 0) r.Byte();
        if ((state & 1024) != 0) r.Var();
        if ((state & 2048) != 0) r.Take(4);
        int wing = (state & 4096) != 0 ? checked((int)r.U32()) : 0;
        if ((state & 8192) != 0) r.Byte();
        int wingSkin = (state & 16384) != 0 ? checked((int)r.U32()) : 0;
        if ((state & 32768) != 0) r.Take(4);
        if (hp < 0 || maximum < 0) throw new InvalidDataException("Invalid character health.");
        return new(x, y, z, hp, maximum, mp, maxMp, sp, maxSp, fp, maxFp, wing, wingSkin, direction);
    }
    static float Float(ref WireReader r) { float n = BitConverter.Int32BitsToSingle(unchecked((int)r.U32())); if (!float.IsFinite(n)) throw new InvalidDataException("Invalid character position."); return n; }
}
