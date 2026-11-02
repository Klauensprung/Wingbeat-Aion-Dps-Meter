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

}
