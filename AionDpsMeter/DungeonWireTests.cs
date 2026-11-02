using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class DungeonWireTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        byte[] Var(int n) { var b = new List<byte>(); do { byte v = (byte)(n & 127); n >>= 7; b.Add((byte)(v | (n == 0 ? 0 : 128))); } while (n > 0); return b.ToArray(); }
        byte[] I(int n) => BitConverter.GetBytes(n);
        byte[] L(long n) => BitConverter.GetBytes(n);
        using var fixtures = new ReferencePacketFixtures(System.IO.Path.Combine(temp, "dungeon-wire-fixtures.jsonl"));
        DecodeResult Decode(byte opcode, byte[] body) {
            var payload = new CapturedPayload(13328, 50000,
                [..Var(body.Length + 6), opcode, 0x61, ..body]) { TimestampMs = 1700000001000 };
            var decoded = new EuGameDecoder().Decode(payload);
            // Malformed-input policy is tested locally; only accepted frames
            // are compared with the independently installed packet readers.
            if (decoded.Events.Count > 0) fixtures.Add(payload);
            return decoded;
        }
        bool optional = true, bounded = true;
        for (byte flags = 0; flags < 4; flags++) {
            byte[] body = [flags, ..I(600002), 1, ..((flags & 1) != 0 ? Var(12345) : []), 2,
                ..I(110120003), ..L(6000000000), ..I(534660001), ..L(2), ..((flags & 2) != 0 ? Var(3) : [])];
            var result = Decode(1, body).Events.Single().Network!.Fields.Deserialize<DungeonResultReading>()!;
            optional &= result.Succeeded && result.MapId == 600002 && result.Rewards[0].Quantity == 6000000000 &&
                result.ContentsId == ((flags & 1) != 0 ? 12345 : null) && result.ClearLevel == ((flags & 2) != 0 ? 3 : null);
            bounded &= Enumerable.Range(0, body.Length).All(n => Decode(1, body[..n]).Events.Count == 0) && Decode(1, [..body, 0]).Events.Count == 0;
        }
        check(optional && bounded, "Dungeon results support optional contents/clear-level fields and 64-bit rewards with exact bounds");
        check(!Decode(1, [0, ..I(600002), 0, 0]).Events.Single().Network!.Fields.GetProperty("Succeeded").GetBoolean(),
            "Dungeon failure remains distinct from success");
        check(Decode(1, [4, ..I(600002), 1, 0]).Events.Count == 0 &&
            Decode(1, [0, ..I(600002), 1, 2, ..I(1), ..L(1), ..I(1), ..L(2)]).Events.Count == 0,
            "Unknown dungeon flags and duplicate reward keys reject the complete result");
        var largeRewards = Enumerable.Range(1, 128).SelectMany(i => I(i).Concat(L(1))).ToArray();
        check(Decode(1, [0, ..I(600002), 1, ..Var(128), ..largeRewards]).Events.Single().Network!.Fields.GetProperty("Rewards").GetArrayLength() == 128,
            "Dungeon reward count and frame length both support multi-byte varints");
        bool states = true;
        for (byte state = 0; state <= 4; state++) {
            var e = Decode(0, [..I(600002), state, ..L(0), ..L(0)]).Events.Single().Network!;
            states &= e.Fields.Deserialize<DungeonStateReading>() is { StartedAt: null, Deadline: null };
        }
        check(states, "Dungeon state supports none, prepare, process, complete and end without inventing missing timestamps");
        var stateEvent = Decode(0, [..I(600002), 2, ..L(1700000000000), ..L(1700000600000)]).Events.Single().Network!;
        check(stateEvent.Fields.Deserialize<DungeonStateReading>() is { StartedAt: 1700000000000, Deadline: 1700000600000 } &&
            Decode(0, [..I(600002), 5, ..L(0), ..L(0)]).Events.Count == 0 &&
            Decode(0, [..I(600002), 2, ..L(1700000600000), ..L(1700000000000)]).Events.Count == 0,
            "Dungeon deadline is retained separately from start, rejecting impossible state/timestamp combinations");
        var engine = new CombatEngine();
        var resultEvent = Decode(1, [0, ..I(600002), 1, 1, ..I(110120003), ..L(3)]).Events.Single().Network!;
        engine.Apply(new("network", Network: stateEvent)); engine.Apply(new("network", Network: resultEvent));
        var saved = engine.Snapshot().ForStorage();
        check(saved.Network.Count(e => e.Kind is "dungeon-state" or "dungeon-result") == 0 &&
            saved.DungeonRun?.ClearedAt == resultEvent.Timestamp && engine.Loot.Rows().Length == 0,
            "Saved run summary preserves completion without raw reward previews or duplicate inventory loot");
    }
}
