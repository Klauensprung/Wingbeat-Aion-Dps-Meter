using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class ReadingTests
{
    static byte[] Var(int n) { var b = new List<byte>(); do { b.Add((byte)((n & 127) | (n >= 128 ? 128 : 0))); n >>= 7; } while (n != 0); return b.ToArray(); }
    static byte[] Frame(byte a, byte b, params byte[] body) => [..Var(body.Length + 6), a, b, ..body];
    static CapturedPayload Payload(byte[] data) => new(13328, 50000, data) { Connection = "readings-fixture", TimestampMs = 1000 };
    static byte[] Entry(ushort id, int value) => [..BitConverter.GetBytes(id), ..BitConverter.GetBytes(value)];
    internal static void Run(Action<bool, string> check, string temp)
    {
        byte[] stats = Frame(0x49, 0x36, [0, 0, 3, ..Entry(19, 1234), ..Entry(425, 1250), ..Entry(65000, -20), ..new byte[8]]);
        var decoder = new EuGameDecoder(); var events = decoder.Decode(Payload(stats)).Events;
        var value = events.Single(e => e.Stats != null).Stats!;
        check(value.Values.Count == 3 && value.Values[65000] == -20 && CharacterReadings.Rows(value).Single(r => r.StatId == 425).Value.EndsWith("%"),
            "Stats retain unknown IDs and signed raw values while formatting known basis-point fields");
        bool splits = true;
        for (int i = 1; i < stats.Length; i++) {
            var split = new EuGameDecoder(); splits &= split.Decode(Payload(stats[..i])).Events.Count == 0 && split.Decode(Payload(stats[i..])).Events.Count == 2;
        }
        check(splits, "Player stat lists publish atomically across every TCP split");
        check(new[] {
            Frame(0x49, 0x36, [0, 0, 2, ..Entry(19, 1), ..Entry(19, 2)]),
            Frame(0x49, 0x36, [0, 0, 2, ..Entry(19, 1)]),
            Frame(0x49, 0x36, [0, 0, 1, ..Entry(19, 1), 99]),
            Frame(0x49, 0x36, [0, 0, ..Var(2049)])
        }.All(f => new EuGameDecoder().Decode(Payload(f)).Events.Count == 0), "Duplicate, truncated, oversized and unknown-tail stat lists reject without partial updates");
        var engine = new CombatEngine(); foreach (var e in events) engine.Apply(e);
        var saved = engine.Snapshot(); value.Values[19] = 9;
        check(saved.LocalStats!.Values[19] == 1234 && engine.Fight.LocalStats!.Values[19] == 1234, "Stat ingestion and snapshots do not share mutable dictionaries");
        engine.NewFight("Next"); check(engine.Fight.LocalStats!.Values[19] == 1234, "Manual fight resets retain the latest local stat snapshot");
        engine.Apply(new("player-stats", Stats: new(999, new() { [19] = 1 })));
        check(engine.Fight.LocalStats!.Values[19] == 1234, "Older stat samples cannot overwrite newer readings");
        engine.Apply(new("local-player", Actor: new(1, "NewCharacter", true, false, 0, "Unknown", 2301)));
        check(engine.Fight.LocalStats != null, "Late first identity preserves current stat readings");
        engine.Apply(new("local-player", Actor: new(2, "OtherCharacter", true, false, 0, "Unknown", 2301)));
        check(engine.Fight.LocalStats == null, "Scene or character changes clear local stats instead of attributing another character's readings");
        var store = new FightStore(Path.Combine(temp, "readings")); store.Save(saved);
        check(store.Load(Path.Combine(store.Root, saved.Id + ".json")).LocalStats!.Values[19] == 1234 && JsonSerializer.Deserialize<Fight>("{}")!.LocalStats == null,
            "Stat snapshots persist and older saved fights remain readable");
        byte[] cast = Frame(0x02, 0x38, [..Var(101), 0, ..BitConverter.GetBytes(17060340u), 43, 2, ..new byte[8]]);
        var castResult = new EuGameDecoder().Decode(Payload(cast)).Events.Single().Network!;
        check(castResult.Kind == "skill-use" && castResult.Fields.GetProperty("ActorId").GetInt32() == 101 && castResult.Fields.GetProperty("SkillCode").GetInt32() == 17060340 &&
            new EuGameDecoder().Decode(Payload(Frame(0x02, 0x38, [..Var(101), 0, ..BitConverter.GetBytes(17060340u), 43]))).Events.Count == 0,
            "EU skill-use prefixes retain caster, variant and sequence and reject truncated records");
        byte[] cooldown = Frame(0x47, 0x38, [2, ..BitConverter.GetBytes(17100000u), ..Var(3500), ..BitConverter.GetBytes(17010000u), 0]);
        CheckCooldownTiming(check, temp, cast, cooldown);
        check(new EuGameDecoder().Decode(Payload(cooldown)).Events.Count == 2 && new EuGameDecoder().Decode(Payload(Frame(0x47, 0x38, [2, ..BitConverter.GetBytes(17100000u), 1]))).Events.Count == 0,
            "Cooldown lists retain milliseconds and zero resets; truncated batches publish nothing");
        var cdEvents = new EuGameDecoder().Decode(Payload(cooldown)).Events.Select(e => e.Network!).ToList();
        cdEvents.Add(new("skill-cooldown", 2000, "47 38", JsonSerializer.SerializeToElement(new { SkillCode = 17100000, RemainingMs = 0 })));
        cdEvents.Add(new("skill-cooldown", 3000, "47 38", JsonSerializer.SerializeToElement(new { SkillCode = "invalid", RemainingMs = "invalid" })));
        check(CharacterReadings.Cooldowns(cdEvents, new SkillNames()).Single(r => r.SkillCode == 17100000).RemainingMs == 0,
            "Cooldown view uses latest values and safely ignores malformed imported metadata");
        byte[] hp = Frame(0, 0x8d, [..Var(12), 2, 1, 0, ..BitConverter.GetBytes(5000000000UL)]);
        check(new EuGameDecoder().Decode(Payload(hp)).Events.Single().Network!.Fields.GetProperty("CurrentHp").GetUInt64() == 5000000000UL,
            "HP readings preserve all 64 bits instead of silently truncating large health values");
        check(new EuGameDecoder().Decode(Payload(Frame(4, 0x8d, [..Var(12)]))).Events.Single().Network?.Kind == "entity-death",
            "Short death records identify the deceased entity");
        var linking = new CombatEngine(new EuGameDecoder());
        linking.Process(Payload(Frame(0x20, 0x36, [0, 0, ..Var(12), ..new byte[4], ..BitConverter.GetBytes(9001u)])));
        linking.Apply(new("party-profiles", Profiles: [new("Member", 2301, 33000, 0) { CharacterId = 9001 }]));
        check(linking.Fight.Actors[12] is { Player: true, Name: "Member", Cp: 33000, CharacterId: 9001 } && !linking.Fight.Actors.ContainsKey(9001),
            "Explicit global-to-entity links resolve party CP and names without treating database IDs as combat IDs");
        linking.Apply(new("party-profiles", Profiles: [new("Member", 2301, 33000, 0) { CharacterId = 9001 }, new("Other", 2302, 44000, 0) { CharacterId = 9001 }]));
        check(linking.Fight.Actors[12].Cp == 33000, "Linked identities retain server constraints when global ID low bits collide");
        var unknown = new CombatEngine(); unknown.Apply(new("character-link", Link: new(50, 9001)));
        unknown.Apply(new("party-profiles", Profiles: [new("Member", 2301, 33000, 0) { CharacterId = 9001 }, new("Other", 2302, 44000, 0) { CharacterId = 9001 }]));
        check(unknown.Fight.Actors[50].Cp == 0, "Ambiguous global links never choose a CP from another server");
    }

    static void CheckCooldownTiming(Action<bool, string> check, string temp, byte[] cast, byte[] cooldown)
    {
        var names = new SkillNames(Path.Combine(temp, "no-local-timing-overrides.json"), true);
        var tracker = new CooldownTracker(); var engine = new CombatEngine(new EuGameDecoder());
        engine.Apply(new("local-player", Actor: new(101, "Timing fixture", true, true, 0, "Cleric", 1)));
        engine.Process(Payload(cast) with { TimestampMs = 10000 });
        engine.Process(Payload(cooldown) with { TimestampMs = 10000 });
        tracker.Observe(engine.Snapshot(), 12000, 50000);
        check(tracker.Reading(17060000, 50000, names, null) == (43d, true) && tracker.Remaining(17100000, 50000, names) == 1.5,
            "Decoder and engine retain capture time: two seconds of processing delay shorten both cast and reported cooldowns");
        tracker.Observe(engine.Snapshot(), 14000, 52000);
        check(tracker.Remaining(17100000, 52000, names) == 0 && tracker.Reading(17060000, 52000, names, null) == (41d, true),
            "Already expired packet cooldowns show ready without adding UI refresh delay");
        var tcp = new TcpAssembler(); var decoder = new EuGameDecoder();
        byte[] gap = Frame(0x47, 0x38, [0]);
        tcp.PushTimed(100, [], 9000, 49000).ToArray();
        check(!tcp.PushTimed((uint)(100 + gap.Length), cooldown, 10000, 50000).Any() && tcp.HasPending,
            "Out-of-order cooldown packet waits for its missing TCP prefix");
        tcp.PushTimed((uint)(100 + gap.Length), cooldown, 11000, 51000).ToArray();
        var chunks = tcp.PushTimed(100, gap, 12000, 52000).ToArray();
        var fight = new Fight();
        foreach (var chunk in chunks)
            fight.Network.AddRange(decoder.Decode(Payload(chunk.Data) with { TimestampMs = chunk.TimestampMs, CounterTimestampMs = chunk.CounterTimestampMs }).Events.Select(e => e.Network!));
        tracker.Reset(); tracker.Observe(fight, 12000, 52000);
        check(chunks.Length == 2 && chunks[1].TimestampMs == 10000 && chunks[1].CounterTimestampMs == 50000 &&
            tracker.Remaining(17100000, 52000, names) == 1.5 && !tcp.HasPending,
            "Reassembly and retransmissions preserve original packet time instead of extending cooldowns across a TCP gap");
        var wrap = new TcpAssembler(); wrap.PushTimed(uint.MaxValue - 1, [1, 2], 100, 1000).ToArray();
        wrap.PushTimed(2, [5, 6], 200, 1100).ToArray();
        var wrapped = wrap.PushTimed(0, [3, 4], 300, 1200).ToArray();
        check(wrapped.SelectMany(c => c.Data).SequenceEqual(new byte[] { 3, 4, 5, 6 }) && wrapped[1].TimestampMs == 200,
            "Timed TCP reassembly retains ordering and capture times across sequence wraparound");
    }
}
