using System.IO;
using System.Text;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class WireTests
{
    static byte[] Var(int n) { var bytes = new List<byte>(); do { bytes.Add((byte)((n & 127) | (n >= 128 ? 128 : 0))); n >>= 7; } while (n != 0); return bytes.ToArray(); }
    static byte[] Frame(byte high, byte low, params byte[] fields) { byte[] body = [high, low, ..fields]; return [..Var(body.Length + 4), ..body]; }
    static CapturedPayload Payload(byte[] bytes, string connection = "test") => new(13328, 50000, bytes) { Connection = connection, TimestampMs = 1700000000000 };
    static byte[] LiteralBlock(byte[] bytes)
    {
        var block = new List<byte> { (byte)(Math.Min(15, bytes.Length) << 4) };
        if (bytes.Length >= 15) { int extra = bytes.Length - 15; while (extra >= 255) { block.Add(255); extra -= 255; } block.Add((byte)extra); }
        block.AddRange(bytes); return block.ToArray();
    }
    public static void Run(Action<bool, string> check, string temp)
    {
        byte[] hp = Frame(0, 0x8d, [..Var(300), 2, 1, 0, ..BitConverter.GetBytes(123456u)]);
        var decoded = new EuGameDecoder().Decode(Payload(hp));
        check(decoded.Events.Single().Network?.Fields.GetProperty("CurrentHp").GetUInt32() == 123456, "Wire HP field decoding");
        byte[] guard = Frame(5, 0xe0, [..Var(300), 3, 1, ..BitConverter.GetBytes(100u), ..BitConverter.GetBytes(25u)]);
        check(new EuGameDecoder().Decode(Payload(guard)).Events.Single().Network?.Fields.GetProperty("CurrentGuard").GetUInt32() == 25, "Wire guard decoding");
        byte[] hit = Frame(4, 0x38, [..Var(301), 4, 0, ..Var(300), ..BitConverter.GetBytes(11000001u), 7, 3, ..new byte[8], 0, ..Var(4321), 1]);
        var damage = new EuGameDecoder().Decode(Payload(hit)).Events.Single().Damage;
        check(damage is { ActorId: 300, TargetId: 301, SkillCode: 11000001, Damage: 4321, IsCritical: true, Timestamp: 1700000000000 }, "Wire direct hit fields and timestamp");
        foreach (int form in new[] { 5, 7 }) foreach (byte direction in new byte[] { 0, 1, 2, 3 }) {
            var flags = new byte[form == 5 ? 13 : 15]; flags[2] = direction;
            var directional = Frame(4, 0x38, [..Var(301), (byte)form, 0, ..Var(300), ..BitConverter.GetBytes(11000001u), 7, 3, ..flags, 0, ..Var(4321), 1]);
            var directionResult = new EuGameDecoder().Decode(Payload(directional)).Events.Single().Damage!;
            check(directionResult.IsBackAttack == (direction == 1) && directionResult.IsFrontAttack == (direction == 2),
                $"Wire direction form {form}, bits {direction}: only unambiguous flags classify hits");
        }
        foreach (int prefix in new[] { 0, 128, 16384 }) foreach (byte direction in new byte[] { 0, 1, 2, 3 }) {
            byte[] variableFlags = [0, ..Var(prefix), direction, ..new byte[8]];
            var variableHit = Frame(4, 0x38, [..Var(301), 6, 0, ..Var(300), ..BitConverter.GetBytes(11000001u), 7, 3, ..variableFlags, 0, ..Var(4321), 1]);
            var variableResult = new EuGameDecoder().Decode(Payload(variableHit)).Events.Single().Damage!;
            check(variableResult.Damage == 4321 && variableResult.IsBackAttack == (direction == 1) && variableResult.IsFrontAttack == (direction == 2),
                $"Variable hit direction survives prefix {prefix} and direction bits {direction}");
        }
        var directionFight = new Fight { LocalPlayer = 300 };
        var indicator = new BackAttackIndicator();
        var backHit = damage! with { IsBackAttack = true };
        directionFight.Damage.Add(backHit);
        indicator.Observe(directionFight, 0, backHit.Timestamp);
        check(indicator.Active(999) && !indicator.Active(1000), "Back hit holds the border for exactly one second");
        directionFight.Damage.Add(backHit with { IsBackAttack = false, IsFrontAttack = true });
        indicator.Observe(directionFight, 100, backHit.Timestamp);
        check(indicator.Active(999), "Front hits in a burst do not cancel the back-hit hold");
        directionFight.Damage.Add(backHit with { Timestamp = backHit.Timestamp + 800 });
        indicator.Observe(directionFight, 800, backHit.Timestamp + 800);
        indicator.Observe(directionFight, 1700, backHit.Timestamp + 1700);
        check(indicator.Active(1799) && !indicator.Active(1800), "New back hits refresh the hold; repeated snapshots do not");
        directionFight.Damage.Add(backHit with { ActorId = 301, Timestamp = backHit.Timestamp + 1800 });
        indicator.Observe(directionFight, 1800, backHit.Timestamp + 1800);
        check(!indicator.Active(1800), "Other players cannot refresh the back-hit border");
        indicator.Observe(new Fight { LocalPlayer = 300 }, 1801, backHit.Timestamp + 1801);
        check(!indicator.Active(1801), "Fight reset clears the hold immediately");
        indicator.Observe(directionFight, 5000, backHit.Timestamp + 5000);
        check(!indicator.Active(5000), "Old saved or recovered hits cannot flash the border");
        check(new EuGameDecoder(13328, new HashSet<int> { 11000001 }).Decode(Payload(hit)).Events.Single().Kind == "heal", "Explicit healing classification separates healing from damage");
        Healing(check, temp);
        NpcIdentities(check, temp);
        byte[] cast = Frame(9, 0x38, [..Var(301), 0, ..BitConverter.GetBytes(1218001u)]);
        check(new EuGameDecoder().Decode(Payload(cast)).Events.Single().Network?.Kind == "boss-cast", "Wire boss cast decoding");
        byte[] parry = Frame(6, 0x38, [..Var(301), 0xe8, 0xef, 0xb8, 0]);
        check(new EuGameDecoder().Decode(Payload(parry)).Events.Single().Defense?.Outcome == DefenseOutcome.Parry, "Wire full mitigation parry");
        byte[] name = Frame(4, 0x8d, [0, ..new byte[4], ..Var(300), 1, 0, 6, ..Encoding.UTF8.GetBytes("Tester")]);
        check(new EuGameDecoder().Decode(Payload(name)).Events.Single().Actor?.Name == "Tester", "Wire player name decoding");
        byte[] spawn = Frame(0x45, 0x36, [..Var(300), 0x15, 0x30, 0xa0, 1, 7, 7, ..Encoding.UTF8.GetBytes("TesterA"), 32, 0, 0, 0, 2, 2]);
        check(new EuGameDecoder().Decode(Payload(spawn)).Events.Single().Actor is { Id: 300, Name: "TesterA", Player: true }, "Player spawn name binds to combat ID");
        // UTF-8 byte length, not character count; this particular name occupies eight bytes.
        byte[] unicodeSpawn = Frame(0x45, 0x36, [..Var(300), 0x15, 0x30, 0xa0, 1, 7, (byte)Encoding.UTF8.GetByteCount("Téstér"), ..Encoding.UTF8.GetBytes("Téstér"), 32, 0, 0, 0, 2, 2]);
        check(new EuGameDecoder().Decode(Payload(unicodeSpawn)).Events.Single().Actor?.Name == "Téstér", "Player names decode strict UTF-8");
        byte[] character = Frame(0x33, 0x36, [..Var(302), 0x5e, 0x91, 0xc1, 0x28, 0x37, 7, ..Encoding.UTF8.GetBytes("OwnName"), ..BitConverter.GetBytes((ushort)2301), ..BitConverter.GetBytes(32u), 2, ..BitConverter.GetBytes(45u)]);
        check(new EuGameDecoder().Decode(Payload(character)).Events.Single() is { Kind: "local-player", Actor: { Id: 302, Name: "OwnName", Server: 2301 } }, "Local character identity validates name, server, class and level");
        byte[] invalidCharacter = Frame(0x33, 0x36, [..Var(302), ..new byte[5], 7, ..Encoding.UTF8.GetBytes("OwnName"), ..BitConverter.GetBytes((ushort)2301), ..BitConverter.GetBytes(0u), 1, ..BitConverter.GetBytes(45u)]);
        check(new EuGameDecoder().Decode(Payload(invalidCharacter)).Events.Count == 0, "Text without a valid character identity tuple cannot name a player");
        byte[] invalidSpawn = Frame(0x45, 0x36, [..Var(300), 0, 0, 0, 0, 8, 7, ..Encoding.UTF8.GetBytes("TesterA"), 32, 0, 0, 0, 2]);
        check(new EuGameDecoder().Decode(Payload(invalidSpawn)).Events.Count == 0, "Unknown spawn header cannot manufacture a name");
        byte[] npcAppearance = Frame(4, 0x8d, [..Var(500), ..new byte[16]]);
        var emptyAppearance = new EuGameDecoder();
        check(emptyAppearance.Decode(Payload(npcAppearance)).Events.Count == 0 && emptyAppearance.RejectedRecords == 0, "Empty NPC appearance response is not treated as a player-name error");
        var identities = new CombatEngine(new EuGameDecoder()); identities.Process(Payload(hit));
        long identityRevision = -1; var beforeIdentity = identities.SnapshotIfChanged(null, ref identityRevision);
        identities.Process(Payload(spawn)); var afterIdentity = identities.SnapshotIfChanged(beforeIdentity, ref identityRevision);
        check(afterIdentity.Name(300) == "TesterA" && afterIdentity.Damage.Single().Damage == 4321 && beforeIdentity.Name(300) != afterIdentity.Name(300), "Late identity renames existing damage without changing totals or stale snapshots");
        identities.Apply(new("actor", Actor: new(300, "TesterA", true, true, 12000, "Cleric", 2301)));
        identities.Process(Payload(spawn));
        check(identities.Snapshot().Actors[300] is { Party: true, Cp: 12000, Job: "Cleric", Server: 2301 }, "Spawn-only names do not erase previously known character metadata");
        identities.Process(Payload(character)); var scene = identities.Snapshot();
        identities.SaveCompleted(new FightStore(temp));
        check(scene.LocalPlayer == 302 && scene.Name(302) == "OwnName" && scene.Actors.ContainsKey(300) && scene.Id != afterIdentity.Id && scene.Damage.Count == 0, "First local character packet resets combat while preserving captured names");
        identities.Process(Payload(spawn)); identities.Process(Payload(hit)); identities.Process(Payload(character));
        check(identities.Snapshot().Id != scene.Id && identities.Snapshot().Name(300) == "TesterA" && identities.Snapshot().Damage.Count == 0, "Repeated local character packets reset combat even for an unchanged identity");
        bool identitySplits = true;
        foreach (var frame in new[] { spawn, character })
        for (int split = 1; split < frame.Length; split++)
        {
            var identityDecoder = new EuGameDecoder();
            identitySplits &= identityDecoder.Decode(Payload(frame[..split])).Events.Count == 0 && identityDecoder.Decode(Payload(frame[split..])).Events.Count == 1;
        }
        check(identitySplits, "Every split of player identity records produces exactly one identity");
        byte[] apply = Frame(0x2a, 0x38, [..Var(301), 1, 19, 2, ..BitConverter.GetBytes(1000u), ..BitConverter.GetBytes(3000u), ..new byte[4], ..BitConverter.GetBytes(1700000000000UL)]);
        check(new EuGameDecoder().Decode(Payload(apply)).Events.Single().Network?.Fields.GetProperty("DurationMs").GetUInt32() == 3000, "Wire abnormal duration");
        byte[] enrage = Frame(0x2a, 0x38, [..Var(301), 1, 17, 2, ..BitConverter.GetBytes(10000008u), ..BitConverter.GetBytes(uint.MaxValue), ..new byte[4], ..BitConverter.GetBytes(1700000000000UL)]);
        check(new EuGameDecoder().Decode(Payload(enrage)).Events.Single().Network?.Kind == "boss-enrage", "Wire known boss frenzy form");
        byte[] remove = Frame(0x2c, 0x38, [..Var(301), 1, 0, 2, 0]);
        check(new EuGameDecoder().Decode(Payload(remove)).Events.Single().Network?.Kind == "abnormal-remove", "Wire abnormal removal");
        byte[] energy = Frame(0x0b, 0x61, [1, 12, ..BitConverter.GetBytes(60000001u), ..Var(840), ..Var(120)]);
        check(new EuGameDecoder().Decode(Payload(energy)).Events.Single().Energy is { Daily: 840, Bonus: 120, FromLoginList: true }, "Wire loot energy list");
        byte[] energyUpdate = Frame(0x0c, 0x61, [0, 12, ..BitConverter.GetBytes(60000001u), ..Var(810), ..Var(100), 0]);
        check(new EuGameDecoder().Decode(Payload(energyUpdate)).Events.Single().Energy is { Daily: 810, Bonus: 100, FromLoginList: false }, "Wire loot energy update");
        byte[] dungeon = Frame(0, 0x61, [..BitConverter.GetBytes(100u), 1, ..BitConverter.GetBytes(1700000000000UL), ..BitConverter.GetBytes(1700000300000UL)]);
        check(new EuGameDecoder().Decode(Payload(dungeon)).Events.Single().Network?.Kind == "dungeon-state", "Wire dungeon state");
        byte[] result = Frame(1, 0x61, [0, ..BitConverter.GetBytes(100u), 1, 0]);
        check(new EuGameDecoder().Decode(Payload(result)).Events.Single().Network?.Fields.GetProperty("Cleared").GetBoolean() == true, "Wire dungeon result");
        byte[] combined = [..hp, ..hit, ..guard, ..cast, ..parry, ..name, ..apply, ..remove, ..energy, ..energyUpdate, ..dungeon, ..result];
        byte[] bundle = Frame(255, 255, [..BitConverter.GetBytes(combined.Length), ..LiteralBlock(combined)]);
        string baseline = JsonSerializer.Serialize(new EuGameDecoder().Decode(Payload(combined)).Events);
        check(JsonSerializer.Serialize(new EuGameDecoder().Decode(Payload(bundle)).Events) == baseline, "LZ4 bundle equals uncompressed semantic events");
        bool identical = true;
        for (int split = 1; split < bundle.Length; split++)
        {
            var decoder = new EuGameDecoder(); var first = decoder.Decode(Payload(bundle[..split])); var second = decoder.Decode(Payload(bundle[split..]));
            identical &= JsonSerializer.Serialize(first.Events.Concat(second.Events)) == baseline && decoder.PendingBytes == 0;
        }
        check(identical, "Every possible compressed record split preserves exactly the same events");
        check(Encoding.ASCII.GetString(Lz4Block.Expand([0x11, (byte)'a', 1, 0, 0x50, (byte)'b', (byte)'c', (byte)'d', (byte)'e', (byte)'f'], 11)) == "aaaaaabcdef", "LZ4 overlapping back-reference");
        bool lz4Bad = false; try { Lz4Block.Expand([0, 0, 0], 4); } catch (InvalidDataException) { lz4Bad = true; }
        check(lz4Bad, "Malformed LZ4 offset rejected");
        byte[] nested = hp;
        for (int i = 0; i < 6; i++) nested = Frame(255, 255, [..BitConverter.GetBytes(nested.Length), ..LiteralBlock(nested)]);
        var limited = new EuGameDecoder();
        check(limited.Decode(Payload(nested)).Events.Count == 0 && limited.RejectedRecords > 0, "Excessively nested compression cannot emit hidden events");
        var truncated = new EuGameDecoder();
        check(truncated.Decode(Payload(hit[..^1])).Events.Count == 0 && truncated.PendingBytes == hit.Length - 1, "Incomplete record cannot emit damage");
        var bad = new EuGameDecoder();
        check(bad.Decode(Payload([255, 255, 255, 255])).State == DecodeState.Rejected && bad.FramingErrors == 1, "Invalid length is explicitly rejected");
        var unknown = new EuGameDecoder();
        check(unknown.Decode(Payload(Frame(0x12, 0x34, [1, 2, 3]))).Events.Count == 0 && unknown.UnknownRecords == 1, "Unknown opcode cannot manufacture events");
        check(new EuGameDecoder().Decode(Payload(hit) with { SourcePort = 50000, DestinationPort = 13328 }).Events.Count == 0, "Encrypted outbound traffic never enters the EU decoder");
        var isolated = new EuGameDecoder(); isolated.Decode(Payload(hp[..3], "first")); isolated.Decode(Payload(hit, "second"));
        check(isolated.Decode(Payload(hp[3..], "first")).Events.Single().Network?.Kind == "hp", "Connection streams have independent frame buffers");
        var engine = new CombatEngine(new EuGameDecoder()); engine.Process(Payload(hp));
        var snapshot = engine.Snapshot(); var store = new FightStore(temp); store.Save(snapshot);
        check(snapshot.HasRecordedData && !snapshot.HasEvents && store.Load(Path.Combine(temp, snapshot.Id + ".json")).Network.Count == 1, "Noncombat wire events persist without affecting DPS duration");
    }

    static void NpcIdentities(Action<bool, string> check, string temp)
    {
        string catalogPath = Path.Combine(temp, "npc-names.json");
        File.WriteAllText(catalogPath, "{\"2000001\":\"Training Dummy\",\"2000002\":\"Forest Beast\"}");
        var catalog = new NpcNames(catalogPath);
        byte[] spawn = Frame(0x41, 0x36, [..Var(301), 4, 0x20, 0, ..BitConverter.GetBytes(2000001u)]);
        byte[] owner = Encoding.UTF8.GetBytes("Téstér");
        byte[] summon = Frame(0x41, 0x36, [..Var(302), 0x1f, 0, 1, (byte)owner.Length, ..owner, ..BitConverter.GetBytes(2000002u)]);
        var decoded = new EuGameDecoder(npcNames: catalog).Decode(Payload(spawn));
        check(decoded.Events.First().Actor is { Id: 301, NpcId: 2000001, Name: "Training Dummy", Player: false } &&
            decoded.Events.Last().Network?.Fields.GetProperty("NpcId").GetInt32() == 2000001,
            "NPC spawn binds the temporary entity ID to an exact catalog template and records the binding");
        check(new EuGameDecoder(npcNames: catalog).Decode(Payload(summon)).Events.First().Actor is { Name: "Forest Beast", Player: false },
            "UTF-8 summon owner names do not replace the NPC's own name or classify it as a player");
        check(new EuGameDecoder(npcNames: new NpcNames(Path.Combine(temp, "no-npcs.json"))).Decode(Payload(spawn)).Events.First().Actor?.Name == "NPC 2000001" &&
            catalog.GetName(301) == "NPC 301", "No NPC lookup by dynamic entity ID and no guessed name when the catalog is absent");
        bool splits = true;
        foreach (var frame in new[] { spawn, summon })
        for (int i = 1; i < frame.Length; i++)
        {
            var reader = new EuGameDecoder(npcNames: catalog);
            splits &= reader.Decode(Payload(frame[..i])).Events.Count == 0 && reader.Decode(Payload(frame[i..])).Events.Count == 2;
        }
        check(splits, "Every TCP split of named/unnamed NPC spawns yields one complete identity and binding");
        bool malformed = true;
        foreach (var frame in new[] {
            Frame(0x41, 0x36, [..Var(301), 4, 0x20, 0, 1, 2]),
            Frame(0x41, 0x36, [..Var(301), 4, 0x20, 2, ..BitConverter.GetBytes(2000001u)]),
            Frame(0x41, 0x36, [..Var(301), 4, 0x20, 1, 8, 65]),
            Frame(0x41, 0x36, [..Var(301), 4, 0x20, 0, ..BitConverter.GetBytes(uint.MaxValue)]) })
        {
            var reader = new EuGameDecoder(npcNames: catalog);
            malformed &= reader.Decode(Payload(frame)).Events.Count == 0 && reader.RejectedRecords == 1;
        }
        check(malformed, "Truncated NPC IDs, invalid owner fields and unknown flags cannot manufacture target names");
        var engine = new CombatEngine(new EuGameDecoder(npcNames: catalog));
        engine.RecordDamage(new() { ActorId = 300, TargetId = 301, SkillCode = 17010001, Damage = 42, Timestamp = 1000 }, false);
        long revision = -1; var before = engine.SnapshotIfChanged(null, ref revision);
        engine.Process(Payload(spawn)); var after = engine.SnapshotIfChanged(before, ref revision);
        check(after.Name(301) == "Training Dummy" && after.Damage.Single().Damage == 42 && before.Name(301) == "Entity 301" &&
            after.Id == before.Id && Analysis.Opponents(after).Length == 0, "Late NPC names update target displays without changing damage or creating PvP opponents");
        engine.Process(Payload(spawn));
        check(engine.Snapshot().Id == after.Id, "Repeated NPC spawn metadata does not split an encounter");
        engine.Process(Payload(Frame(0x41, 0x36, [..Var(301), 4, 0x20, 0, ..BitConverter.GetBytes(2000002u)])));
        var store = new FightStore(Path.Combine(temp, "npc-fights")); store.Save(engine.Snapshot());
        var saved = store.Load(Path.Combine(store.Root, after.Id + ".json"));
        check(saved.Id == after.Id && saved.Name(saved.Damage.Single().TargetId) == "Training Dummy" && saved.Actors[saved.Damage.Single().TargetId].NpcId == 2000001 && saved.Damage.Single().Damage == 42 &&
            engine.Snapshot().Name(301) == "Forest Beast", "Reused entity IDs preserve the boss fight and previous target identity in saved hits");
        engine.Apply(new("actor", Actor: new(301, "PlayerNow", true, false, 0, "Unknown", 2301)));
        check(engine.Snapshot().Actors[301] is { Player: true, NpcId: 0, Name: "PlayerNow" }, "Player identity can replace a reused NPC entity without inheriting its NPC classification");
        check(JsonSerializer.Deserialize<Actor>("{\"Id\":9,\"Name\":\"Old player\",\"Player\":true,\"Party\":false,\"Cp\":0,\"Job\":\"Unknown\",\"Server\":1}")?.NpcId == 0,
            "Saved actor records from previous versions remain compatible without NPC metadata");
    }

    static void Healing(Action<bool, string> check, string temp)
    {
        // Protocol-shaped fixtures with synthetic actor IDs, amounts and counters.
        byte[] Direct(int skill, int actor, int target, int amount, int variant = 4) => Frame(4, 0x38,
            [..Var(target), ..Var(variant), 0, ..Var(actor), ..BitConverter.GetBytes((uint)skill), 7, 2, ..new byte[8], ..Var(12345), ..Var(amount), 1, 0]);
        byte[] Tick(int flag, int skill, uint effect, int remainder, int amount) => Frame(5, 0x38,
            [..Var(301), (byte)flag, ..Var(300), ..Var(45), ..BitConverter.GetBytes(effect),
                ..((flag & 2) != 0 ? Var(remainder) : []), ..((flag & 1) != 0 ? Var(amount) : []), ..BitConverter.GetBytes((uint)skill)]);
        var direct = Direct(17100130, 300, 301, 900);
        var heal = new EuGameDecoder().Decode(Payload(direct)).Events.Single();
        check(heal is { Kind: "heal", Damage: { ActorId: 300, TargetId: 301, SkillCode: 17100130, Damage: 900, IsHeal: true } }, "Healing Light on another player retains healer, recipient and skill variant");
        check(new EuGameDecoder().Decode(Payload(Direct(17120240, 300, 300, 2100))).Events.Single() is { Kind: "heal", Damage.Damage: 2100 }, "Radiant Recovery self-heal is no longer discarded");
        check(new EuGameDecoder().Decode(Payload(Direct(17800001, 300, 300, 600))).Events.Single().Kind == "heal", "Passive Cleric recovery counts independently of direct damage");
        check(new EuGameDecoder().Decode(Payload(Direct(18170020, 300, 301, 800))).Events.Single().Kind == "heal", "Chanter healing uses the built-in classification");
        check(new EuGameDecoder().Decode(Payload(Direct(11730007, 300, 300, 75))).Events.Single().Kind == "heal" &&
            new EuGameDecoder().Decode(Payload(Direct(11120240, 300, 301, 500))).Events.Single().Kind == "damage", "Mixed recovery families do not turn offensive life-drain hits into healing");
        check(new EuGameDecoder().Decode(Payload(Direct(15760007, 300, 300, 90))).Events.Count == 0 &&
            new EuGameDecoder().Decode(Payload(Direct(16990002, 300, 301, 20))).Events.Count == 0 &&
            new EuGameDecoder().Decode(Payload(Direct(18140007, 300, 300, 20))).Events.Count == 0, "Known MP-only variants never enter healing or damage");
        check(new EuGameDecoder().Decode(Payload(Direct(16990004, 300, 300, 100000))).Events.Count == 0 &&
            new EuGameDecoder().Decode(Payload(Direct(99001234, 300, 300, 9000))).Events.Count == 0, "Summon initialization and unknown self-targeted records are not fabricated heals");
        check(new EuGameDecoder().Decode(Payload(Direct(17100130, 300, 301, 2, variant: 0))).Events.Count == 0 &&
            new EuGameDecoder().Decode(Payload(Direct(17100130, 300, 301, 2, variant: 8))).Events.Count == 0, "Healing cast notices without an amount block cannot add healing");
        var tick = Tick(11, 17090000, 1709000011, 900, 100);
        var periodic = new EuGameDecoder().Decode(Payload(tick)).Events.Single();
        check(periodic is { Kind: "heal", Damage: { ActorId: 300, TargetId: 301, SkillCode: 17090000, Damage: 100 } } && periodic.Damage.AdditionalData!["Periodic"].GetBoolean(), "Regeneration uses the 100 HP tick, not its 900 remaining pool");
        var announcement = Tick(9, 17090000, 1709000011, 0, 1000);
        check(new EuGameDecoder().Decode(Payload(announcement)).Events.Count == 0, "Regeneration pool announcement is not counted again as healing");
        check(new EuGameDecoder().Decode(Payload(Tick(10, 17070000, 1707000011, 300, 0))).Events.Count == 0 &&
            new EuGameDecoder().Decode(Payload(Tick(11, 2011101, 201110112, 400, 100))).Events.Count == 0 &&
            new EuGameDecoder().Decode(Payload(Tick(11, 12750001, 1275000111, 1000, 200))).Events.Count == 0,
            "Damage ticks, ambiguous potion resources and unsupported shield pools never become healing");
        check(new EuGameDecoder().Decode(Payload(Tick(11, 17090000, 120001211, 1000, 200))).Events.Count == 0, "Unrelated monster effects cannot borrow a player's healing skill attribution");
        byte[] tickBody = tick[1..];
        var malformed = new EuGameDecoder();
        check(malformed.Decode(Payload([..Var(tickBody.Length + 5), ..tickBody, 0])).Events.Count == 0 && malformed.RejectedRecords == 1, "Recovery records with unknown trailing fields are rejected atomically");
        check(new EuGameDecoder().Decode(Payload(Tick(11, 17090000, 1709000011, 10, 0))).Events.Count == 0, "Zero recovery does not create a healing event");
        bool fragments = true;
        foreach (var frame in new[] { direct, tick })
        for (int split = 1; split < frame.Length; split++)
        {
            var decoder = new EuGameDecoder();
            fragments &= decoder.Decode(Payload(frame[..split])).Events.Count == 0 && decoder.Decode(Payload(frame[split..])).Events.Single().Kind == "heal";
        }
        check(fragments, "Direct and periodic heals survive every TCP record split exactly once");
        byte[] joined = [..announcement, ..direct, ..tick];
        byte[] compressed = Frame(255, 255, [..BitConverter.GetBytes(joined.Length), ..LiteralBlock(joined)]);
        var engine = new CombatEngine(new EuGameDecoder());
        engine.Apply(new("local-player", Actor: new(300, "Test healer", true, true, 0, "Cleric", 1)));
        engine.Apply(new("actor", Actor: new(301, "Test recipient", true, true, 0, "Unknown", 1)));
        engine.Process(Payload(compressed)); var fight = engine.Snapshot();
        var store = new FightStore(temp); store.Save(fight); var saved = store.Load(Path.Combine(temp, fight.Id + ".json"));
        check(saved.Healing.Count == 2 && saved.Healing.Sum(e => e.Damage) == 1000 && saved.Damage.Count == 0 && Analysis.Opponents(saved).Length == 0,
            "Compressed heals persist per recipient without contaminating damage or PvP");
        var meter = MeterPresentation.Build(saved, Analysis.Totals(saved), healing: true);
        check(meter.Rows.First() is { Id: 300, Amount: 1000 } && meter.Rows.Single(r => r.Id == 301).Amount == 0 && meter.Amount == 1000, "Decoded wire heals reach the live HPS meter and retain the recipient's idle party row");
    }
}
