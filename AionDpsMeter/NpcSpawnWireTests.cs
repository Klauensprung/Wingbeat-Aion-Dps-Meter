using System.Text;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class NpcSpawnWireTests
{
    static byte[] Var(int value) { var b = new List<byte>(); do { b.Add((byte)((value & 127) | (value >= 128 ? 128 : 0))); value >>= 7; } while (value != 0); return b.ToArray(); }
    internal static byte[] Body(int entity, int npc, string name = "", bool all = false, bool effect = false, byte reason = 0)
    {
        byte[] text = Encoding.UTF8.GetBytes(name);
        var b = new List<byte>(); b.AddRange(Var(entity)); b.AddRange(BitConverter.GetBytes(all ? ushort.MaxValue : reason != 0 ? (ushort)4 : (ushort)0));
        b.Add((byte)(all ? 31 : text.Length > 0 ? 1 : 0));
        if (all || text.Length > 0) { b.AddRange(Var(text.Length)); b.AddRange(text); }
        b.AddRange(BitConverter.GetBytes(npc)); if (all) b.AddRange(new byte[8]);
        b.AddRange(BitConverter.GetBytes(all ? ushort.MaxValue : reason != 0 ? (ushort)4 : (ushort)0)); b.AddRange(new byte[18]);
        if (all) b.AddRange(new byte[46]);
        b.Add(1); b.AddRange(Var(50000)); b.AddRange(Var(100000)); b.AddRange(new byte[52]);
        if (all) b.AddRange(new byte[25]);
        b.Add((byte)(effect ? 1 : 0));
        if (effect) {
            b.AddRange([3, 7]); b.AddRange(BitConverter.GetBytes(1000));
            b.AddRange(BitConverter.GetBytes(5000L)); b.AddRange(BitConverter.GetBytes(0L));
            b.AddRange([42, 1]); b.AddRange(BitConverter.GetBytes(17090000)); b.Add(0);
        }
        if (all) {
            b.AddRange(new byte[2]); b.Add(reason); b.Add(7); // aggressive + paused + activated share this byte.
            b.AddRange(BitConverter.GetBytes(42)); b.Add(0); b.AddRange(new byte[4]);
            b.AddRange(new byte[8]); b.Add(0); b.AddRange(new byte[17]); b.Add(0);
        }
        if (!all && reason != 0) b.Add(reason);
        b.Add(0); b.AddRange(BitConverter.GetBytes(50));
        if (all) {
            b.AddRange(new byte[12]); b.Add(0); b.Add(2); // Waypoints and empty guard fields.
            b.AddRange(new byte[25]);
        }
        b.Add(0);
        if (all) { b.AddRange(BitConverter.GetBytes(5000000000L)); b.AddRange(new byte[4]); }
        return b.ToArray();
    }
    internal static void Run(Action<bool, string> check)
    {
        DecodeResult Decode(byte[] body) => new EuGameDecoder().Decode(new CapturedPayload(13328, 50000,
            [..Var(body.Length + 6), 0x41, 0x36, ..body]) { TimestampMs = 1000 });
        var minimal = Decode(Body(1, 2000001));
        check(minimal.Events.Single(e => e.Network?.Kind == "hp").Network!.Fields.GetProperty("MaximumHp").GetInt64() == 100000,
            "NPC spawn supplies health maxima without needing a later point update");
        byte[] full = Body(1, 2000001, new string('x', 130), true);
        var result = Decode(full);
        check(result.Events.Single(e => e.Network?.Kind == "npc-spawn").Network!.Fields.GetProperty("OwnerId").GetInt32() == 42 &&
            result.Events.Single(e => e.Network?.Kind == "guard").Network!.Fields.GetProperty("Activated").GetBoolean(),
            "NPC spawn handles every appearance/state option and shared booleans across nested fields");
        check(Enumerable.Range(0, full.Length).All(n => Decode(full[..n]).Events.Count == 0) && Decode([..full, 0]).Events.Count == 0,
            "NPC spawn truncations and trailers cannot partially publish identity, health or effects");
        var buff = Decode(Body(1, 2000001, effect: true)).Events.Single(e => e.Network?.Kind == "abnormal-apply").Network!.Fields;
        check(buff.GetProperty("SourceSkillId").GetInt32() == 17090000 && buff.GetProperty("CasterId").GetInt32() == 42 &&
            buff.GetProperty("DurationMs").GetInt64() == 5000,
            "Initial NPC effects feed the same source attribution and duration normalization as later updates");
    }
}
