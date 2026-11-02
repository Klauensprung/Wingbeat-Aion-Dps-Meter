using System.IO;
using System.Text;

namespace AionDpsMeter.Local;

internal sealed record LoginAppearance(string Race, string Gender, string? Data, int Wing, AppearanceEquipment[] Equipment);
internal sealed record LoginCharacter([property: System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.WriteAsString | System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)] long Id, string Name, int Template, int Level, string Guild, long CombatPower)
{
    public LoginAppearance? Appearance { get; init; }
    public int? GearScore { get; init; }
}
internal sealed record LoginReading(ushort Result, int Server, LoginCharacter[] Characters);
internal static class LoginWire
{
    internal static LoginReading Read(ref WireReader r)
    {
        ushort result = r.U16(); r.Take(16);
        int count = Count(ref r, 128); var characters = new List<LoginCharacter>(count);
        for (int i = 0; i < count; i++) {
            r.Take(4); string name = Text(ref r); int template = checked((int)r.U32()); byte race = r.Byte(), gender = r.Byte();
            int level = checked((int)r.U32()); r.Take(4); int gearScore = checked((int)r.U32()); r.Take(20); string guild = Text(ref r); r.Take(40);
            var equipment = new VisibleItem[Count(ref r, 128)];
            for (int j = 0; j < equipment.Length; j++) equipment[j] = PlayerSpawnWire.Item(ref r);
            long id = checked((long)r.U64()); r.Take(12); int wing = checked((int)r.U32()), wingSkin = checked((int)r.U32());
            var bytes = r.Take(Count(ref r, 65536));
            string? appearance = bytes.Length > 0 && bytes[0] == 12 ? Convert.ToBase64String(bytes) : null; r.Byte(); r.Bit();
            for (int j = Count(ref r, 4096); j > 0; j--) { r.Take(12); r.Bit(); r.Take(8); }
            r.Take(9); long cp = checked((long)r.U64());
            if (!string.IsNullOrEmpty(name) && !name.StartsWith('$')) characters.Add(new(id, name, template, level, guild, cp) { GearScore = gearScore, Appearance = new(race == 1 ? "light" : "dark", gender == 1 ? "male" : "female", appearance, wingSkin != 0 ? wingSkin : wing,
                equipment.Where(e => e.Display && (e.ItemId != 0 || e.SkinId != 0)).Select(e => new AppearanceEquipment(e.ItemId, e.SkinId, e.Dyes)).ToArray()) });
        }
        r.Byte(); r.Bit(); r.Bit();
        // Account session token: consume without decoding or keeping it.
        r.Take(Count(ref r, 65536));
        string serverKey = Text(ref r); r.Take(Count(ref r, 4096));
        r.Bit(); r.Byte(); r.Bit(); r.Var(); r.Bit(); r.Byte();
        if (r.Remaining != 0) throw new InvalidDataException("Unknown login trailer.");
        int server = int.TryParse(serverKey, out int parsed) && parsed > 0 ? parsed : 0;
        return new(result, server, characters.ToArray());
    }
    static string Text(ref WireReader r) => new UTF8Encoding(false, true).GetString(r.Take(Count(ref r, 4096))).TrimEnd('\0');
    static int Count(ref WireReader r, int max) { int count = r.Var(); if (count > max) throw new InvalidDataException("Login list exceeds limit."); return count; }
}
