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
    readonly AbnormalAttribution abnormalSources = new();
    readonly Dictionary<long, CapturedPartyMember> partyMembers = new();
    long partyLeader;
    bool partyComplete;
    bool forceRoster;
    bool partyResetPending;
    bool npcShopOpened;
    string? localCharacterName;
    long partyLocalIdentity;
    int partyLocalServer;
    string? clockConnection;
    long? serverOffset;
    int expandedBudget, nestedBudget;
    public string Target => "Experimental EU decoder: direct and periodic combat effects; partial event coverage";
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
        if (clockConnection != key) {
            localCharacterName = null; partyLocalIdentity = 0; partyLocalServer = 0;
            partyResetPending |= partyComplete || partyMembers.Count > 0;
            clockConnection = key; serverOffset = null; partyMembers.Clear(); partyLeader = 0; partyComplete = false; forceRoster = false;
        }
        abnormalSources.Connection(key);
        try
        {
            foreach (var record in stream.Push(payload.Data)) DecodeRecord(record, payload.TimestampMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), events, 0, payload.CounterTimestampMs);
        }
        catch (InvalidDataException ex) {
            FramingErrors++; streams.Remove(key); abnormalSources.Clear(); partyResetPending |= partyComplete || partyMembers.Count > 0;
            partyMembers.Clear(); partyLeader = 0; partyComplete = false; forceRoster = false; return new(DecodeState.Rejected, [], ex.Message);
        }
        if (partyResetPending) {
            events.Insert(0, new("party", Party: new([], "Party mapping awaiting capture roster", Complete: false)));
            partyResetPending = false;
        }
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
            if (opcode == 0xffff)
            {
                int size = checked((int)reader.U32());
                if (size < 1 || size > expandedBudget) throw new InvalidDataException("Nested expansion budget exceeded.");
                expandedBudget -= size; byte[] body = Lz4Block.Expand(reader.Take(reader.Remaining), size);
                var inner = new GameRecordStream(); var children = inner.Push(body);
                if (inner.PendingBytes != 0) throw new InvalidDataException("Incomplete records in bundle.");
                Bundles++;
                foreach (var child in children) DecodeRecord(child, time, staged, depth + 1, counterTime);
            }
            else
            {
                switch (opcode)
                {
                    case 0x9e56:
                        if (AppearancePresetWire.Read(ref reader) is { } presets) Emit(staged, "appearance-presets", time, hex, presets);
                        break;
                    case 0x4838:
                        Emit(staged, "telegraph", time, hex, BossTelegraphs.Read(ref reader)); break;
                    case 0x468d:
                        if (BossAnnouncementWire.Read(ref reader) is { } announcement) Emit(staged, "boss-kill", time, hex, announcement);
                        break;
                    case 0x26e3:
                        Emit(staged, "scoreboard", time, hex, BattleScoreWire.Read(ref reader)); break;
                    case 0x02e3: case 0x03e3: case 0x05e3: case 0x07e3: case 0x08e3:
                        if (ArtifactSiegeWire.Read(ref reader, opcode, time, serverOffset ?? 0) is { } siege) Emit(staged, siege.Kind, time, hex, siege.Data);
                        break;
                    case 0x0a40:
                        if (AbyssWire.Matching(ref reader) is { } matching) Emit(staged, "abyss-matching", time, hex, matching);
                        break;
                    case 0x578d:
                        if (AbyssWire.Season(ref reader, localCharacterName) is { } season) Emit(staged, "abyss-season", time, hex, season);
                        break;
                    case 0x41e2: case 0x43e2: case 0x158d: case 0x35e3: case 0x448d:
                        if (SessionSignalWire.Read(ref reader, opcode) is { } signal) Emit(staged, signal.Kind, time, hex, signal.Data);
                        break;
                    case 0x9156:
                        Emit(staged, "black-market", time, hex, BlackMarketWire.Read(ref reader, serverOffset ?? 0)); break;
                    case 0x9556:
                        var marketPurchase = BlackMarketWire.Purchase(ref reader);
                        if (marketPurchase != null) Emit(staged, "black-market-purchase", time, hex, marketPurchase);
                        break;
                    case 0x8f8d: case 0x8b8d:
                    {
                        var conversions = ConversionWire.Read(ref reader, opcode == 0x8f8d);
                        if (conversions != null) Emit(staged, "morph-counts", time, hex, conversions);
                        break;
                    }
                    case 0x8e56: case 0x8f56: case 0x018c: case 0x038c:
                    {
                        var purchase = ShopPurchaseWire.Read(ref reader, opcode);
                        if (purchase.Result != 0) break;
                        if (opcode == 0x018c) npcShopOpened = true;
                        if (opcode == 0x038c && !npcShopOpened || opcode == 0x018c && purchase.Batch.Purchases.Length == 0) break;
                        Emit(staged, "shop-purchases", time, hex, purchase.Batch); break;
                    }
                    case 0x1536:
                    {
                        var login = LoginWire.Read(ref reader);
                        if (login.Result == 0) {
                            localCharacterName = null; partyLocalIdentity = 0; partyLocalServer = 0;
                            Emit(staged, "account-login", time, hex, login);
                            abnormalSources.Clear(); partyMembers.Clear(); partyLeader = 0; partyComplete = false; forceRoster = false;
                        }
                        break;
                    }
                    case 0x1c8d:
                        Emit(staged, "duty-missions", time, hex, DutyMissionWire.Read(ref reader)); break;
                    case 0x0036:
                    {
                        long server = unchecked((long)reader.U64());
                        if (server <= 0 || reader.Remaining != 0) throw new InvalidDataException("Invalid map clock.");
                        serverOffset = checked(server - time);
                        break;
                    }
                    case 0x4a36:
                        Emit(staged, "actor-stats", time, hex, StatUpdateWire.Read(ref reader)); break;
                    case 0x5636:
                    {
                        int cp = checked((int)reader.U64()); long highest = checked((long)reader.U64());
                        if (reader.Remaining != 0) throw new InvalidDataException("Unknown combat power trailer.");
                        Emit(staged, "local-combat-power", time, hex, new { CombatPower = cp, HighestCombatPower = highest });
                        break;
                    }
                    case 0x0040:
                    {
                        int entity = Positive(reader.Var()); byte phase = reader.Byte();
                        if (reader.Remaining != 0) throw new InvalidDataException("Unknown phase trailer.");
                        Emit(staged, "actor-phase", time, hex, new { EntityId = entity, Phase = phase }); break;
                    }
                    case 0x528d:
                    {
                        int entity = Positive(reader.Var());
                        if (reader.Remaining != 0) throw new InvalidDataException("Unknown aggro trailer.");
                        Emit(staged, "npc-aggro", time, hex, new { EntityId = entity }); break;
                    }
                    case 0x218d:
                    {
                        int entity = Positive(reader.Var()); byte flags = reader.Byte(), state = reader.Byte();
                        if ((flags & ~1) != 0) throw new InvalidDataException("Unknown combat state flags.");
                        long serverBerserk = (flags & 1) != 0 ? unchecked((long)reader.U64()) : 0;
                        if (serverBerserk < 0 || reader.Remaining != 0) throw new InvalidDataException("Invalid combat state trailer.");
                        long? berserk = serverBerserk > 0 && serverOffset is long offset ? checked(serverBerserk - offset) : null;
                        Emit(staged, "combat-state", time, hex, new { EntityId = entity, State = state, InCombat = state != 0, BerserkAtMs = berserk });
                        break;
                    }
                    case 0x2136:
                    {
                        Emit(staged, "local-zone", time, hex, LocalPositionWire.Prepare(ref reader)); abnormalSources.Clear();
                        // A zone invalidates entity IDs, not the confirmed membership identities.
                        foreach (var member in partyMembers.Values.ToArray()) partyMembers[member.Id] = member with { EntityId = 0 };
                        if (partyMembers.Count > 0 || partyComplete) PartyEvents(staged, time, hex, []);
                        break;
                    }
                    case 0x2336:
                        Emit(staged, "local-position", time, hex, LocalPositionWire.Loaded(ref reader)); break;
                    case 0x0140:
                        Emit(staged, "zone-state", time, hex, ZoneStateWire.Read(ref reader)); break;
                    case 0x1e56:
                        Emit(staged, "ground-drops", time, hex, GroundLootWire.Appeared(ref reader)); break;
                    case 0x1f56:
                        Emit(staged, "ground-removals", time, hex, GroundLootWire.Removed(ref reader)); break;
                    case 0x1156:
                        Emit(staged, "inventory", time, hex, InventoryWire.Read(ref reader, true)); break;
                    case 0x1b56:
                        Emit(staged, "inventory-update", time, hex, InventoryWire.Read(ref reader, false)); break;
                    case 0x0191:
                        Emit(staged, "field-boss-list", time, hex, FieldBossWire.Read(ref reader)); break;
                    case 0x3438:
                    {
                        byte flags = reader.Byte(); ushort result = reader.U16(); int target = reader.Var();
                        if ((flags & ~1) != 0) throw new InvalidDataException("Unknown target reply flags.");
                        if ((flags & 1) != 0) reader.Take(4);
                        if (reader.Remaining != 0) throw new InvalidDataException("Unknown target reply trailer.");
                        if (result == 0) Emit(staged, "local-target", time, hex, new { EntityId = target });
                        break;
                    }
                    case 0x2092:
                    {
                        int target = reader.Var();
                        if (reader.Remaining != 0) throw new InvalidDataException("Unknown party boss-target trailer.");
                        Emit(staged, "party-boss-target", time, hex, new { EntityId = target }); break;
                    }
                    case 0x2d92: case 0x2e92:
                        Emit(staged, opcode == 0x2e92 ? "map-event-join" : "map-event-progress", time, hex, MapEventWire.Read(ref reader, opcode == 0x2e92)); break;
                    case 0x2f92:
                    {
                        int id = Positive(checked((int)reader.U32()));
                        if (reader.Remaining != 0) throw new InvalidDataException("Unknown map event departure trailer.");
                        Emit(staged, "map-event-leave", time, hex, new { EventId = id }); break;
                    }
                    case 0x338a:
                    {
                        int entity = Positive(reader.Var()); string guild = PlayerMetadata.Guild(ref reader);
                        if (reader.Remaining != 0) throw new InvalidDataException("Unknown guild update trailer.");
                        Emit(staged, "player-guild", time, hex, new { EntityId = entity, Guild = guild }); break;
                    }
                    case 0x918d:
                    {
                        int entity = Positive(reader.Var()), level = checked((int)reader.U32());
                        if (reader.Remaining != 0) throw new InvalidDataException("Unknown level update trailer.");
                        Emit(staged, "player-level", time, hex, new { EntityId = entity, Level = level }); break;
                    }
                    case 0x338d:
                        if (Gathering.Read(ref reader) is int gathered) Emit(staged, "gathered", time, hex, new { EntityId = gathered });
                        break;
                    case 0x3436:
                        Emit(staged, "world-object", time, hex, WorldObjectWire.Spawn(ref reader)); break;
                    case 0x3536:
                        Emit(staged, "world-object-removed", time, hex, WorldObjectWire.Remove(ref reader)); break;
                    case 0x8356: case 0x8456:
                        Emit(staged, "achievements", time, hex, AchievementWire.Read(ref reader, opcode == 0x8356)); break;
                    case 0x268d:
                    {
                        int entity = Positive(reader.Var());
                        if (reader.Remaining != 0) throw new InvalidDataException("Unknown resurrection trailer.");
                        Emit(staged, "entity-revived", time, hex, new { EntityId = entity }); break;
                    }
                    case 0x3538:
                    {
                        int actor = Positive(reader.Var()); byte flags = reader.Byte(); int target = reader.Var();
                        if ((flags & ~1) != 0) throw new InvalidDataException("Unknown target update flags.");
                        if ((flags & 1) != 0) reader.Take(4);
                        if (reader.Remaining != 0) throw new InvalidDataException("Unknown target update trailer.");
                        Emit(staged, "target", time, hex, new { ActorId = actor, TargetId = target }); break;
                    }
                    case 0x0090:
                        Emit(staged, "pet-roster", time, hex, PetWire.Roster(ref reader)); break;
                    case 0x0d90:
                        Emit(staged, "pet-progress", time, hex, new { Gains = PetWire.Progress(ref reader) }); break;
                    case 0x0e90:
                        Emit(staged, "pet-progress-decrease", time, hex, new { Gains = PetWire.Progress(ref reader) }); break;
                    case 0x1490:
                        Emit(staged, "pet-level", time, hex, PetWire.Level(ref reader)); break;
                    case 0x0990:
                        Emit(staged, "pet-added", time, hex, PetWire.Acquired(ref reader)); break;
                    case 0x4936:
                    {
                        var stats = CharacterReadings.Stats(ref reader, time);
                        if (stats == null) break;
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
                        var start = SkillStartWire.Read(ref reader);
                        Emit(staged, "skill-use", time, hex, start);
                        if (start.CooldownMs > 0) Emit(staged, "skill-cooldown", time, hex,
                            new { start.ActorId, start.SkillCode, RemainingMs = start.CooldownMs });
                        if (start.RechargeMs > 0) Emit(staged, "skill-charges", time, hex,
                            new { start.ActorId, start.SkillCode, start.Charges, start.RechargeMs });
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
                    {
                        var original = reader;
                        try {
                            var group = PartyFinderWire.Group(ref reader);
                            Emit(staged, "party-finder", time, hex, group);
                            var profiles = group.Members.Where(m => m.Name.Length is > 0 and <= 24 && m.Server is >= 1000 and <= 9999 && m.CombatPower is >= 0 and <= 2000000)
                                .GroupBy(m => (m.Name, m.Server)).Where(g => g.Count() == 1).Select(g => new PartyProfile(g.Key.Name, g.Key.Server, (int)g.First().CombatPower, 0)).ToArray();
                            if (profiles.Length is > 0 and <= 10) staged.Add(new("party-profiles", Profiles: profiles));
                        }
                        catch (Exception ex) when (ex is InvalidDataException or OverflowException or DecoderFallbackException) {
                            reader = original;
                            staged.Add(new("party-profiles", Profiles: PlayerTelemetry.ReadParty(ref reader)));
                        }
                        break;
                    }
                    case 0x0b97: case 0x1597: case 0x1697: case 0x1d97: case 0x2a97:
                        if (PartyFinderWire.Update(ref reader, opcode) is { } finderUpdate) Emit(staged, finderUpdate.Kind, time, hex, finderUpdate.Data);
                        break;
                    case 0x0336:
                        if (PlayerTelemetry.ReadPing(ref reader, time, counterTime) is { } ping) staged.Add(new("ping", Ping: ping));
                        break;
                    case 0x0438: Hit(ref reader, time, staged, hex); break;
                    case 0x0538: PeriodicEffect(ref reader, time, staged, hex); break;
                    case 0x4136:
                    {
                        var spawn = NpcSpawnWire.Read(ref reader);
                        var npc = new Actor(spawn.EntityId, npcs.GetName(spawn.NpcId), false, false, 0, "Unknown", 0)
                            { NpcId = spawn.NpcId, OwnerId = spawn.OwnerId == spawn.EntityId ? 0 : spawn.OwnerId };
                        staged.Add(new("actor", Actor: npc));
                        Emit(staged, "npc-spawn", time, hex, new { EntityId = npc.Id, npc.NpcId, npc.Name, spawn.OwnerId, spawn.Level, spawn.BerserkAt });
                        Emit(staged, "hp", time, hex, new { spawn.EntityId, spawn.CurrentHp, spawn.MaximumHp });
                        Emit(staged, "actor-position", time, hex, new { ActorId = spawn.EntityId, spawn.X, spawn.Y, spawn.Z, VisibilityReason = spawn.Reason, EntityKind = "npc" });
                        if (spawn.Stats.Count > 0) Emit(staged, "actor-stats", time, hex, new StatUpdateReading(spawn.EntityId, spawn.Stats, spawn.MaximumHp));
                        if (spawn.Guard != null) Emit(staged, "guard", time, hex, spawn.Guard);
                        AbnormalValues(spawn.EntityId, spawn.Effects, false, time, staged, hex);
                        break;
                    }
                    case 0x4536:
                    {
                        var spawn = PlayerSpawnWire.Read(ref reader, time); int id = spawn.Actor.Id;
                        staged.Add(new("actor", Actor: spawn.Actor));
                        if (BindPartyActor(spawn.Actor)) PartyEvents(staged, time, hex, []);
                        Emit(staged, "visible-equipment", time, hex, new EquipmentReading(id, true, spawn.Equipment));
                        Emit(staged, "hp", time, hex, new { EntityId = id, CurrentHp = spawn.State.Hp, spawn.State.MaximumHp });
                        Emit(staged, "actor-position", time, hex, new { ActorId = id, spawn.State.X, spawn.State.Y, spawn.State.Z, VisibilityReason = spawn.Reason, EntityKind = "player" });
                        if (spawn.Stats.Count > 0) Emit(staged, "actor-stats", time, hex, new StatUpdateReading(id, spawn.Stats, spawn.State.MaximumHp));
                        AbnormalValues(id, spawn.Effects, false, time, staged, hex);
                        break;
                    }
                    case 0x3336:
                    {
                        var character = LocalCharacterWire.Read(ref reader, time); int id = character.Actor.Id;
                        localCharacterName = character.Actor.Name;
                        bool samePartyOwner = partyLocalIdentity > 0 && partyLocalIdentity == character.Actor.PersistentId && partyLocalServer == character.Actor.Server;
                        if (!samePartyOwner && (partyLocalIdentity > 0 || !partyMembers.ContainsKey(character.Actor.PersistentId))) {
                            partyMembers.Clear(); partyLeader = 0; partyComplete = false; forceRoster = false;
                        }
                        partyLocalIdentity = character.Actor.PersistentId; partyLocalServer = character.Actor.Server;
                        BindPartyActor(character.Actor);
                        staged.Add(new("local-player", Actor: character.Actor));
                        if (partyMembers.Count > 0 || partyComplete) {
                            foreach (var member in partyMembers.Values.Where(m => m.EntityId > 0)) staged.Add(PartyActor(member, time));
                            PartyEvents(staged, time, hex, []);
                        }
                        Emit(staged, "appearance-current", time, hex, character.Appearance);
                        Emit(staged, "wing-change", time, hex, new { EntityId = id, Skin = false, WingId = character.State.WingId });
                        Emit(staged, "wing-change", time, hex, new { EntityId = id, Skin = true, WingId = character.State.WingSkinId });
                        Emit(staged, "visible-equipment", time, hex, new EquipmentReading(id, true, character.Equipment));
                        abnormalSources.Clear();
                        Emit(staged, "local-zone", time, hex, new { character.MapId, character.State.X, character.State.Y, character.State.Z, character.State.Direction });
                        staged.Add(new("player-stats", Stats: new(time, character.Stats, true, character.State.MaximumHp)));
                        Emit(staged, "hp", time, hex, new { EntityId = id, CurrentHp = character.State.Hp, character.State.MaximumHp });
                        Emit(staged, "resources", time, hex, new { EntityId = id, Values = character.State.Resources() });
                        AbnormalValues(id, character.Effects, false, time, staged, hex);
                        break;
                    }
                    case 0x0092:
                    {
                        var snapshot = PartyWire.Full(ref reader);
                        var members = forceRoster ? partyMembers.Values.Where(m => m.GroupKey != snapshot.Key).Concat(snapshot.Members).ToArray() : snapshot.Members;
                        ForceWire.Validate(members);
                        partyMembers.Clear(); foreach (var member in members) partyMembers.Add(member.Id, member);
                        if (!forceRoster) { partyComplete = true; partyLeader = snapshot.LeaderId; }
                        PartyEvents(staged, time, hex, snapshot.Members); break;
                    }
                    case 0x1192:
                    {
                        long leader = checked((long)reader.U64());
                        if (reader.Remaining != 0) throw new InvalidDataException("Unknown party leader trailer.");
                        partyLeader = leader; PartyEvents(staged, time, hex, []); break;
                    }
                    case 0x0d92: case 0x2292:
                    {
                        var member = PartyWire.Added(ref reader);
                        member = member with { GroupKey = partyMembers.GetValueOrDefault(member.Id)?.GroupKey ?? (forceRoster ? 0 : partyMembers.Values.FirstOrDefault()?.GroupKey ?? 0) };
                        if (!partyMembers.ContainsKey(member.Id) && partyMembers.Count >= (forceRoster ? 64 : 10) ||
                            member.EntityId > 0 && partyMembers.Values.Any(m => m.Id != member.Id && m.EntityId == member.EntityId))
                            throw new InvalidDataException("Invalid party addition.");
                        partyMembers[member.Id] = member; PartyEvents(staged, time, hex, [member]); break;
                    }
                    case 0x1392: case 0x1992: case 0x1f92: case 0x1f93:
                        if (PartyWire.Left(ref reader, opcode)) { partyMembers.Clear(); partyLeader = 0; partyComplete = true; forceRoster = false; staged.Add(new("party", Party: new([], "Captured party departure") { Timestamp = time })); }
                        break;
                    case 0x0296:
                    {
                        var members = ForceWire.Full(ref reader, out long leader);
                        partyLeader = leader;
                        partyMembers.Clear(); foreach (var member in members) partyMembers.Add(member.Id, member);
                        forceRoster = true; partyComplete = true; PartyEvents(staged, time, hex, members); break;
                    }
                    case 0x0a96: case 0x1b96:
                    {
                        var member = ForceWire.Added(ref reader, opcode == 0x1b96);
                        if (opcode == 0x1b96) member = member with { GroupKey = partyMembers.GetValueOrDefault(member.Id)?.GroupKey ?? 0 };
                        ForceWire.Validate(partyMembers.Values.Where(m => m.Id != member.Id).Append(member));
                        if (!forceRoster) partyComplete = false;
                        forceRoster = true; partyMembers[member.Id] = member; PartyEvents(staged, time, hex, [member]); break;
                    }
                    case 0x2296:
                    {
                        long id = ForceWire.Removed(ref reader); partyMembers.Remove(id);
                        if (!forceRoster) partyComplete = false;
                        forceRoster = true; PartyEvents(staged, time, hex, []); break;
                    }
                    case 0x1a96: case 0x2e96:
                        ForceWire.Left(ref reader, opcode == 0x1a96); partyMembers.Clear(); partyLeader = 0; partyComplete = true; forceRoster = false;
                        staged.Add(new("party", Party: new([], "Captured alliance departure") { Timestamp = time })); break;
                    case 0x1c92: case 0x2c96:
                    {
                        var status = PartyWire.Status(ref reader, opcode == 0x2c96);
                        if (partyMembers.TryGetValue(status.Id, out var member)) {
                            if (member.CombatPower == status.CombatPower && member.ClassId == status.ClassId && member.Level == status.Level && member.GearScore == status.GearScore && member.Guild == status.Guild) break;
                            member = member with { CombatPower = status.CombatPower, ClassId = status.ClassId, Level = status.Level, GearScore = status.GearScore, Guild = status.Guild }; partyMembers[member.Id] = member;
                            if (member.EntityId > 0) staged.Add(PartyActor(member, time));
                        }
                        break;
                    }
                    case 0x008d:
                    {
                        var health = HealthWire.Read(ref reader);
                        if (health.CurrentHp != null || health.MaximumHp != null)
                            Emit(staged, "hp", time, hex, new { health.EntityId, health.CurrentHp, health.MaximumHp });
                        var resources = health.Values.Where(p => p.Key is 1 or 3 or 6 or 8 or 10 or 13).ToDictionary();
                        if (resources.Count > 0) Emit(staged, "resources", time, hex, new { health.EntityId, Values = resources });
                        break;
                    }
                    case 0x4636:
                    {
                        var shields = BarrierWire.Read(ref reader);
                        foreach (var shield in shields.Values) {
                            var source = abnormalSources.Find(shields.Target, shield.Uid, time);
                            Emit(staged, "barrier", time, hex, new { TargetId = shields.Target, shield.Uid, shield.Remaining,
                                ActorId = source?.Caster ?? 0, SkillCode = source?.Skill ?? 0, AbnormalId = source?.Buff ?? 0 });
                        }
                        break;
                    }
                    case 0x1a37: case 0x1b37: case 0x1c37:
                        Emit(staged, "actor-position", time, hex, MovementWire.Read(ref reader, opcode == 0x1a37)); break;
                    case 0x1438: case 0x1638:
                        Emit(staged, "displacement", time, hex, DisplacementWire.Read(ref reader, opcode == 0x1438)); break;
                    case 0x3c37: case 0x3d37:
                        Emit(staged, "actor-position", time, hex, WarpWire.Read(ref reader, opcode == 0x3d37)); break;
                    case 0x1c56:
                        Emit(staged, "visible-equipment", time, hex, VisibleEquipment.ReadUpdate(ref reader)); break;
                    case 0x0051: case 0x0f38: case 0x1338:
                        Emit(staged, "skill-levels", time, hex, SkillLoadoutWire.Levels(ref reader, opcode)); break;
                    case 0x0039:
                        Emit(staged, "skill-choices", time, hex, new { Skills = SkillLoadoutWire.Choices(ref reader) }); break;
                    case 0x1838:
                        Emit(staged, "skill-chain", time, hex, SkillLoadoutWire.Chain(ref reader)); break;
                    case 0x26e2:
                        Emit(staged, "daevanion", time, hex, new { Boards = Daevanion.Read(ref reader) }); break;
                    case 0x7256:
                        Emit(staged, "wings", time, hex, new { Wings = Wings.ReadList(ref reader) }); break;
                    case 0x7956:
                        Emit(staged, "titles", time, hex, Titles.Read(ref reader, time)); break;
                    case 0x5b8d: case 0xaf8a: case 0x0be3:
                        Emit(staged, opcode == 0x5b8d ? "skins" : opcode == 0xaf8a ? "decorations" : "monoliths", time, hex,
                            new { Entries = CharacterCollections.Read(ref reader, opcode) }); break;
                    case 0x7656:
                    {
                        int entity = Positive(reader.Var()); bool skin = reader.Bit(); int wing = checked((int)reader.U32());
                        if (reader.Remaining != 0) throw new InvalidDataException("Unknown wing change trailer.");
                        Emit(staged, "wing-change", time, hex, new { EntityId = entity, Skin = skin, WingId = wing }); break;
                    }
                    case 0x4236: case 0x4736:
                    {
                        int entity = Positive(reader.Var()); byte reason;
                        if (opcode == 0x4236) {
                            byte flags = reader.Byte();
                            if ((flags & ~1) != 0) throw new InvalidDataException("Unknown NPC removal flags.");
                            reason = reader.Byte(); if ((flags & 1) != 0) reader.U32();
                        } else { reason = reader.Byte(); reader.Var(); }
                        if (reader.Remaining != 0) throw new InvalidDataException("Unknown entity removal trailer.");
                        abnormalSources.RemoveEntity(entity, time);
                        Emit(staged, "entity-removed", time, hex, new { EntityId = entity, Reason = reason,
                            Removal = reason == 3 ? "died" : reason == 7 ? "despawned" : "left" });
                        break;
                    }
                    case 0x0638:
                    {
                        int actor = Positive(reader.Var()); uint skill = reader.U32();
                        byte sequence = reader.Byte(), reason = reader.Byte();
                        if (skill is 0 or > int.MaxValue || reader.Remaining != 0) throw new InvalidDataException("Invalid skill end record.");
                        Emit(staged, "skill-end", time, hex, new { ActorId = actor, SkillCode = (int)skill, Sequence = sequence, StopReason = reason });
                        break;
                    }
                    case 0x048d:
                    {
                        var death = DeathWire.Read(ref reader);
                        Emit(staged, "entity-death", time, hex, new { death.EntityId, death.KillerId,
                            death.SkillCode, death.NpcId, death.ActionId, death.Reason, death.DieType });
                        if (death.KillerId > 0 && death.ServerId > 0 && !string.IsNullOrWhiteSpace(death.KillerName))
                            staged.Add(new("actor", Actor: new(death.KillerId, death.KillerName, true, false, 0, "Unknown", death.ServerId)));
                        break;
                    }
                    case 0x05e0:
                    {
                        Emit(staged, "guard", time, hex, GroupVitalsWire.Guard(ref reader)); break;
                    }
                    case 0x1b92: case 0x2b96:
                    {
                        var vitals = GroupVitalsWire.Read(ref reader);
                        Emit(staged, "hp", time, hex, new { vitals.EntityId, vitals.CurrentHp, vitals.MaximumHp, vitals.Alive,
                            GroupKind = opcode == 0x1b92 ? "party" : "alliance" });
                        Emit(staged, "resources", time, hex, new { vitals.EntityId,
                            Values = new Dictionary<byte, long> { [1] = vitals.Mp, [8] = vitals.MaximumMp } });
                        break;
                    }
                    case 0x0938: case 0x1c38:
                    {
                        bool charge = opcode == 0x1c38;
                        var cast = CastWire.Start(ref reader, charge);
                        Emit(staged, charge ? "charge-start" : "cast-start", time, hex, cast);
                        if (!charge && cast.SkillCode < 10000000)
                            Emit(staged, "boss-cast", time, hex, new { CasterId = cast.ActorId, cast.SkillCode, cast.TargetId, cast.CastingMs });
                        break;
                    }
                    case 0x0c38: case 0x2138:
                    {
                        var stopped = CastWire.Stop(ref reader);
                        Emit(staged, opcode == 0x2138 ? "charge-stop" : "cast-stop", time, hex, stopped);
                        break;
                    }
                    case 0x2a38: case 0x2b38: Abnormal(ref reader, opcode == 0x2b38, time, staged, hex); break;
                    case 0x2d38:
                        Emit(staged, "abnormal-immunity", time, hex, ImmunityWire.Read(ref reader)); break;
                    case 0x2c38:
                    {
                        var removed = AbnormalWire.Remove(ref reader);
                        foreach (var entry in removed.Values) {
                            abnormalSources.Remove(removed.Target, entry.Slot, time);
                            Emit(staged, "abnormal-remove", time, hex, new { TargetId = removed.Target, entry.Slot,
                                ExpireReason = entry.Reason, DispelCasterId = entry.Caster, DispelSkillId = entry.Skill, DispelEffectId = entry.Effect });
                        }
                        break;
                    }
                    case 0x0061:
                        Emit(staged, "dungeon-state", time, hex, DungeonWire.State(ref reader)); break;
                    case 0x2e57:
                    {
                        int npc = checked((int)reader.U32());
                        if (npc <= 0 || reader.Remaining != 0) throw new InvalidDataException("Invalid dungeon boss notification.");
                        Emit(staged, "dungeon-boss-killed", time, hex, new { NpcId = npc }); break;
                    }
                    case 0x0961:
                    {
                        int count = checked((int)reader.U32());
                        if (reader.Remaining != 0) throw new InvalidDataException("Invalid dungeon death count.");
                        Emit(staged, "dungeon-deaths", time, hex, new { Count = count }); break;
                    }
                    case 0x0161:
                        Emit(staged, "dungeon-result", time, hex, DungeonWire.Result(ref reader)); break;
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
        var hit = HitWire.Read(ref reader);
        if (CombatEffectTypes.ResourceOnly(hit.EffectId)||HealingSkills.SummonInitialization(hit.SkillCode,hit.EffectId,hit.ActorId,hit.TargetId)) return;
        bool heal = healingSkills == null ? CombatEffectTypes.Healing(hit.EffectId) : IsHealing(hit.SkillCode, hit.ActorId, hit.TargetId);
        void Amount(long amount, int target, bool healing, string component) {
            if (amount <= 0) return;
            events.Add(new(healing ? "heal" : "damage", Damage: new() {
                Timestamp = time, ActorId = hit.ActorId, TargetId = target, SkillCode = hit.SkillCode,
                Damage = amount, IsHeal = healing, IsCritical = !healing && hit.HitType == 3,
                IsBackAttack = !healing && hit.Angle == 1, IsFrontAttack = !healing && hit.Angle == 2,
                AdditionalData = new() {
                    ["WireOpcode"] = JsonSerializer.SerializeToElement(hex),
                    ["WireType"] = JsonSerializer.SerializeToElement(hit.HitType),
                    ["EffectId"] = JsonSerializer.SerializeToElement(hit.EffectId),
                    ["Counter"] = JsonSerializer.SerializeToElement(hit.Action),
                    ["ExtraHits"] = JsonSerializer.SerializeToElement(hit.AdditionalHits),
                    ["Component"] = JsonSerializer.SerializeToElement(component),
                    ["Perfect"] = JsonSerializer.SerializeToElement(hit.Perfect),
                    ["HardHit"] = JsonSerializer.SerializeToElement(hit.HardHit),
                    ["ShieldBlock"] = JsonSerializer.SerializeToElement(hit.ShieldBlock),
                    ["WeaponBlock"] = JsonSerializer.SerializeToElement(hit.WeaponBlock),
                    ["RestoredHp"] = JsonSerializer.SerializeToElement(hit.RestoredHp)
                }
            }));
        }
        Amount(hit.Amount, hit.TargetId, heal, "primary");
        if (heal) return;
        if (hit.Amount <= 0 && hit.HitType is 1 or 4 or 5 or 6 or 7 or 8 or 9)
            events.Add(new("defense", Defense: new() { Timestamp = time, ActorId = hit.ActorId, TargetId = hit.TargetId,
                SkillCode = hit.SkillCode, Outcome = hit.HitType == 1 ? DefenseOutcome.Evade : hit.HitType == 6 ? DefenseOutcome.Resist : DefenseOutcome.TargetAvoided }));
        // Primary damage already includes additional hit amounts; recovery is a
        // separate caster heal, not damage to the selected target.
        Amount(checked(Math.Max(0, hit.HpDrain) + Math.Max(0, hit.HpRecovery)), hit.ActorId, true, "drain-recovery");
    }

    void PeriodicEffect(ref WireReader reader, long time, List<DecodedCombatEvent> events, string opcode)
    {
        var tick = PeriodicWire.Read(ref reader);
        if (CombatEffectTypes.Barrier(tick.EffectId)) {
            var shieldSource = abnormalSources.Find(tick.TargetId, tick.Uid, time);
            Emit(events, "barrier", time, opcode, new { ActorId = tick.ActorId != 0 ? tick.ActorId : shieldSource?.Caster ?? 0, TargetId = tick.TargetId,
                tick.Uid, SkillCode = tick.Skill != 0 ? tick.Skill : shieldSource?.Skill ?? 0, tick.Remaining, AbnormalId = shieldSource?.Buff ?? 0 });
            return;
        }
        if (tick.BlockedSkill != 0 && tick.BlockedActor != tick.TargetId) {
            events.Add(new("defense", Defense: new() { Timestamp = time, ActorId = tick.BlockedActor,
                TargetId = tick.TargetId, SkillCode = tick.BlockedSkill, Outcome = DefenseOutcome.TargetAvoided }));
            return;
        }
        if(tick.HasRemaining && tick.ReflectionSkill==0 && CombatEffectTypes.PeriodicHealing(tick.EffectId) && !CombatEffectTypes.PeriodicExcluded(tick.EffectId))
            Emit(events,"recovery-preview",time,opcode,new { tick.TargetId, Slot=tick.Uid, tick.EffectId, tick.Remaining });
        if (tick.Amount <= 0 || CombatEffectTypes.PeriodicExcluded(tick.EffectId)) return;
        bool heal = tick.ReflectionSkill == 0 && CombatEffectTypes.PeriodicHealing(tick.EffectId);
        var source = abnormalSources.Find(tick.ReflectionSkill != 0 ? tick.ActorId : tick.TargetId, tick.Uid, time);
        int skill = tick.ReflectionSkill != 0 ? tick.ReflectionSkill : tick.Skill != 0 ? tick.Skill :
            source?.Caster == tick.ActorId ? source.Skill : 0;
        events.Add(new(heal ? "heal" : "damage", Damage: new() {
            Timestamp = time, ActorId = tick.ActorId, TargetId = tick.TargetId,
            SkillCode = skill, Damage = tick.Amount, IsHeal = heal,
            AdditionalData = new() {
                ["WireOpcode"] = JsonSerializer.SerializeToElement(opcode),
                ["Periodic"] = JsonSerializer.SerializeToElement(true),
                ["EffectId"] = JsonSerializer.SerializeToElement(tick.EffectId),
                ["Slot"] = JsonSerializer.SerializeToElement(tick.Uid),
                ["AbnormalId"] = JsonSerializer.SerializeToElement(source?.Buff ?? 0),
                ["RemainingRecovery"] = JsonSerializer.SerializeToElement(tick.Remaining),
                ["ReflectedSkill"] = JsonSerializer.SerializeToElement(tick.ReflectionSkill != 0 ? tick.Skill : 0)
            }
        }));
    }

    bool BindPartyActor(Actor actor)
    {
        bool changed = false;
        foreach (var member in partyMembers.Values.Where(m => m.EntityId == actor.Id && m.Id != actor.PersistentId).ToArray()) {
            partyMembers[member.Id] = member with { EntityId = 0 }; changed = true;
        }
        if (actor.PersistentId > 0 && partyMembers.TryGetValue(actor.PersistentId, out var known) &&
            known.Server == actor.Server && known.Name == actor.Name && known.EntityId != actor.Id) {
            partyMembers[known.Id] = known with { EntityId = actor.Id }; changed = true;
        }
        return changed;
    }
    void PartyEvents(List<DecodedCombatEvent> events, long time, string hex, CapturedPartyMember[] updated)
    {
        foreach (var member in updated.Where(m => m.EntityId > 0)) {
            events.Add(PartyActor(member, time));
            Emit(events, "hp", time, hex, new { member.EntityId, CurrentHp = member.Hp, member.MaximumHp });
            if (member.Mp is int mp && member.MaximumMp is int maxMp)
                Emit(events, "resources", time, hex, new { member.EntityId, Values = new Dictionary<byte, long> { [1] = mp, [8] = maxMp } });
        }
        events.Add(new("party", Party: new(partyMembers.Values.Where(m => m.EntityId > 0).Select(m => m.EntityId).ToArray(),
            partyComplete ? forceRoster ? "Captured alliance roster" : "Captured party roster" : "Partial captured roster",
            Complete: partyComplete, Replace: true, Alliance: forceRoster) { LeaderName = partyMembers.GetValueOrDefault(partyLeader)?.Name,
                Timestamp = time, Summaries = partyMembers.Values.Select(m => new PartyMemberSummary(m.Name, m.Server, CapturedClass.Name(m.ClassId), m.Level, m.GearScore, m.CombatPower)).ToArray() }));
    }
    static DecodedCombatEvent PartyActor(CapturedPartyMember member, long time) => new("actor",
        Actor: new(member.EntityId, member.Name, true, false, member.CombatPower, CapturedClass.Name(member.ClassId), member.Server)
            { PersistentId = member.Id, ProfileSource = "Captured party", CpSource = "Captured party", Metadata = new(member.Level, member.Guild, time, time, member.GearScore, time) });
    void Abnormal(ref WireReader reader, bool refresh, long time, List<DecodedCombatEvent> events, string hex)
    {
        var batch = AbnormalWire.Read(ref reader, refresh);
        AbnormalValues(batch.Target, batch.Values, refresh, time, events, hex);
    }
    void AbnormalValues(int target, AbnormalReading[] values, bool refresh, long time, List<DecodedCombatEvent> events, string hex)
    {
        foreach (var value in values) {
            bool permanent = value.Duration <= 0;
            long? deadline = !permanent && value.Expires > 0 && serverOffset is long offset ? checked(value.Expires - offset) : null;
            long duration = permanent ? uint.MaxValue : deadline is long expires ? Math.Max(0, checked(expires - time)) : value.Duration;
            abnormalSources.Add(target, value, time, deadline);
            if (value.AbnormalId == 10000008 && permanent) {
                Emit(events, "boss-enrage", time, hex, new { EntityId = target, value.AbnormalId });
            }
            Emit(events, refresh ? "abnormal-refresh" : "abnormal-apply", time, hex, new {
                TargetId = target, value.Slot, value.AbnormalId,
                DurationMs = duration, RawDurationMs = value.Duration, ExpiresAtMs = deadline,
                UntilRemoved = permanent, ServerStampMs = value.Expires, CasterId = value.Caster,
                SourceSkillId = value.Skill, AbnormalLevel = value.Level, value.ReadyToRemove,
                value.MultiplyCaster, value.MultiplyValue, CasterX = value.X, CasterY = value.Y, CasterZ = value.Z });
        }
    }

    static void Energy(ref WireReader reader, bool list, long time, List<DecodedCombatEvent> events)
    {
        int form = list ? 0 : reader.Byte(); if (form > 1) throw new InvalidDataException("Unknown energy update.");
        int count = list ? reader.Var() : 1; if (count > 1000) throw new InvalidDataException("Invalid ticket count.");
        LootEnergyEvent? result = null;
        var tickets = new ContentTicket[count];
        for (int i = 0; i < count; i++)
        {
            int flags = reader.Byte(); if ((flags & ~15) != 0) throw new InvalidDataException("Unknown ticket flags.");
            uint id = reader.U32();
            long remaining = (flags & 1) != 0 ? unchecked((long)reader.U64()) : 0;
            long recharge = (flags & 2) != 0 ? unchecked((long)reader.U64()) : 0;
            int daily = (flags & 4) != 0 ? reader.Var() : 0, bonus = (flags & 8) != 0 ? reader.Var() : 0;
            tickets[i] = new(unchecked((int)id), daily, bonus, remaining, recharge);
            if (id == 60000001) result = new(daily, bonus, list, time);
        }
        if (!list) reader.Take(form == 1 ? 5 : 1);
        if (reader.Remaining != 0) throw new InvalidDataException("Ticket trailing data.");
        Emit(events, "content-tickets", time, list ? "0B61" : "0C61", new ContentTicketReading(list, tickets));
        if (list || result != null) Emit(events, "currency-ticket", time, list ? "0B61" : "0C61",
            new CurrencyTicketReading(list, result == null ? null : (long)result.Daily + result.Bonus));
        if (result != null) events.Add(new("energy", Energy: result));
    }
}
