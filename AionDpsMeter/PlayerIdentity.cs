using System.Globalization;
using System.IO;
using System.Text;

namespace AionDpsMeter.Local;

// These are bounded wire layouts, not searches for arbitrary text inside packets.
// See THIRD-PARTY-NOTICES.txt for public format references.
internal static class PlayerIdentity
{
    static readonly UTF8Encoding Utf8 = new(false, true);

    internal static string Name(ref WireReader reader)
    {
        int length = reader.Byte();
        if (length is < 1 or > 72) throw new InvalidDataException("Invalid player name length.");
        string name = Utf8.GetString(reader.Take(length)).TrimEnd('\0');
        if (name.Length is < 1 or > 24 || !name.EnumerateRunes().Any(Rune.IsLetter) ||
            name.EnumerateRunes().Any(r => !Rune.IsLetterOrDigit(r) &&
                Rune.GetUnicodeCategory(r) is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark) &&
                r.Value is not ('_' or '-' or '\'' or '’')))
            throw new InvalidDataException("Invalid player name characters.");
        return name;
    }

    internal static Actor Spawn(ref WireReader reader)
    {
        int actor = reader.Var();
        if (actor <= 0) throw new InvalidDataException("Invalid player spawn ID.");
        reader.Take(4);
        if (reader.Byte() != 7) throw new InvalidDataException("Unknown player spawn header.");
        string name = Name(ref reader);
        uint classCode = reader.U32(); int faction = reader.Byte();
        if (classCode is < 4 or > 39 || faction is not (1 or 2)) throw new InvalidDataException("Invalid player spawn identity fields.");
        return new(actor, name, true, false, 0, "Unknown", 0);
    }

    internal static Actor LocalCharacter(ref WireReader reader)
    {
        int actor = reader.Var();
        if (actor <= 0) throw new InvalidDataException("Invalid local player ID.");
        // EU captures place the name after five flag bytes. The profile code
        // and faction differ from other regions; do not hard-code an Elyos bit.
        reader.Take(5);
        string name = Name(ref reader);
        int server = reader.U16(); uint profileCode = reader.U32();
        int faction = reader.Byte(); uint level = reader.U32();
        if (server is < 1000 or > 9999 || profileCode is < 4 or > 39 ||
            faction is not (1 or 2) || level is < 1 or > 200)
            throw new InvalidDataException("Invalid local character identity fields.");
        int cp = LocalCombatPower(reader.Take(reader.Remaining));
        return new(actor, name, true, false, cp, "Unknown", server) {
            CpSource = cp > 0 ? "Local character packet" : "Unknown"
        };
    }
    internal static int LocalCombatPower(ReadOnlySpan<byte> tail)
    {
        // EU 33 36: current/peak CP are adjacent uint64 values, 73 bytes from
        // the record end in verified captures. Unknown layouts stay unavailable;
        // do not scan arbitrary packet numbers for a plausible combat power.
        if (tail.Length < 73) return 0;
        var fields = new WireReader(tail[^73..]);
        ulong current = fields.U64(), peak = fields.U64();
        return current is >= 10000 and <= 2000000 && peak >= current && peak <= 2000000 ? (int)current : 0;
    }
}
