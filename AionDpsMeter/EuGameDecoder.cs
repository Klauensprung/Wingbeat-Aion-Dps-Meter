using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

// Separate implementation based on recorded protocol facts, not a port of recovered classes.
// Partial and experimental: see the event compatibility matrix before using its totals.
public sealed class EuGameDecoder(int serverPort = 13328, IReadOnlySet<int>? healingSkills = null, NpcNames? npcNames = null) : IGamePacketDecoder
{
    readonly NpcNames npcs = npcNames ?? NpcNames.Default;
    readonly Dictionary<string, GameRecordStream> streams = new();
    readonly Dictionary<string, long> recordsByOpcode = new();
    readonly Dictionary<string, long> eventsByKind = new();
    readonly Dictionary<string, long> rejections = new();
    int expandedBudget, nestedBudget;
    public string Target => "Experimental EU decoder: direct healing and supported recovery ticks; partial event coverage";
    public long Records { get; private set; }
    public long UnknownRecords { get; private set; }
    public long RejectedRecords { get; private set; }
    public long FramingErrors { get; private set; }
    public long Bundles { get; private set; }
    public long IgnoredOutboundPayloads { get; private set; }
    public IReadOnlyDictionary<string, long> Opcodes => recordsByOpcode;
    public IReadOnlyDictionary<string, long> EventKinds => eventsByKind;
    public IReadOnlyDictionary<string, long> Rejections => rejections;
    public int PendingBytes => streams.Values.Sum(s => s.PendingBytes);

    public DecodeResult Decode(CapturedPayload payload)
    {
        if (payload.SourcePort != serverPort) { IgnoredOutboundPayloads++; return new(DecodeState.Unsupported, [], "Not the configured server-to-client direction."); }
        string key = payload.Connection == "unknown" ? $"{payload.DeviceName}:{payload.SourcePort}>{payload.DestinationPort}" : payload.Connection;
        if (!streams.TryGetValue(key, out var stream))
        {
            if (streams.Count >= 64) return new(DecodeState.Rejected, [], "Too many game connections; restart capture.");
            streams[key] = stream = new();
        }
        var events = new List<DecodedCombatEvent>();
        try
        {
            foreach (var record in stream.Push(payload.Data)) DecodeRecord(record, payload.TimestampMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), events, 0, payload.CounterTimestampMs);
        }
        catch (InvalidDataException ex) { FramingErrors++; streams.Remove(key); return new(DecodeState.Rejected, [], ex.Message); }
        foreach (var e in events) eventsByKind[e.Network?.Kind ?? e.Kind] = eventsByKind.GetValueOrDefault(e.Network?.Kind ?? e.Kind) + 1;
        return new(DecodeState.Recognized, events, "Experimental bounded record decoding.");
    }

    void DecodeRecord(byte[] record, long time, List<DecodedCombatEvent> events, int depth, long? counterTime)
    {
        if (depth == 0) { expandedBudget = 8 * GameRecordStream.Limit; nestedBudget = 32768; }
        if (depth > 4 || --nestedBudget < 0) { RejectedRecords++; return; }
        var reader = new WireReader(record);
        reader.Var(); int prefix = reader.Position;
        ushort opcode = BinaryPrimitives.ReadUInt16BigEndian(reader.Take(2));
        string hex = $"{opcode >> 8:X2} {opcode & 255:X2}";
        Records++; recordsByOpcode[hex] = recordsByOpcode.GetValueOrDefault(hex) + 1;
        var staged = new List<DecodedCombatEvent>();
        try
        {
            if (opcode is 0xffff or 0x3d38)
            {
                byte[] body;
                if (opcode == 0xffff)
                {
                    int size = checked((int)reader.U32());
                    if (size < 1 || size > expandedBudget) throw new InvalidDataException("Nested expansion budget exceeded.");
                    expandedBudget -= size; body = Lz4Block.Expand(reader.Take(reader.Remaining), size);
                }
                else body = reader.Take(reader.Remaining).ToArray();
                var inner = new GameRecordStream(); var children = inner.Push(body);
                if (inner.PendingBytes != 0) throw new InvalidDataException("Incomplete records in bundle.");
                Bundles++;
                foreach (var child in children) DecodeRecord(child, time, staged, depth + 1, counterTime);
            }
            else
            {
                switch (opcode)
                {
                    case 0x4936:
                    {
                        var stats = CharacterReadings.Stats(ref reader, time);
                        staged.Add(new("player-stats", Stats: stats));
                        Emit(staged, "player-stats", time, hex, new { stats.Values });
                        break;
                    }
                    case 0x2036:
                    {
                        reader.Take(2); int entity = Positive(reader.Var()); reader.Take(4); uint character = reader.U32();
                        if (character == 0) throw new InvalidDataException("Empty global character ID.");
                        staged.Add(new("character-link", Link: new(entity, character)));
                        Emit(staged, "character-link", time, hex, new { EntityId = entity, CharacterId = character });
                        break;
                    }
                    case 0x0238:
                    {
                        // Observed EU skill-use prefix. The target/position trailer is
                        // intentionally opaque; this is not a cooldown-duration report.
                        int actor = Positive(reader.Var());
                        if (reader.Byte() != 0) throw new InvalidDataException("Unknown skill-use prefix.");
                        uint skill = reader.U32(); int sequence = reader.Byte(), mode = reader.Byte();
                        if (skill is < 11000000 or >= 20000000 || reader.Remaining < 5 || mode is not (0 or 2))
                            throw new InvalidDataException("Unknown skill-use layout.");
                        Emit(staged, "skill-use", time, hex, new { ActorId = actor, SkillCode = (int)skill, Sequence = sequence });
                        break;
                    }
                    case 0x4738:
                    {
                        int count = reader.Byte();
                        var seen = new HashSet<uint>();
                        for (int i = 0; i < count; i++) {
                            uint skill = reader.U32(); int remaining = reader.Var();
                            if (skill is 0 or > int.MaxValue || remaining > 86400000 || !seen.Add(skill)) throw new InvalidDataException("Invalid skill cooldown.");
                            Emit(staged, "skill-cooldown", time, hex, new { SkillCode = (int)skill, RemainingMs = remaining });
                        }
                        if (reader.Remaining != 0) throw new InvalidDataException("Unknown cooldown trailer.");
                        break;
                    }
                    case 0x0297:
                        staged.Add(new("party-profiles", Profiles: PlayerTelemetry.ReadParty(ref reader)));
                        break;
                    case 0x0336:
                        if (PlayerTelemetry.ReadPing(ref reader, time, counterTime) is { } ping) staged.Add(new("ping", Ping: ping));
                        break;
                    case 0x0438: Hit(ref reader, time, staged, hex); break;
                    case 0x0538: RecoveryTick(ref reader, time, staged, hex); break;
                    case 0x4136:
                    {
                        var npc = npcs.Spawn(ref reader);
                        staged.Add(new("actor", Actor: npc));
                        Emit(staged, "npc-spawn", time, hex, new { EntityId = npc.Id, npc.NpcId, npc.Name });
                        break;
                    }
                    case 0x4536:
                        staged.Add(new("actor", Actor: PlayerIdentity.Spawn(ref reader)));
                        break;
                    case 0x3336:
                        staged.Add(new("local-player", Actor: PlayerIdentity.LocalCharacter(ref reader)));
                        break;
                    case 0x008d:
                    {
                        int entity = Positive(reader.Var());
                        if (!reader.Take(3).SequenceEqual(new byte[] { 2, 1, 0 })) throw new InvalidDataException("Unknown HP layout.");
                        ulong hp = reader.Remaining switch { 4 => reader.U32(), 8 => reader.U64(), _ => throw new InvalidDataException("Unknown HP value width.") };
                        if (hp > long.MaxValue) throw new InvalidDataException("Invalid HP value.");
                        Emit(staged, "hp", time, hex, new { EntityId = entity, CurrentHp = hp }); break;
                    }
                    case 0x0638:
                    {
                        int target = Positive(reader.Var()); uint effect = reader.U32();
                        if (effect == 0x00b8efe8) staged.Add(new("defense", Defense: new() { Timestamp = time, TargetId = target, Outcome = DefenseOutcome.Parry }));
                        else UnknownRecords++;
                        break;
                    }
                    case 0x048d:
                    {
                        int target = reader.Var();
                        if (reader.Remaining == 0) {
                            Emit(staged, "entity-death", time, hex, new { EntityId = Positive(target) }); break;
                        }
                        uint killingSkill = reader.U32(); int actor = reader.Var(); int server = reader.U16();
                        if (target > 0 && actor > 0 && killingSkill > 0)
                            Emit(staged, "entity-death", time, hex, new { EntityId = target, KillerId = actor, SkillCode = killingSkill });
                        // Empty target/appearance responses are not malformed player names.
                        if (actor == 0 || server == 0) break;
                        string name = PlayerIdentity.Name(ref reader);
                        staged.Add(new("actor", Actor: new(actor, name, true, false, 0, "Unknown", server)));
                        if (target > 0) Emit(staged, "target", time, hex, new { ActorId = actor, TargetId = target });
                        break;
                    }
                    case 0x05e0:
                    {
                        int actor = Positive(reader.Var());
                        if (!reader.Take(2).SequenceEqual(new byte[] { 3, 1 })) throw new InvalidDataException("Unknown guard layout.");
                        uint max = reader.U32(), current = reader.U32();
                        if (max is 0 or > 1000000 || current > max) throw new InvalidDataException("Invalid guard values.");
                        Emit(staged, "guard", time, hex, new { ActorId = actor, MaxGuard = max, CurrentGuard = current }); break;
                    }
                    case 0x0938:
                    {
                        int actor = Positive(reader.Var()); reader.Byte(); uint skill = reader.U32();
                        if (skill is 0 or >= 10000000) throw new InvalidDataException("Unknown cast skill.");
                        Emit(staged, "boss-cast", time, hex, new { CasterId = actor, SkillCode = skill }); break;
                    }
                    case 0x2a38: case 0x2b38: Abnormal(ref reader, opcode == 0x2b38, time, staged, hex); break;
                    case 0x2c38:
                    {
                        int target = Positive(reader.Var()); int count = reader.Byte();
                        if (count == 0) throw new InvalidDataException("Empty abnormal removal.");
                        for (int i = 0; i < count; i++)
                        {
                            int form = reader.Byte(); if (form is not (0 or 7)) throw new InvalidDataException("Unknown removal layout.");
                            int slot = reader.Var(); reader.Byte();
                            if (form == 7) { reader.Var(); reader.Take(8); }
                            Emit(staged, "abnormal-remove", time, hex, new { TargetId = target, Slot = slot });
                        }
                        if (reader.Remaining != 0) throw new InvalidDataException("Abnormal removal trailing data."); break;
                    }
                    case 0x0061:
                    {
                        if (record.Length != 24 || prefix != 1) throw new InvalidDataException("Unknown dungeon state layout.");
                        int dungeon = Positive(checked((int)reader.U32())); int state = reader.Byte(); ulong begin = reader.U64(), end = reader.U64();
                        if (state is < 1 or > 9 || begin < 1600000000000UL || end > 2100000000000UL || end < begin) throw new InvalidDataException("Invalid dungeon timestamps.");
                        Emit(staged, "dungeon-state", time, hex, new { DungeonId = dungeon, State = state, StartMs = begin, EndMs = end }); break;
                    }
                    case 0x0161:
                    {
                        if (record.Length != 10 || reader.Byte() != 0) throw new InvalidDataException("Unknown dungeon result layout.");
                        int dungeon = Positive(checked((int)reader.U32())); int cleared = reader.Byte();
                        if (cleared > 1 || reader.Byte() != 0) throw new InvalidDataException("Invalid dungeon result.");
                        Emit(staged, "dungeon-result", time, hex, new { DungeonId = dungeon, Cleared = cleared == 1 }); break;
                    }
                    case 0x0b61: case 0x0c61: Energy(ref reader, opcode == 0x0b61, time, staged); break;
                    default: UnknownRecords++; break;
                }
            }
            events.AddRange(staged);
        }
        catch (Exception ex) when (ex is InvalidDataException or OverflowException or DecoderFallbackException)
        {
            RejectedRecords++; string reason = hex + ": " + ex.Message; rejections[reason] = rejections.GetValueOrDefault(reason) + 1;
        }
    }

    static int Positive(int id) => id > 0 ? id : throw new InvalidDataException("Expected positive ID.");
    static void Emit(List<DecodedCombatEvent> events, string kind, long time, string opcode, object fields) =>
        events.Add(new("network", Network: new(kind, time, opcode, JsonSerializer.SerializeToElement(fields))));

    bool IsHealing(int skill, int actor, int target)
    {
        if (HealingSkills.ResourceOnly(skill)) return false;
        if (healingSkills == null) return HealingSkills.Contains(skill, actor == target);
        return healingSkills.Contains(skill) || skill is >= 11000000 and < 20000000 && healingSkills.Contains(skill / 10000 * 10000);
    }

    void Hit(ref WireReader reader, long time, List<DecodedCombatEvent> events, string hex)
    {
        int target = Positive(reader.Var()), variant = reader.Var(), flag = reader.Var(), actor = Positive(reader.Var());
        uint rawSkill = reader.U32(); int counter = reader.Byte();
        if ((variant & 4) == 0) { UnknownRecords++; return; } // Companion notices have no amount block.
        int type = reader.Var();
        if (type is 36 or 37 or 39 or 40 or 41 or 42) return;
        if (type > 72 || rawSkill > int.MaxValue) throw new InvalidDataException("Invalid hit values.");
        int form = variant & 3;
        int flagsLength = form switch { 0 => 8, 1 => 13, 3 => 15, _ => 0 };
        if (form == 2) { var peek = reader; peek.Byte(); int at = peek.Position; peek.Var(); flagsLength = 10 + peek.Position - at; }
        byte[] flags = reader.Take(flagsLength).ToArray();
        int direction = 0;
        if (form is 1 or 3) direction = flags[2] & 3;
        else if (form == 2) {
            var directionReader = new WireReader(flags);
            directionReader.Byte(); directionReader.Var();
            direction = directionReader.Byte() & 3;
        }
        int unknown = reader.Var(), amount = reader.Var();
        int loop = reader.Remaining > 0 ? reader.Var() : 0;
        if (amount is < 1 or > 10000000) throw new InvalidDataException("Invalid hit amount.");
        // Preserve auxiliary hits as metadata: the reference's TotalDamage is the primary amount.
        var extraHits = new List<int>();
        if (reader.Remaining > 0)
        {
            int count = reader.Var();
            if (count is >= 1 and <= 10) for (int i = 0; i < count; i++) extraHits.Add(reader.Var());
        }
        if (HealingSkills.ResourceOnly((int)rawSkill)) return;
        bool heal = IsHealing((int)rawSkill, actor, target);
        if (actor == target && !heal) { UnknownRecords++; return; }
        events.Add(new(heal ? "heal" : "damage", Damage: new()
        {
            Timestamp = time, ActorId = actor, TargetId = target, SkillCode = (int)rawSkill,
            Damage = amount, IsHeal = heal, IsCritical = !heal && type <= 35 && ((type / 3) & 1) != 0,
            // Extended forms carry bit 0 back / bit 1 front after their prefix.
            // Short forms and contradictory flags remain unclassified.
            IsBackAttack = !heal && direction == 1,
            IsFrontAttack = !heal && direction == 2,
            AdditionalData = new() {
                ["WireOpcode"] = JsonSerializer.SerializeToElement(hex),
                ["WireType"] = JsonSerializer.SerializeToElement(type), ["Counter"] = JsonSerializer.SerializeToElement(counter),
                ["Variant"] = JsonSerializer.SerializeToElement(variant), ["Flags"] = JsonSerializer.SerializeToElement(Convert.ToHexString(flags)),
                ["ExtraHits"] = JsonSerializer.SerializeToElement(extraHits), ["Loop"] = JsonSerializer.SerializeToElement(loop),
                ["Flag"] = JsonSerializer.SerializeToElement(flag), ["Unknown"] = JsonSerializer.SerializeToElement(unknown)
            }
        }));
    }

    void RecoveryTick(ref WireReader reader, long time, List<DecodedCombatEvent> events, string opcode)
    {
        int target = Positive(reader.Var()); int flags = reader.Byte();
        // 09 announces the remaining recovery pool. Only 0B carries an actual
        // tick as a separate field. Counting either the pool or the announcement
        // would multiply HoT totals. Other effect/resource layouts stay unknown.
        if (flags != 0x0b) { UnknownRecords++; return; }
        int actor = Positive(reader.Var()), slot = reader.Var(); uint effect = reader.U32();
        int remaining = reader.Var(), recovered = reader.Var(); uint rawSkill = reader.U32();
        if (reader.Remaining != 0 || rawSkill > int.MaxValue || effect == 0 || recovered > 10000000)
            throw new InvalidDataException("Invalid recovery tick layout.");
        int skill = (int)rawSkill;
        // Same player-skill family in the effect and source skill; arbitrary
        // monster effects, potion resource pairs and shield pools are excluded.
        if (!IsHealing(skill, actor, target) || effect / 1000000 != skill / 10000) { UnknownRecords++; return; }
        if (recovered == 0) return;
        events.Add(new("heal", Damage: new() {
            Timestamp = time, ActorId = actor, TargetId = target, SkillCode = skill, Damage = recovered, IsHeal = true,
            AdditionalData = new() {
                ["WireOpcode"] = JsonSerializer.SerializeToElement(opcode),
                ["Periodic"] = JsonSerializer.SerializeToElement(true),
                ["EffectId"] = JsonSerializer.SerializeToElement(effect),
                ["Slot"] = JsonSerializer.SerializeToElement(slot),
                ["RemainingRecovery"] = JsonSerializer.SerializeToElement(remaining)
            }
        }));
    }

    static void Abnormal(ref WireReader reader, bool refresh, long time, List<DecodedCombatEvent> events, string hex)
    {
        int target = Positive(reader.Var());
        if (!refresh && reader.Byte() != 1) throw new InvalidDataException("Unknown abnormal apply layout.");
        int form = reader.Byte(); if (form is not (17 or 19)) throw new InvalidDataException("Unknown abnormal form.");
        int slot = reader.Var(); uint id = reader.U32(), duration = reader.U32(); reader.Take(4); ulong stamp = reader.U64();
        if (id is 0 or > 1000000000 || stamp < 1577836800000UL || stamp > 4102444800000UL) throw new InvalidDataException("Invalid abnormal fields.");
        if (form == 17)
        {
            if (id != 10000008 || duration != uint.MaxValue) throw new InvalidDataException("Unknown enrage form.");
            Emit(events, "boss-enrage", time, hex, new { EntityId = target, AbnormalId = id }); return;
        }
        if (duration != uint.MaxValue && duration > 86400000) throw new InvalidDataException("Invalid abnormal duration.");
        int caster = 0; uint sourceSkill = 0;
        if (reader.Remaining > 0) { var optional = reader; int candidate = optional.Var(); if (candidate > 0 && optional.Remaining >= 5) { caster = candidate; optional.Byte(); sourceSkill = optional.U32(); } }
        Emit(events, refresh ? "abnormal-refresh" : "abnormal-apply", time, hex, new { TargetId = target, Slot = slot, AbnormalId = id, DurationMs = duration, ServerStampMs = stamp, CasterId = caster, SourceSkillId = sourceSkill });
    }

    static void Energy(ref WireReader reader, bool list, long time, List<DecodedCombatEvent> events)
    {
        int form = list ? 0 : reader.Byte(); if (form > 1) throw new InvalidDataException("Unknown energy update.");
        int count = list ? reader.Var() : 1; if (count is < 1 or > 1000) throw new InvalidDataException("Invalid ticket count.");
        LootEnergyEvent? result = null;
        for (int i = 0; i < count; i++)
        {
            int flags = reader.Byte(); if ((flags & ~13) != 0) throw new InvalidDataException("Unknown ticket flags.");
            uint id = reader.U32(); if ((flags & 1) != 0) reader.U64();
            int daily = (flags & 4) != 0 ? reader.Var() : 0, bonus = (flags & 8) != 0 ? reader.Var() : 0;
            if (id == 60000001) result = new(daily, bonus, list, time);
        }
        if (!list) reader.Take(form == 1 ? 5 : 1);
        if (reader.Remaining != 0) throw new InvalidDataException("Ticket trailing data.");
        if (result != null) events.Add(new("energy", Energy: result));
    }
}
