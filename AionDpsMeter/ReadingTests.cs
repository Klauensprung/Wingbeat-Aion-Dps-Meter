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
        engine.NewFight("Next"); check(engine.Fight.LocalStats!.Values[19] == 1234, "Inactivity splits retain the latest local stat snapshot");
        engine.Apply(new("player-stats", Stats: new(999, new() { [19] = 1 })));
        check(engine.Fight.LocalStats!.Values[19] == 1234, "Older stat samples cannot overwrite newer readings");
        engine.Apply(new("local-player", Actor: new(1, "NewCharacter", true, false, 0, "Unknown", 2301)));
        check(engine.Fight.LocalStats == null, "Scene or character changes clear local stats instead of attributing another character's readings");
        var store = new FightStore(Path.Combine(temp, "readings")); store.Save(saved);
        check(store.Load(Path.Combine(store.Root, saved.Id + ".json")).LocalStats!.Values[19] == 1234 && JsonSerializer.Deserialize<Fight>("{}")!.LocalStats == null,
            "Stat snapshots persist and older saved fights remain readable");
        byte[] cooldown = Frame(0x47, 0x38, [2, ..BitConverter.GetBytes(17100000u), ..Var(3500), ..BitConverter.GetBytes(17010000u), 0]);
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
            "Short death records remain distinct from long identity records on the shared EU opcode");
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
}
