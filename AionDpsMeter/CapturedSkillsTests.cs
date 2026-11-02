using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class CapturedSkillsTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var engine = new CombatEngine();
        engine.Apply(new("network", Network: new("skill-levels", 1, "test", JsonSerializer.SerializeToElement(new SkillLevelReading(true, [new(17010000, 13)])))));
        engine.Apply(new("local-player", Actor: new(1, "Player", true, false, 0, "Cleric", 1)));
        check(engine.Fight.Actors[1].CapturedSkills?.Levels.Single().Level == 13, "Skill lists received before local identity attach when login identifies the character");
        void Update(string kind, long time, object value) => engine.Apply(new("network", Network: new(kind, time, "test", JsonSerializer.SerializeToElement(value))));
        Update("skill-choices", 10, new { Skills = new SkillChoiceValue[] { new(17010000, [1, 3]) } });
        var unknown = CapturedSkills.Build(new Actor(2, "Unknown", true, false, 0, "Cleric", 1) { CapturedSkills = new(-1, 10, false, true, [], [new(17010000, [1])]) });
        check(unknown is { Complete: false } partial && partial.Skills.Single().Level == 0,
            "Specializations arriving before levels remain unknown rather than guessing level one");
        Update("skill-levels", 11, new SkillLevelReading(true, [new(17010000, 13), new(17020000, 10)]));
        var before = engine.Snapshot();
        Update("skill-levels", 12, new SkillLevelReading(false, [new(17010000, 14)]));
        Update("skill-levels", 9, new SkillLevelReading(true, []));
        check(FightMetadata.BuildFor(engine.Fight, 1) is { Complete: true } build && build.Skills.Single().Level == 14 &&
            build.Skills.Single().Choices.SequenceEqual([1, 3]) && FightMetadata.BuildFor(before, 1)!.Skills.Single().Level == 13 && engine.Fight.Network.Count == 0,
            "Level deltas preserve specialization slots, reject stale snapshots and retain no raw skill events");
        var store = new FightStore(Path.Combine(temp, "captured-skills")); store.Save(engine.Snapshot());
        var saved = store.Load(Path.Combine(store.Root, engine.Fight.Id + ".json"));
        check(FightMetadata.BuildFor(saved, 1)!.Skills.Single().Level == 14, "Captured skills survive fight persistence");
        bool rejected = false;
        try { Update("skill-levels", 13, new SkillLevelReading(false, [new(17010000, 1), new(17010000, 2)])); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected && FightMetadata.BuildFor(engine.Fight, 1)!.Skills.Single().Level == 14, "Duplicate imported skill updates fail without changing state");
        Update("skill-choices", 14, new { Skills = Array.Empty<SkillChoiceValue>() });
        check(FightMetadata.BuildFor(engine.Fight, 1) is { Complete: false, Skills.Length: 0 }, "Empty specialization snapshot clears old choices");
        engine.Apply(new("build", Build: new(1, [new(17010000, 7, [])], true, "Manual")));
        check(FightMetadata.BuildFor(engine.Fight, 1)!.Source == "Manual", "Explicit builds take precedence over captured skills");
        Update("local-zone", 20, new { MapId = 1010, AwaitingIdentity = true });
        Update("skill-levels", 21, new SkillLevelReading(true, [new(20010000, 9)]));
        Update("skill-choices", 21, new { Skills = new SkillChoiceValue[] { new(20010000, [2]) } });
        check(engine.Fight.Actors[1].CapturedSkills!.Levels.All(s => s.Skill != 20010000),
            "New character skill lists during map preparation cannot overwrite the previous character");
        Update("local-zone", 19, new { MapId = 1010, AwaitingIdentity = true });
        Update("skill-levels", 19, new SkillLevelReading(true, []));
        engine.Apply(new("local-player", Actor: new(2, "Second", true, false, 0, "Assassin", 1)));
        check(FightMetadata.BuildFor(engine.Fight, 2) is { Complete: true } second && second.Skills.Single().Skill == 20010000,
            "New identity receives its pending complete build, ignoring stale boundary and skill packets");
        Update("local-zone", 30, new { MapId = 1010, AwaitingIdentity = true });
        engine.Apply(new("local-player", Actor: new(2, "Second", true, false, 0, "Assassin", 1)));
        check(FightMetadata.BuildFor(engine.Fight, 2)!.Skills.Single().Skill == 20010000,
            "Same-character map travel without a new skill list preserves the captured build");
        Update("local-zone", 40, new { MapId = 1010, AwaitingIdentity = true });
        Update("skill-levels", 41, new SkillLevelReading(true, [new(30010000, 1)]));
        Update("local-zone", 42, new { MapId = 1010, AwaitingIdentity = true });
        engine.Apply(new("local-player", Actor: new(3, "Third", true, false, 0, "Cleric", 1)));
        check(engine.Fight.Actors[3].CapturedSkills == null, "Superseded map preparation discards unassigned skill state");

        var login = new CombatEngine();
        void LoginUpdate(string kind, long time, object value) => login.Apply(new("network", Network: new(kind, time, "test", JsonSerializer.SerializeToElement(value))));
        LoginUpdate("skill-levels", 1, new SkillLevelReading(true, [new(17010000, 13)]));
        LoginUpdate("skill-choices", 2, new { Skills = new SkillChoiceValue[] { new(17010000, [1, 3]) } });
        LoginUpdate("local-zone", 3, new { MapId = 1010, AwaitingIdentity = true });
        login.Apply(new("local-player", Actor: new(1, "First", true, false, 0, "Cleric", 1)));
        check(FightMetadata.BuildFor(login.Fight, 1) is { Complete: true } first && first.Skills.Single().Level == 13,
            "Initial map preparation preserves the complete skill baseline received before identity");
        LoginUpdate("local-zone", 10, new { MapId = 1010, AwaitingIdentity = true });
        LoginUpdate("skill-levels", 11, new SkillLevelReading(true, [new(20010000, 9)]));
        LoginUpdate("skill-choices", 11, new { Skills = new SkillChoiceValue[] { new(20010000, [2]) } });
        LoginUpdate("local-zone", 10, new { MapId = 1010, AwaitingIdentity = true });
        login.Apply(new("local-player", Actor: new(2, "Second", true, false, 0, "Assassin", 1)));
        check(FightMetadata.BuildFor(login.Fight, 2) is { Complete: true } duplicate && duplicate.Skills.Single().Skill == 20010000 && login.Fight.Network.Count == 0,
            "Duplicate map preparation preserves pending skills without retaining raw packets");

        var cache = new PlayerProfileCache();
        var previous = new Actor(7, "Previous", true, false, 0, "Cleric", 1);
        cache.RememberLocal(previous);
        var cached = new CombatEngine(profiles: cache);
        cached.Apply(new("actor", Actor: previous));
        cached.Apply(new("network", Network: new("skill-levels", 1, "test", JsonSerializer.SerializeToElement(new SkillLevelReading(true, [new(20010000, 9)])))));
        check(cached.Fight.LocalPlayer == 7 && cached.Fight.Actors[7].CapturedSkills == null,
            "Cached local identity cannot claim skills received before the authoritative login");
        cached.Apply(new("network", Network: new("local-zone", 2, "test", JsonSerializer.SerializeToElement(new { MapId = 1010, AwaitingIdentity = true }))));
        cached.Apply(new("local-player", Actor: new(8, "Actual", true, false, 0, "Assassin", 1)));
        check(cached.Fight.Actors[8].CapturedSkills?.Levels.Single().Skill == 20010000,
            "Initial pending skills survive map preparation when the prior local identity was only cached");

        DecodeResult Decode(ushort op, byte[] body) {
            var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw((byte)(op >> 8), (byte)op); w.Raw(body);
            return new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, w.Bytes.ToArray()));
        }
        var chainWire = new PartyWireTests.Writer(); chainWire.Int(17010000); chainWire.Var(2); chainWire.Int(17010001); chainWire.Int(17010002);
        var chainBytes = chainWire.Bytes.ToArray();
        string chainFixtures = Path.Combine(temp, "reference-chain-synthetic.jsonl");
        using (var fixtures = new ReferencePacketFixtures(chainFixtures)) {
            foreach (int count in new[] { 0, 1, 2, 127, 128, 256 }) {
                var body = new PartyWireTests.Writer(); body.Int(17010000); body.Var(count);
                for (int i = 0; i < count; i++) body.Int(17010001 + i);
                var frame = new PartyWireTests.Writer(); frame.Var(body.Bytes.Count + 6); frame.Raw(0x18, 0x38); frame.Raw(body.Bytes.ToArray());
                fixtures.Add(new CapturedPayload(13328, 50000, frame.Bytes.ToArray()) { TimestampMs = 1 });
            }
        }
        check(File.ReadLines(chainFixtures).Count() == 6, "Chain reference fixtures cover empty, ordinary and varint-boundary child lists");
        check(Decode(0x1838, chainBytes).Events.Single().Network!.Fields.Deserialize<SkillChainReading>() is { Parent: 17010000, Skills.Length: 2 } &&
            Enumerable.Range(0, chainBytes.Length).All(n => Decode(0x1838, chainBytes[..n]).Events.Count == 0) && Decode(0x1838, [..chainBytes, 0]).Events.Count == 0,
            "Chain activation consumes the parent and child list exactly, rejecting truncations and trailers");
        var chains = new CombatEngine();
        void ChainEvent(string kind, long time, object value) => chains.Apply(new("network", Network: new(kind, time, "test", JsonSerializer.SerializeToElement(value))));
        ChainEvent("skill-chain", 1, new SkillChainReading(17010001, [17010002]));
        ChainEvent("skill-chain", 2, new SkillChainReading(17010000, [17010001]));
        ChainEvent("skill-levels", 3, new SkillLevelReading(true, [new(17010000, 13)]));
        ChainEvent("skill-choices", 4, new { Skills = new SkillChoiceValue[] { new(17010000, [1, 3]) } });
        chains.Apply(new("local-player", Actor: new(1, "Player", true, false, 0, "Cleric", 1)));
        check(FightMetadata.BuildFor(chains.Fight, 1) is { Complete: true, Skills.Length: 3 } chainBuild && chainBuild.Skills.All(s => s.Level == 13 && s.Choices.SequenceEqual([1, 3])) && chains.Fight.Network.Count == 0,
            "Nested chain skills inherit known root levels and specializations after identity without raw history");
        var originalChain = chains.Snapshot();
        ChainEvent("skill-chain", 0, new SkillChainReading(999, [17010001]));
        ChainEvent("skill-chain", 5, new SkillChainReading(17010000, []));
        check(FightMetadata.BuildFor(chains.Fight, 1)!.Skills.Length == 3, "Stale chain updates and empty activation lists do not remove known aliases");
        ChainEvent("skill-chain", 6, new SkillChainReading(17010002, [17010001]));
        check(FightMetadata.BuildFor(chains.Fight, 1)!.Skills.Length == 1 && FightMetadata.BuildFor(originalChain, 1)!.Skills.Length == 3,
            "Cyclic chain aliases cannot fabricate a loadout or mutate earlier fight snapshots");
        var chainStore = new FightStore(Path.Combine(temp, "chain-skills")); chainStore.Save(originalChain);
        check(FightMetadata.BuildFor(chainStore.Load(Path.Combine(chainStore.Root, originalChain.Id + ".json")), 1)!.Skills.Length == 3,
            "Compact chain inheritance survives fight persistence");
        foreach (ushort op in new ushort[] { 0x0051, 0x0f38, 0x1338 }) {
            var w = new PartyWireTests.Writer(); if (op != 0x0f38) w.Var(1); if (op == 0x1338) w.Int(17010000);
            w.Raw(op == 0x0051 ? (byte)15 : (byte)1); w.Int(17010000); w.Raw(13, 10); w.Zero(5);
            if (op == 0x0051) { w.Var(123); w.Int(456); w.Raw(2); w.Var(789); } else w.Raw(0);
            var bytes = w.Bytes.ToArray();
            check(Decode(op, bytes).Events.Single().Network!.Fields.Deserialize<SkillLevelReading>()!.Skills.Single().Level == 13 &&
                Enumerable.Range(0, bytes.Length).All(n => Decode(op, bytes[..n]).Events.Count == 0) && Decode(op, [..bytes, 0]).Events.Count == 0,
                $"Skill opcode {op:X4} consumes optional fields and rejects all truncations and trailers");
        }
        var choice = new PartyWireTests.Writer(); choice.Var(1); choice.Int(17010000); choice.Var(3);
        choice.Raw(3); choice.Int(9999); choice.Raw(1); choice.Int(5555); choice.Raw(2); choice.Int(0);
        var data = choice.Bytes.ToArray();
        check(Decode(0x0039, data).Events.Single().Network!.Fields.GetProperty("Skills").Deserialize<SkillChoiceValue[]>()!.Single().Choices.SequenceEqual([1, 3]) &&
            Enumerable.Range(0, data.Length).All(n => Decode(0x0039, data[..n]).Events.Count == 0) && Decode(0x0039, [..data, 0]).Events.Count == 0,
            "Specialization choices use occupied slot numbers, not part IDs, and require a complete packet");
    }
}
