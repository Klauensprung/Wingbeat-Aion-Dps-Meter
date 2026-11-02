using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class DungeonProgressWireTests
{
    internal static void Run(Action<bool, string> check)
    {
        DecodeResult Decode(ushort opcode, byte[] body) => new EuGameDecoder().Decode(new CapturedPayload(13328, 50000,
            [(byte)(body.Length + 6), (byte)(opcode >> 8), (byte)opcode, ..body]));
        foreach (ushort opcode in new ushort[] { 0x2e57, 0x0961 }) {
            byte[] body = BitConverter.GetBytes(123);
            check(Decode(opcode, body).Events.Count == 1 && Enumerable.Range(0, body.Length).All(n => Decode(opcode, body[..n]).Events.Count == 0) &&
                Decode(opcode, [..body, 0]).Events.Count == 0 && Decode(opcode, BitConverter.GetBytes(-1)).Events.Count == 0,
                $"Dungeon progress opcode {opcode:X4} validates exact signed value width and rejects negative values");
        }
        check(Decode(0x0961, BitConverter.GetBytes(0)).Events.Single().Network!.Fields.GetProperty("Count").GetInt32() == 0 &&
            Decode(0x2e57, BitConverter.GetBytes(0)).Events.Count == 0, "Reported zero deaths is valid while a zero boss identity is not");
    }
}
