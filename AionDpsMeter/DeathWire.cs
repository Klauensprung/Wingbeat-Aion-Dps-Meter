using System.IO;
using System.Text;

namespace AionDpsMeter.Local;

internal sealed record DeathReading(int EntityId, int SkillCode, int KillerId, ushort ServerId,
    string KillerName, int NpcId, int ActionId, byte Reason, byte DieType);
internal static class DeathWire
{
    static readonly UTF8Encoding Utf8 = new(false, true);
    internal static DeathReading Read(ref WireReader r)
    {
        int target = r.Var(), skill = checked((int)r.U32()), killer = r.Var();
        ushort server = r.U16(); string name = Text(ref r);
        Text(ref r); // Guild name is not needed for death attribution.
        r.U16();
        int npc = r.Var(), action = unchecked((int)r.U32());
        byte reason = r.Byte(), type = r.Byte();
        if (target <= 0 || r.Remaining != 0) throw new InvalidDataException("Invalid death record.");
        return new(target, skill, killer, server, name, npc, action, reason, type);
    }
    static string Text(ref WireReader r)
    {
        int count = r.Var();
        if (count > 4096) throw new InvalidDataException("Death text exceeds limit.");
        return Utf8.GetString(r.Take(count)).TrimEnd('\0');
    }
}
