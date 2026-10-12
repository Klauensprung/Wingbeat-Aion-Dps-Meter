using System.IO;
using System.Text;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class TelemetryTests
{
    static byte[] Var(int value) { var bytes = new List<byte>(); do { bytes.Add((byte)((value & 127) | (value > 127 ? 128 : 0))); value >>= 7; } while (value != 0); return bytes.ToArray(); }
    static byte[] Frame(byte a, byte b, byte[] body) => [..Var(body.Length + 6), a, b, ..body];
    static CapturedPayload Payload(byte[] data) => new(13328, 50000, data) { Connection = "synthetic-telemetry", TimestampMs = 1700000000000 };
    static byte[] Party(bool emptySlot = false, bool trailer = false)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        w.Write(1u); w.Write((byte)0); w.Write((byte)10); w.Write(0u); w.Write((ushort)0);
        w.Write(0UL); w.Write((byte)0); w.Write((ushort)0); w.Write((byte)2);
        for (int i = 1; i <= 2; i++) {
            if (emptySlot && i == 2) {
                w.Write((byte)0); w.Write((byte)2); w.Write(0UL); w.Write((byte)0);
                w.Write(0u); w.Write(0u); w.Write(0u); w.Write((byte)0); w.Write(0UL);
                w.Write((byte)0); w.Write((byte)0); w.Write((byte)0); continue;
            }
            w.Write((byte)1); w.Write((byte)i); w.Write(((ulong)2301 << 48) | (uint)(9000 + i));
            byte[] name = Encoding.UTF8.GetBytes("Member" + i); w.Write((byte)name.Length); w.Write(name);
            w.Write(0u); w.Write(40u); w.Write(0u); w.Write(123u); w.Write((byte)0);
            w.Write((ulong)(15000 + i * 1000)); w.Write((byte)0); w.Write((byte)0); w.Write((byte)i);
        }
        if (trailer) { w.Write((byte)0); w.Write((byte)4); }
        return Frame(2, 0x97, stream.ToArray());
    }
    public static void Run(Action<bool, string> check, string temp)
    {
        byte[] Local(ulong cp, ulong peak, int trailingBytes = 57) => Frame(0x33, 0x36,
            [..Var(400), ..new byte[5], 7, ..Encoding.UTF8.GetBytes("LocalCP"),
                ..BitConverter.GetBytes((ushort)2301), ..BitConverter.GetBytes(32u), 2, ..BitConverter.GetBytes(40u),
                ..new byte[20], ..BitConverter.GetBytes(cp), ..BitConverter.GetBytes(peak), ..new byte[trailingBytes]]);
        var localFrame = Local(44000, 45000);
        var solo = new CombatEngine(new EuGameDecoder()); solo.Process(Payload(localFrame));
        solo.Apply(new("ping", Ping: new(42, 1700000000000, "Synthetic fixture")));
        var localRows = MeterPresentation.Build(solo.Snapshot(), Analysis.Totals(solo.Fight), pinLocal: true);
        check(localRows.Rows[0] is { Local: true, CombatPower: 44000, CombatPowerSource: "Local character packet", LocalPingMs: 42 },
            "Local identity packets supply CP and identify the pinned ping row without a party");
        bool localSplits = true;
        for (int split = 1; split < localFrame.Length; split++) {
            var decoder = new EuGameDecoder();
            localSplits &= decoder.Decode(Payload(localFrame[..split])).Events.Count == 0 &&
                decoder.Decode(Payload(localFrame[split..])).Events.Single().Actor?.Cp == 44000;
        }
        check(localSplits, "Local CP survives every TCP split without publishing partial identities");
        check(new[] { Local(46000, 45000), Local(0, 45000), Local(44000, 2000001), Local(44000, 45000, 58) }
            .All(bytes => new EuGameDecoder().Decode(Payload(bytes)).Events.Single().Actor is { Cp: 0, Name: "LocalCP" }),
            "Invalid or shifted CP fields preserve identity without guessing unrelated packet values");
        var beforeCpUpdate = solo.Snapshot(); solo.Process(Payload(Local(44500, 45000)));
        check(solo.Fight.Id != beforeCpUpdate.Id && solo.Fight.Actors[400].Cp == 44500 && beforeCpUpdate.Actors[400].Cp == 44000,
            "Local CP packets start a new fight, refresh CP and preserve prior snapshots");
        solo.Apply(new("party-profiles", Profiles: [new("LocalCP", 2301, 42000, 0)]));
        check(solo.Fight.Actors[400].Cp == 44500, "Party metadata cannot replace the local character's direct CP reading");
        solo.Apply(new("actor", Actor: solo.Fight.Actors[400] with { Cp = 48000, CpSource = "Entered locally" }));
        solo.Process(Payload(Local(45000, 45000)));
        check(solo.Fight.Actors[400].Cp == 48000, "Explicit manual CP survives subsequent local character packets");

        var frame = Party(); var decoded = new EuGameDecoder().Decode(Payload(frame));
        var profiles = decoded.Events.Single().Profiles!;
        check(profiles is [{ Name: "Member1", CombatPower: 16000, Server: 2301, LatencyState: 1 }, { CombatPower: 17000 }], "Structured party records decode CP independently of item level and retain latency state without inventing milliseconds");
        bool splits = true;
        for (int split = 1; split < frame.Length; split++) {
            var decoder = new EuGameDecoder();
            splits &= decoder.Decode(Payload(frame[..split])).Events.Count == 0 && decoder.Decode(Payload(frame[split..])).Events.Single().Profiles!.SequenceEqual(profiles);
        }
        check(splits, "Party telemetry survives every TCP split without partial publication");
        check(new EuGameDecoder().Decode(Payload(Party(emptySlot: true, trailer: true))).Events.Single().Profiles is [{ Name: "Member1", CombatPower: 16000 }],
            "EU party trailers and vacant slots preserve occupied members without fabricating empty identities");
        var tail = frame.Length - 1;
        // Preserve framing while truncating the last member's fields.
        var reader = new WireReader(frame); reader.Var(); reader.Take(2);
        var shortFrame = Frame(2, 0x97, frame[reader.Position..tail]);
        var invalid = new EuGameDecoder();
        check(invalid.Decode(Payload(shortFrame)).Events.Count == 0 && invalid.RejectedRecords == 1, "A malformed later party member cannot publish earlier CP values");
        var engine = new CombatEngine();
        engine.Apply(new("local-player", Actor: new(300, "Member1", true, true, 0, "Cleric", 2301)));
        engine.Apply(new("party-profiles", Profiles: profiles));
        engine.Apply(new("actor", Actor: new(301, "Member2", true, true, 0, "Unknown", 2301)));
        check(engine.Fight.Actors[300].Cp == 16000 && engine.Fight.Actors[301].Cp == 17000 && !engine.Fight.Actors.ContainsKey(9001), "Party CP links before or after identity packets without using global IDs as entity IDs");
        engine.Apply(new("actor", Actor: new(301, "Member2", true, true, 0, "Ranger", 2301)));
        check(engine.Fight.Actors[301].Cp == 17000, "Name and class updates preserve captured party CP");
        engine.Apply(new("actor", Actor: new(302, "Member2", true, false, 0, "Unknown", 2301)));
        check(engine.Fight.Actors[301].Cp == 0 && engine.Fight.Actors[302].Cp == 0, "Ambiguous duplicate identities clear a provisional party CP match");
        engine.Apply(new("actor", Actor: new(303, "Member1", true, false, 0, "Unknown", 2302)));
        check(engine.Fight.Actors[303].Cp == 0 && engine.Fight.Actors[300].Cp == 16000, "Server identity prevents CP leaking to a same-name player on another server");
        const long unix = 1700000000000, counter = 1000000;
        byte[] Ping(long clock) => Frame(3, 0x36, [0, 0, ..BitConverter.GetBytes(clock)]);
        var legacy = new EuGameDecoder().Decode(Payload(Ping(unix + 62135596800000L - 42))).Events.Single().Ping!;
        var current = new EuGameDecoder().Decode(Payload(Ping(counter + 16777216000L - 37)) with { CounterTimestampMs = counter }).Events.Single().Ping!;
        check(legacy.Milliseconds == 42 && current.Milliseconds == 37, "Clock echoes measure local ping with legacy and live counter clocks");
        check(new EuGameDecoder().Decode(Payload(Ping(counter + 16777216000L - 37))).Events.Count == 0 &&
            new EuGameDecoder().Decode(Payload(Ping(unix + 62135596800000L + 50))).Events.Count == 0,
            "Replay without a counter clock and impossible ping never fabricate a zero-ms reading");
        engine.Apply(new("ping", Ping: legacy));
        engine.RecordDamage(new() { ActorId = 300, TargetId = 999, Damage = 100, Timestamp = unix }, false);
        var fight = engine.Snapshot(); var meter = MeterPresentation.Build(fight, Analysis.Totals(fight), pinLocal: true);
        check(meter.Rows[0] is { CombatPower: 16000, LocalPingMs: 42 } && meter.Rows.Where(r => !r.Local).All(r => r.LocalPingMs == null), "CP and ping reach overlay rows without assigning local ping to other people");
        var store = new FightStore(Path.Combine(temp, "telemetry")); store.Save(fight);
        var saved = store.Load(Path.Combine(store.Root, fight.Id + ".json"));
        check(saved.LocalPing == legacy && saved.Actors[300].Cp == 16000 && saved.PartyProfiles.Length == 2, "Party CP and local ping survive saved-fight round trips");
        var old = JsonSerializer.Deserialize<Fight>("{}")!;
        check(old.LocalPing == null && old.PartyProfiles.Length == 0, "Older fight files default to unavailable telemetry");
        engine.Apply(new("party-profiles", Profiles: []));
        check(engine.Fight.Actors[300] is { Cp: 16000, CpSource: PlayerProfileCache.CachedSource } && fight.Actors[300].Cp == 16000, "Missing party CP falls back to the last known value without changing saved snapshots");
        engine.Apply(new("actor", Actor: engine.Fight.Actors[300] with { Cp = 22000, CpSource = "Entered locally" }));
        engine.Apply(new("party-profiles", Profiles: profiles));
        check(engine.Fight.Actors[300].Cp == 22000, "Manual CP overrides remain explicit and survive party metadata updates");
    }
}
