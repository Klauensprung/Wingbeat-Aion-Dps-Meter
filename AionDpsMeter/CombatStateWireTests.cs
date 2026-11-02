using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class CombatStateWireTests
{
    internal static void Run(Action<bool, string> check)
    {
        var decoder = new EuGameDecoder();
        DecodeResult Decode(byte a, byte b, byte[] body) => decoder.Decode(new CapturedPayload(13328, 50000,
            [(byte)(body.Length + 6), a, b, ..body]) { TimestampMs = 1000 });
        Decode(0, 0x36, BitConverter.GetBytes(10000L));
        var state = Decode(0x21, 0x8d, [1, 1, 2, ..BitConverter.GetBytes(20000L)]).Events.Single().Network!;
        check(state.Kind == "combat-state" && state.Fields.GetProperty("InCombat").GetBoolean() &&
            state.Fields.GetProperty("BerserkAtMs").GetInt64() == 11000,
            "Combat-state packets preserve state and translate optional berserk time through the server clock");
        check(!Decode(0x21, 0x8d, [1, 0, 0]).Events.Single().Network!.Fields.GetProperty("InCombat").GetBoolean() &&
            Decode(0x21, 0x8d, [1, 1, 2, 0]).Events.Count == 0,
            "Out-of-combat state is explicit and truncated optional timing emits no partial state");
    }
}
