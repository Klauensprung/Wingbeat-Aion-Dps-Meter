using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class ZoneStateTests
{
    internal static void Run(Action<bool, string> check)
    {
        byte[] Payload(int state, ulong stamp) => [..BitConverter.GetBytes(600012u), (byte)(stamp == 0 ? 0 : 1),
            ..BitConverter.GetBytes(stamp), (byte)state, 1, 2, ..BitConverter.GetBytes(123UL)];
        DecodeResult Decode(byte[] payload) => new EuGameDecoder().Decode(new CapturedPayload(13328, 50000,
            [(byte)(payload.Length + 6), 0x01, 0x40, ..payload]) { TimestampMs = 1700000000000 });
        var decoded = Decode(Payload(2, 1700000003000)).Events.Single().Network!;
        check(decoded.Kind == "zone-state" && decoded.Fields.Deserialize<ZoneStateReading>() == new ZoneStateReading(600012, 2, 1700000003000, 2, 123),
            "Extended zone state preserves raw phase, timestamp, variant and reference without declaring a clear");
        byte[] ordinary = [..BitConverter.GetBytes(1010u), 0, ..new byte[8], 1, 0];
        check(Decode(ordinary).Events.Single().Network!.Fields.Deserialize<ZoneStateReading>() == new ZoneStateReading(1010, 1, null, null, null) &&
            Decode(Payload(1, 0)).Events.Count == 1,
            "Ordinary zone and extended untimed lobby observations remain distinct");
        check(Decode(Payload(2, 10)).Events.Count == 0 && Decode(Payload(4, 1700000003000)).Events.Count == 0 &&
            Decode(Payload(2, 1700000003000)[..^1]).Events.Count == 0 && Decode([..ordinary, 0]).Events.Count == 0,
            "Zone-state reader rejects unsupported phases, implausible timestamps and non-exact lengths");
        NetworkEvent State(int phase, long capture, long start = 1700000003000) => new("zone-state", capture, "synthetic",
            JsonSerializer.SerializeToElement(new ZoneStateReading(600012, phase, start, 2, 123)));
        var engine = new CombatEngine();
        engine.Apply(new("network", Network: State(2, 1000))); var before = engine.Snapshot();
        engine.Apply(new("network", Network: State(3, 2000)));
        for (int i = 0; i < 10000; i++) engine.Apply(new("network", Network: State(3, 3000 + i)));
        check(engine.Fight.Network.Count == 2 && engine.Fight.Network[1].Timestamp == 2000 && before.Network.Count == 1,
            "Repeated zone phases retain the earliest observation without growing history or mutating snapshots");
        string fightId = engine.Fight.Id;
        engine.Apply(new("network", Network: State(1, 14000, 1700001000000)));
        check(engine.Fight.Id == fightId && engine.Fight.Network.Count == 3,
            "Zone-state observations never reset combat or assert run completion and distinguish new server timestamps");
        for (int i = 0; i < 100; i++) engine.Apply(new("network", Network: State(2, 15000 + i, 1700002000000 + i)));
        check(engine.Fight.Network.Count == 64, "Zone-state history is bounded to 64 phase observations");
        engine.NewFight("Reset"); engine.Apply(new("network", Network: State(2, 20000)));
        check(engine.Fight.Network.Count == 1, "Combat reset releases zone-state retention indices");
    }
}
