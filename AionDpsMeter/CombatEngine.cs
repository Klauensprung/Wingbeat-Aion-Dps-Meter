using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

public sealed partial class CombatEngine
{
    public readonly object Gate = new();
    internal CraftingChecklist Crafting { get; }
    internal AppearancePresetTracker AppearancePresets { get; } = new();
    internal BlackMarketTracker BlackMarket { get; } = new();
    internal PartyFinderTracker PartyFinder { get; } = new();
    internal LiveCastTracker LiveCast { get; } = new();
    internal BossAlertTracker BossAlerts {get;}=new();
    internal long LiveIdentityBoundary => skillBoundaryTime;
    internal long LiveSceneRevision { get; private set; }
    internal bool LiveIdentityReady => !skillsAwaitingIdentity && !cachedLocalIdentity && Fight.LocalPlayer > 0;
    internal PetCollectionSnapshot? CurrentPets() { lock (Gate) return LiveIdentityReady ? Fight.Pets : null; }
    internal SessionSignalTracker SessionSignals { get; } = new();
    internal AbyssTracker Abyss { get; } = new();
    internal ArtifactSiegeTracker Siege { get; } = new();
    internal BattleScoreTracker Scoreboard { get; } = new();
    internal ShopPurchaseTracker ShopPurchases { get; }
    internal FarmingCounterLedger GatheringTracking { get; }
    internal FarmingCounterLedger KillTracking { get; }
    internal FarmingCounterLedger LootTracking { get; }
    internal FarmingCounterLedger SoulTracking { get; }
    readonly FarmingSoulTracker farmingSouls;
    internal FarmingCounterLedger PetRegistrationTracking { get; }
    internal FarmingCounterLedger PetLevelTracking { get; }
    readonly FarmingPetTracker farmingPets;
    readonly FarmingLootTracker farmingLoot;
    internal FarmingKillTracker FarmingKills { get; } = new();
    internal WorldObjectTracker WorldObjects { get; } = new();
    internal DutyMissionLedger DutyMissions { get; }
    (DutyMissionReading Reading, long Timestamp)? pendingDutyMissions;
    internal MapEventTracker MapEvents { get; } = new();
    internal FieldBossTracker FieldBosses { get; } = new();
    internal LocalPositionTracker PartyPosition { get; } = new();
    internal FieldBossTimerCache FieldBossCache { get; set; } = new();
    readonly NetworkSnapshotRetention networkRetention = new();
    readonly CooldownTracker liveCooldowns = new();
    readonly StaggerBreakTracker staggerBreaks = new();
    int bossMap;
    long bossStateTime = -1;
    long bossScene = 1;
    readonly PetLevelNotices petNotices = new();
    readonly InventoryLootSession inventoryLoot = new();
    internal LootLedger Loot { get; } = new();
    internal CurrencyBalance[] CurrentCurrencies() => inventoryLoot.Currencies();
    internal CapturedContentTicket[] CurrentTickets() => inventoryLoot.Tickets();
    internal InventoryHoldingSnapshot CurrentHoldings() => inventoryLoot.Holdings();
    internal bool CurrentTicketsComplete => inventoryLoot.TicketsComplete;
    internal LocalPositionTracker Position { get; } = new();
    internal SpatialEvidence Spatial { get; } = new();
    internal PositionEstimator PositionPrediction { get; } = new();
    ObservedRunTracker observedRun = new();
    DungeonRunTracker dungeonRun = new();
    // Incoming scene state has no confirmed owner yet. Never merge it into the
    // previous character's compact run, even when both scenes use the same map.
    ObservedRunTracker? pendingObservedRun;
    DungeonRunTracker? pendingDungeonRun;
    Dictionary<int, long>? pendingGatheredObjects;
    long pendingOmittedGathers;
    readonly ActorCarryover actorCarryover = new();
    long ownershipSceneTime = -1;
    int lootMapId;
    long lootMapTime = -1;
    internal PetLevelNotice[] DrainPetNotices(long now) { lock (Gate) return petNotices.Drain(now); }
    readonly IGamePacketDecoder decoder;
    readonly PlayerProfileCache profiles;
    bool cachedLocalIdentity;
    PlayerStatReading? pendingStats;
    CapturedSkillSnapshot? pendingSkills;
    DaevanionSnapshot? pendingDaevanion;
    PetCollectionSnapshot? pendingPets;
    WingSnapshot? pendingWings;
    TitleSnapshot? pendingTitles;
    CharacterCollectionSnapshot? pendingCollections;
    EquippedGearSnapshot? pendingGear;
    bool skillsAwaitingIdentity;
    bool loginAwaitingMap;
    internal LoginReading? AccountCharacters { get; private set; }
    readonly AccountCharacterCache accountCharacters;
    long skillBoundaryTime = -1;
    long localCpTime = -1;
    readonly Queue<Fight> completed = new();
    Fight? detachedRunProgress;
    bool fightMapChanged;
    readonly object saveGate = new();
    readonly Dictionary<int, long> lastDeaths = new();
    const long IdleResetMs = 15000;
    const long RecoveryWindowMs = 300000, AddPhaseWindowMs = 120000;
    bool recoveryBlocked;
    long? lastCombat;
    long? lastPartyCombat;
    public bool HasRecentPartyCombat(long timestamp)
    {
        lock (Gate) return lastPartyCombat is long last && timestamp >= last && timestamp - last < 10000;
    }
    void ObservePartyCombat(long timestamp, int actor, int target)
    {
        bool Friendly(int id) => id > 0 && (id == Fight.LocalPlayer || Fight.Actors.GetValueOrDefault(id)?.Party == true);
        if (Friendly(actor) || Friendly(target)) lastPartyCombat = Math.Max(lastPartyCombat ?? timestamp, timestamp);
    }
    Fight? resumable;
    // One bounded continuation of the recovery candidate. Feed packets as they
    // arrive so stops can match pre-pause starts without keeping/replaying logs.
    CastOutcomeAccumulator? recoveryCasts;
    LootLedger? recoveryLoot;
    long resumeStoppedAt;
    HashSet<int> resumeTargets = new();
    HashSet<int> phaseAnchors = new();
    long unsupportedPayloads;
    long revision;
    long identityEvents;
    public long IdentityEvents { get { lock (Gate) return identityEvents; } }
    public long UnsupportedPayloads => Interlocked.Read(ref unsupportedPayloads);
    public string DecoderStatus => decoder.Target;
    public string DecoderDetails
    {
        get { lock (Gate) return decoder is EuGameDecoder eu ? $"records {eu.Records:N0}, unsupported {eu.UnknownRecords:N0}, rejected {eu.RejectedRecords:N0}, framing errors {eu.FramingErrors:N0}" : $"undecoded payloads {UnsupportedPayloads:N0}"; }
    }
    public Fight Fight { get; private set; } = new();
    public bool HasCompletedFights { get { lock (Gate) return completed.Count > 0 || detachedRunProgress != null; } }
    public CombatEngine(IGamePacketDecoder? decoder = null, PlayerProfileCache? profiles = null, DutyMissionLedger? dutyMissions = null, AccountCharacterCache? accountCharacters = null, ShopPurchaseTracker? shopPurchases = null, CraftingChecklist? crafting = null, string? visibilityMemoryPath = null, FarmingCounterLedger? gatheringTracking = null, FarmingCounterLedger? killTracking = null, FarmingCounterLedger? lootTracking = null, FarmingCounterLedger? soulTracking = null, FarmingCounterLedger? petRegistrationTracking = null, FarmingCounterLedger? petLevelTracking = null) { PetRegistrationTracking = petRegistrationTracking ?? new(); PetLevelTracking = petLevelTracking ?? new(); farmingPets = new(PetRegistrationTracking, PetLevelTracking); SoulTracking = soulTracking ?? new(); farmingSouls = new(SoulTracking); LootTracking = lootTracking ?? new(); farmingLoot = new(LootTracking); KillTracking = killTracking ?? new(); GatheringTracking = gatheringTracking ?? new(); InitializeVisibilityMemory(visibilityMemoryPath); Crafting = crafting ?? new(); ShopPurchases = shopPurchases ?? new(); this.accountCharacters = accountCharacters ?? new(); DutyMissions = dutyMissions ?? new(); this.decoder = decoder ?? new UnconfiguredGameDecoder(); this.profiles = profiles ?? new(); }
    void RefreshProfiles()
    {
        PlayerTelemetry.ApplyProfiles(Fight); profiles.Update(Fight);
        if (Fight.LocalPlayer == 0 || cachedLocalIdentity) {
            Fight.LocalPlayer = profiles.MatchLocal(Fight);
            cachedLocalIdentity = Fight.LocalPlayer > 0;
        }
    }

    public void Process(CapturedPayload payload)
    {
        lock (Gate)
        {
            var result = decoder.Decode(payload);
            if (result.State != DecodeState.Recognized) { Interlocked.Increment(ref unsupportedPayloads); return; }
            foreach (var item in result.Events) Apply(item);
        }
    }
    public void Apply(DecodedCombatEvent item)
    {
        lock (Gate)
        {
            switch (item.Kind)
            {
                case "actor" when item.Actor != null: UpdateActor(item.Actor); break;
                case "local-player" when item.Actor is { Player: true } local:
                    if (!skillsAwaitingIdentity) bossScene++;
                    // The local character/CP packet (33 36) is an explicit reset
                    // boundary, even if the server, entity ID and CP are unchanged.
                    if (!skillsAwaitingIdentity) {
                        FarmingKills.ClearScene();
                        if (Fight.LocalPlayer > 0) { farmingLoot.Reset(skillBoundaryTime); farmingPets.ResetSession(skillBoundaryTime); }
                    }
                    bool changedIdentity = Fight.LocalPlayer > 0 && (Fight.LocalPlayer != local.Id || !Fight.Actors.TryGetValue(local.Id, out var knownLocal) ||
                        knownLocal.Name != local.Name || knownLocal.PersistentId > 0 && local.PersistentId > 0 && knownLocal.PersistentId != local.PersistentId || (knownLocal.Server > 0 && local.Server > 0 && knownLocal.Server != local.Server));
                    // A prepared scene belongs to the incoming identity. Only
                    // discard a carried map when no identity handoff is pending.
                    if (!skillsAwaitingIdentity && Fight.Actors.TryGetValue(Fight.LocalPlayer, out var oldLocal) &&
                        (oldLocal.Name != local.Name || oldLocal.Server > 0 && local.Server > 0 && oldLocal.Server != local.Server ||
                         oldLocal.CharacterId > 0 && local.CharacterId > 0 && oldLocal.CharacterId != local.CharacterId)) { lootMapId = 0; Position.Clear(zone: true); }
                    if (Fight.HasRecordedData) completed.Enqueue(Snapshot());
                    if (detachedRunProgress != null) { completed.Enqueue(detachedRunProgress); detachedRunProgress = null; }
                    observedRun = pendingObservedRun ?? new(); pendingObservedRun = null;
                    dungeonRun = pendingDungeonRun ?? new(); pendingDungeonRun = null;
                    NewFight(Fight.Label); // Also invalidates any idle/add-phase recovery.
                    foreach (var carried in Fight.Actors.Values.Where(a => !a.Player && a.BossScene != bossScene).ToArray())
                        Fight.Actors[carried.Id] = carried with { CapturedBoss = false };
                    if (pendingGatheredObjects != null) {
                        Fight.GatheredObjects = pendingGatheredObjects; Fight.OmittedGathers = pendingOmittedGathers;
                        pendingGatheredObjects = null; pendingOmittedGathers = 0;
                    }
                    ClearSummonOwners(); ownershipSceneTime = -1;
                    Fight.LocalTarget = null;
                    LiveSceneRevision++;BossAlerts.Clear();
                    Fight.PartyBossTarget = null;
                    Fight.LocalResources = null;
                    FieldBosses.Clear();
                    petNotices.Clear();
                    var deferredInventory=inventoryLoot.Bind(local);
                    Fight.Energy = inventoryLoot.Energy;
                    BlackMarket.Follow(local);
                    AppearancePresets.Follow(local);
                    PartyFinder.Follow(local);
                    LiveCast.Clear(); liveCooldowns.Reset();
                    Abyss.Follow(local);
                    Siege.Follow(local.Server);
                    Position.Clear(zone: false);
                    Spatial.Clear(zone: false);
                    PositionPrediction.Clear(Spatial.MapId);
                    Fight.LocalPosition = null;
                    Fight.MixedRunContexts = false;
                    if (changedIdentity)
                    {
                        actorCarryover.Clear();
                        Fight.Pets = null;
                        Fight.Actors.Clear(); Fight.Specs.Clear(); Fight.Builds.Clear(); Fight.PartyProfiles = []; Fight.LocalPing = null; Fight.LocalStats = null; Fight.LocalResources = null; Fight.PartyKnown = false; Fight.PartySource = "Not captured"; Fight.PartyLeaderName = null; Fight.LocalPlayer = local.Id;
                    }
                    PartyPosition.Clear(zone: true);
                    Fight.LocalPlayer = local.Id; cachedLocalIdentity = false; profiles.RememberLocal(local);
                    UpdateActor(local);
                    if (pendingDutyMissions is { } pendingDuty) { DutyMissions.Apply(local.Server, pendingDuty.Reading, pendingDuty.Timestamp); pendingDutyMissions = null; }
                    Fight.LocalStats = pendingStats; pendingStats = null;
                    if (pendingSkills != null) {
                        Fight.Actors[local.Id] = Fight.Actors[local.Id] with { CapturedSkills = pendingSkills };
                        pendingSkills = null;
                    }
                    skillsAwaitingIdentity = false; loginAwaitingMap = false;
                    if (pendingDaevanion != null) {
                        Fight.Actors[local.Id] = Fight.Actors[local.Id] with { Daevanion = pendingDaevanion };
                        pendingDaevanion = null;
                    }
                    if (pendingPets != null) { Fight.Pets = pendingPets; pendingPets = null; }
                    if (pendingWings != null) { Fight.Actors[local.Id] = Fight.Actors[local.Id] with { Wings = pendingWings }; pendingWings = null; }
                    if (pendingTitles != null) { Fight.Actors[local.Id] = Fight.Actors[local.Id] with { Titles = pendingTitles }; pendingTitles = null; }
                    if (pendingCollections != null) { Fight.Actors[local.Id] = Fight.Actors[local.Id] with { Collections = pendingCollections }; pendingCollections = null; }
                    if (pendingGear != null) { Fight.Actors[local.Id] = Fight.Actors[local.Id] with { Gear = pendingGear }; pendingGear = null; }
                    foreach(var receipt in deferredInventory) {
                        if(receipt.Gain is {} gain)RecordInventoryGain(gain);
                        dungeonRun.RecordDeferred(receipt);
                    }
                    if(inventoryLoot.ConfirmedOmittedGains>0) {
                        Loot.RecordOmitted(inventoryLoot.ConfirmedOmittedGains);
                        (Fight.LiveLoot??=new()).RecordOmitted(inventoryLoot.ConfirmedOmittedGains);
                    }
                    foreach(var loss in inventoryLoot.ConfirmedLosses)dungeonRun.RecordDeferredLoss(loss);
                    foreach(var run in dungeonRun.FinishPending())completed.Enqueue(new Fight {
                        Id=run.Id,Label="Dungeon progress",LocalPlayer=local.Id,DungeonRun=run,
                        Actors=new(){[local.Id]=new(local.Id,local.Name,true,local.Party,local.Cp,local.Job,local.Server){PersistentId=local.PersistentId,CharacterId=local.CharacterId}}
                    });
                    if(dungeonRun.OmittedCompletedRuns>0)LocalLog.Write($"Pending dungeon history limit reached; {dungeonRun.OmittedCompletedRuns} earlier run summaries omitted.");
                    Fight.DungeonRun=dungeonRun.Current;
                    localCpTime = -1;
                    break;
                case "damage" when item.Damage != null: RecordDamage(item.Damage, false); break;
                case "heal" when item.Damage != null:
                    if(!HealingSkills.SummonInitialization(item.Damage))RecordDamage(item.Damage, true);
                    break;
                case "defense" when item.Defense != null:
                    var defense = item.Defense;
                    if (defense.SourceActorId == 0 && Fight.Actors.GetValueOrDefault(defense.ActorId) is { Player: false, OwnerId: > 0 } attacker)
                        defense = defense with { SourceActorId = defense.ActorId, ActorId = attacker.OwnerId.Value };
                    if (defense.SourceTargetId == 0 && Fight.Actors.GetValueOrDefault(defense.TargetId) is { Player: false, OwnerId: > 0 } defender)
                        defense = defense with { SourceTargetId = defense.TargetId, TargetId = defender.OwnerId.Value };
                    item = item with { Defense = defense };
                    BeginCombat(item.Defense.Timestamp, item.Defense.ActorId, item.Defense.TargetId, false);
                    ObservePartyCombat(item.Defense.Timestamp, item.Defense.ActorId, item.Defense.TargetId);
                    EnsureActor(item.Defense.ActorId); EnsureActor(item.Defense.TargetId); Fight.Defense.Add(item.Defense); revision++; break;
                case "energy" when item.Energy != null:
                    inventoryLoot.ApplyEnergy(item.Energy, cachedLocalIdentity || skillsAwaitingIdentity ? null : Fight.Actors.GetValueOrDefault(Fight.LocalPlayer));
                    Fight.Energy = inventoryLoot.Energy; revision++; break;
                case "player-stats" when item.Stats != null:
                    CharacterReadings.Validate(item.Stats);
                    if (item.Stats.Timestamp < skillBoundaryTime) break;
                    if (skillsAwaitingIdentity || cachedLocalIdentity || Fight.LocalPlayer == 0) {
                        if (pendingStats == null || item.Stats.Timestamp >= pendingStats.Timestamp) pendingStats = CharacterReadings.Copy(item.Stats);
                    }
                    else if (Fight.LocalStats == null || item.Stats.Timestamp >= Fight.LocalStats.Timestamp) {
                        Fight.LocalStats = CharacterReadings.Copy(item.Stats); revision++;
                    }
                    break;
                case "character-link" when item.Link is { EntityId: > 0, CharacterId: > 0 } link:
                    EnsureActor(link.EntityId);
                    if (Fight.Actors[link.EntityId].NpcId == 0) {
                        Fight.Actors[link.EntityId] = Fight.Actors[link.EntityId] with { CharacterId = link.CharacterId };
                        RefreshProfiles(); revision++;
                    }
                    break;
                case "party-profiles" when item.Profiles != null:
                    PlayerTelemetry.Validate(item.Profiles); Fight.PartyProfiles = [..item.Profiles]; RefreshProfiles(); revision++; break;
                case "ping" when item.Ping is { Milliseconds: >= 0 and <= 1000, Timestamp: >= 0, Source.Length: > 0 } ping:
                    if (Fight.LocalPing == null || ping.Timestamp >= Fight.LocalPing.Timestamp) { Fight.LocalPing = ping; revision++; }
                    break;
                case "party" when item.Party != null:
                    FightMetadata.SetRoster(Fight, item.Party); PartyFinder.Roster(item.Party.Members.Length);
                    if (item.Party.Timestamp >= skillBoundaryTime) {
                        if (pendingDungeonRun != null) pendingDungeonRun.RecordParty(item.Party);
                        else if (!skillsAwaitingIdentity && !cachedLocalIdentity) dungeonRun.RecordParty(item.Party);
                    }
                    if (DungeonRunTracker.Same(Fight.DungeonRun, dungeonRun.Current)) Fight.DungeonRun = dungeonRun.Current;
                    UpdateDetachedRunProgress(); revision++; break;
                case "build" when item.Build != null: FightMetadata.SetBuild(Fight, item.Build); revision++; break;
                case "network" when item.Network != null:
                    if (item.Network.Fields.ValueKind != System.Text.Json.JsonValueKind.Object) break;
                    if (item.Network.Kind == "hp" && item.Network.Timestamp >= skillBoundaryTime &&
                        item.Network.Fields.TryGetProperty("GroupKind", out var groupKind) &&
                        groupKind.ValueKind == System.Text.Json.JsonValueKind.String && groupKind.GetString() is "party" or "alliance") {
                        int memberId = NetworkEntity(item.Network, "EntityId");
                        int limit = groupKind.GetString() == "alliance" ? 64 : 10;
                        if (memberId > 0 && (Fight.Actors.GetValueOrDefault(memberId)?.Party == true || Fight.Actors.Values.Count(a => a.Party) < limit)) {
                            EnsureActor(memberId);
                            var member = Fight.Actors[memberId];
                            if (member.NpcId == 0 && member.OwnerId is null or 0) {
                                Fight.Actors[memberId] = member with { Player = true, Party = true,
                                    ProfileSource = member.Player ? member.ProfileSource : "Captured group health; identity pending" };
                                if (!Fight.PartyKnown) Fight.PartySource = "Partial roster from captured group health";
                            }
                        }
                    }
                    if (item.Network.Kind is "local-zone" or "account-login" && item.Network.Timestamp >= skillBoundaryTime)
                        liveCooldowns.Reset();
                    else if (!skillsAwaitingIdentity && !cachedLocalIdentity && Fight.LocalPlayer > 0 && item.Network.Timestamp >= skillBoundaryTime)
                        liveCooldowns.Capture(item.Network, Fight.LocalPlayer);
                    if (item.Network.Timestamp >= skillBoundaryTime && item.Network.Timestamp >= bossStateTime) {
                        var signal = item.Network;
                        if (signal.Kind is "local-zone" or "account-login") {
                            int nextMap = signal.Kind == "local-zone" ? NetworkEntity(signal, "MapId") : 0;
                            bool preparing = signal.Fields.TryGetProperty("AwaitingIdentity",out var preparingValue) && preparingValue.ValueKind == System.Text.Json.JsonValueKind.True;
                            // Preparation and confirmation describe the same arrival.
                            // NPCs can already have spawned between the two packets.
                            if (signal.Timestamp > bossStateTime && (signal.Kind == "account-login" || preparing || nextMap != bossMap)) bossScene++;
                            bossMap = nextMap;
                            bossStateTime = signal.Timestamp;
                        }
                        else if (!skillsAwaitingIdentity && !cachedLocalIdentity &&
                            (signal.Kind == "guard" || signal.Kind == "actor-position" && signal.Fields.TryGetProperty("VisibilityReason", out _)) &&
                            Fight.Actors.TryGetValue(NetworkEntity(signal, "ActorId"), out var guarded) && !guarded.Player && guarded.NpcId > 0 && guarded.BossScene == bossScene) {
                            bool boss = signal.Kind == "guard"
                                ? CapturedBoss.IsBoss(guarded) || !DungeonMaps.NamesBosses(bossMap)
                                : DungeonMaps.IsBoss(guarded.NpcId, bossMap);
                            Fight.Actors[guarded.Id] = guarded with { CapturedBoss = boss };
                            bossStateTime = signal.Timestamp;
                        }
                    }
                    if (item.Network.Timestamp >= skillBoundaryTime) staggerBreaks.Apply(item.Network, id =>
                        !skillsAwaitingIdentity && !cachedLocalIdentity && Fight.Actors.TryGetValue(id, out var npc) && !npc.Player &&
                        npc.BossScene == bossScene && CapturedBoss.IsBoss(npc) ? npc.NpcId : 0);
                    if (item.Network.Timestamp >= skillBoundaryTime) LiveCast.Apply(item.Network, skillsAwaitingIdentity || cachedLocalIdentity ? 0 : Fight.LocalPlayer);
                    item = item with { Network = SupportAttribution.Capture(item.Network, Fight) };
                    if (item.Network.Kind == "local-combat-power" && !skillsAwaitingIdentity && !cachedLocalIdentity && item.Network.Timestamp >= 0 &&
                        item.Network.Timestamp >= localCpTime && item.Network.Timestamp >= skillBoundaryTime &&
                        Fight.Actors.TryGetValue(Fight.LocalPlayer, out var cpActor) &&
                        item.Network.Fields.TryGetProperty("CombatPower", out var cpValue) && cpValue.ValueKind == System.Text.Json.JsonValueKind.Number && cpValue.TryGetInt32(out int currentCp) && currentCp >= 0) {
                        localCpTime = item.Network.Timestamp;
                        UpdateActor(cpActor with { Cp = currentCp, CpSource = "Local CP update" });
                    }
                    if (item.Network.Kind is "skill-levels" or "skill-choices" or "skill-chain" && item.Network.Timestamp >= skillBoundaryTime) {
                        if (!skillsAwaitingIdentity && !cachedLocalIdentity && Fight.LocalPlayer > 0 && Fight.Actors.TryGetValue(Fight.LocalPlayer, out var skilled))
                            Fight.Actors[skilled.Id] = skilled with { CapturedSkills = CapturedSkills.Apply(skilled.CapturedSkills, item.Network) };
                        else pendingSkills = CapturedSkills.Apply(pendingSkills, item.Network);
                    }
                    if (item.Network.Kind is "wings" or "wing-change" && item.Network.Timestamp >= skillBoundaryTime) {
                        bool confirmed = !skillsAwaitingIdentity && !cachedLocalIdentity && Fight.LocalPlayer > 0;
                        bool eligible = item.Network.Kind == "wings" || confirmed && item.Network.Fields.TryGetProperty("EntityId", out var wingOwner) && wingOwner.TryGetInt32(out int owner) && owner == Fight.LocalPlayer;
                        if (eligible) {
                            if (confirmed && Fight.Actors.TryGetValue(Fight.LocalPlayer, out var winged))
                                Fight.Actors[winged.Id] = winged with { Wings = Wings.Apply(winged.Wings, item.Network) };
                            else pendingWings = Wings.Apply(pendingWings, item.Network);
                        }
                    }
                    if (item.Network.Kind is "inventory" or "inventory-update" && item.Network.Timestamp >= skillBoundaryTime) {
                        if (!skillsAwaitingIdentity && !cachedLocalIdentity && Fight.Actors.TryGetValue(Fight.LocalPlayer, out var geared))
                            Fight.Actors[geared.Id] = geared with { Gear = EquippedGear.Apply(geared.Gear, item.Network) };
                        else pendingGear = EquippedGear.Apply(pendingGear, item.Network);
                    }
                    if (item.Network.Kind is "skins" or "decorations" or "monoliths" or "achievements" && item.Network.Timestamp >= skillBoundaryTime) {
                        if (!skillsAwaitingIdentity && !cachedLocalIdentity && Fight.Actors.TryGetValue(Fight.LocalPlayer, out var collector))
                            Fight.Actors[collector.Id] = collector with { Collections = CharacterCollections.Apply(collector.Collections, item.Network) };
                        else pendingCollections = CharacterCollections.Apply(pendingCollections, item.Network);
                    }
                    if (item.Network.Kind == "titles" && item.Network.Timestamp >= skillBoundaryTime) {
                        if (!skillsAwaitingIdentity && !cachedLocalIdentity && Fight.Actors.TryGetValue(Fight.LocalPlayer, out var titled))
                            Fight.Actors[titled.Id] = titled with { Titles = Titles.Apply(titled.Titles, item.Network) };
                        else pendingTitles = Titles.Apply(pendingTitles, item.Network);
                    }
                    if (item.Network.Kind == "daevanion" && item.Network.Timestamp >= skillBoundaryTime) {
                        if (!skillsAwaitingIdentity && !cachedLocalIdentity && Fight.Actors.TryGetValue(Fight.LocalPlayer, out var progressed))
                            Fight.Actors[progressed.Id] = progressed with { Daevanion = Daevanion.Apply(progressed.Daevanion, item.Network) };
                        else pendingDaevanion = Daevanion.Apply(pendingDaevanion, item.Network);
                    }
                    if (item.Network.Kind == "account-login" && item.Network.Timestamp > skillBoundaryTime) {
                        AccountCharacters = System.Text.Json.JsonSerializer.Deserialize<LoginReading>(item.Network.Fields);
                        if (AccountCharacters != null) accountCharacters.Apply(AccountCharacters, item.Network.Timestamp);
                        skillBoundaryTime = item.Network.Timestamp; skillsAwaitingIdentity = true; loginAwaitingMap = true;
                        pendingObservedRun = new(); pendingDungeonRun = new(true);
                        pendingGatheredObjects = new(); pendingOmittedGathers = 0;
                        LiveSceneRevision++;BossAlerts.Clear();
                        pendingStats = null; pendingSkills = null; pendingDaevanion = null; pendingPets = null; pendingWings = null; pendingTitles = null; pendingCollections = null; pendingGear = null; pendingDutyMissions = null;
                        inventoryLoot.Login(item.Network.Timestamp);
                        farmingLoot.Reset(item.Network.Timestamp);
                        farmingPets.ResetSession(item.Network.Timestamp);
                        lootMapId = 0; lootMapTime = item.Network.Timestamp;
                        WorldObjects.ResetSession(item.Network.Timestamp);
                        FarmingKills.Clear(item.Network.Timestamp);
                        MapEvents.ResetSession(item.Network.Timestamp);
                        FieldBosses.Clear();
                        Position.Clear(zone: true);
                        Fight.LocalPosition = null; Fight.LocalTarget = null; Fight.PartyBossTarget = null;
                        Spatial.Clear(zone: true);
                        PositionPrediction.Clear();
                        AppearancePresets.Clear(item.Network.Timestamp);
                        BlackMarket.Clear(); PartyFinder.AccountCharacters(item.Network.Timestamp); SessionSignals.Clear(item.Network.Timestamp);
                        Abyss.Clear(item.Network.Timestamp);
                        Siege.Clear();
                        Scoreboard.Clear(item.Network.Timestamp);
                    }
                    if (item.Network.Kind == "local-zone" && item.Network.Timestamp > skillBoundaryTime &&
                        item.Network.Fields.TryGetProperty("AwaitingIdentity", out var awaitingIdentity) && awaitingIdentity.ValueKind == System.Text.Json.JsonValueKind.True) {
                        // Initial login may deliver skills before map preparation.
                        // A later preparation supersedes pending data; a duplicate does not.
                        bool initialUnassigned = skillBoundaryTime < 0 && (Fight.LocalPlayer == 0 || cachedLocalIdentity);
                        if (!loginAwaitingMap) {
                            pendingObservedRun = new(); pendingDungeonRun = new(true);
                            pendingGatheredObjects = new(); pendingOmittedGathers = 0;
                        }
                        skillBoundaryTime = item.Network.Timestamp;
                        skillsAwaitingIdentity = true;
                        LiveSceneRevision++;BossAlerts.Clear();
                        Fight.LocalTarget = null;
                        if (!initialUnassigned && !loginAwaitingMap) { pendingDutyMissions = null; pendingStats = null; pendingSkills = null; pendingDaevanion = null; pendingPets = null; pendingWings = null; pendingTitles = null; pendingCollections = null; pendingGear = null; }
                        if (!initialUnassigned && !loginAwaitingMap) AppearancePresets.Clear(item.Network.Timestamp);
                        inventoryLoot.Prepare(item.Network.Timestamp); loginAwaitingMap = false;
                    }
                    if (item.Network.Kind == "visible-equipment" && item.Network.Fields.TryGetProperty("ActorId", out var equipmentId) &&
                        equipmentId.TryGetInt32(out int equipped) && equipped > 0) {
                        EnsureActor(equipped);
                        var actor = Fight.Actors[equipped];
                        var metadata = actor.Metadata;
                        if (actor.Player && item.Network.Timestamp >= 0 && item.Network.Fields.TryGetProperty("GearScore", out var score) &&
                            score.ValueKind == System.Text.Json.JsonValueKind.Number && score.TryGetInt32(out int gearScore) && gearScore >= 0)
                            metadata = PlayerMetadata.Merge(metadata, new(GearScore: gearScore, GearScoreAt: item.Network.Timestamp));
                        Fight.Actors[equipped] = actor with { VisibleEquipment = VisibleEquipment.Apply(actor.VisibleEquipment, item.Network), Metadata = metadata };
                    }
                    if (item.Network.Kind == "local-zone" && item.Network.Timestamp >= ownershipSceneTime) {
                        ClearSummonOwners(); ownershipSceneTime = item.Network.Timestamp;
                    }
                    if (item.Network.Kind == "duty-missions" && item.Network.Timestamp >= skillBoundaryTime) {
                        var duty = System.Text.Json.JsonSerializer.Deserialize<DutyMissionReading>(item.Network.Fields)!;
                        if (!skillsAwaitingIdentity && !cachedLocalIdentity && Fight.Actors.TryGetValue(Fight.LocalPlayer, out var dutyOwner))
                            DutyMissions.Apply(dutyOwner.Server, duty, item.Network.Timestamp);
                        else if (pendingDutyMissions == null || item.Network.Timestamp >= pendingDutyMissions.Value.Timestamp)
                            pendingDutyMissions = (duty, item.Network.Timestamp);
                    }
                    var previousPets = Fight.Pets;
                    if (item.Network.Timestamp >= skillBoundaryTime) {
                        if (skillsAwaitingIdentity || cachedLocalIdentity || Fight.LocalPlayer == 0)
                            pendingPets = PetCollection.Apply(pendingPets, item.Network);
                        else Fight.Pets = PetCollection.Apply(previousPets, item.Network, Fight.LocalPlayer);
                    }
                    petNotices.Observe(previousPets, Fight.Pets, item.Network.Kind);
                    if (item.Network.Timestamp >= skillBoundaryTime) {
                        Fight.LocalTarget = LocalTarget.Apply(Fight.LocalTarget, item.Network, skillsAwaitingIdentity || cachedLocalIdentity ? 0 : Fight.LocalPlayer);
                        Fight.PartyBossTarget = PartyBossTarget.Apply(Fight.PartyBossTarget, item.Network);
                    }
                    if (item.Network.Timestamp >= skillBoundaryTime) {
                        FieldBosses.Apply(item.Network);
                        if (item.Network.Kind == "field-boss-list" && LiveIdentityReady &&
                            Fight.Actors.GetValueOrDefault(Fight.LocalPlayer) is { Server: > 0 } bossObserver)
                            FieldBossCache.Capture(bossObserver.Server, FieldBosses.Snapshot());
                    }
                    SessionSignals.Apply(item.Network, Fight.LocalPlayer);
                    Siege.Apply(item.Network);
                    if (item.Network.Kind == "local-zone") Scoreboard.Clear(item.Network.Timestamp);
                    Scoreboard.Apply(item.Network);
                    if (item.Network.Kind != "abyss-season" || !skillsAwaitingIdentity && !cachedLocalIdentity && item.Network.Timestamp >= skillBoundaryTime) Abyss.Apply(item.Network);
                    if (item.Network.Kind == "local-zone" && item.Network.Timestamp >= skillBoundaryTime && NetworkEntity(item.Network, "MapId") > 0)
                        PartyFinder.Map();
                    if (item.Network.Timestamp >= skillBoundaryTime) PartyFinder.Apply(item.Network);
                    if (item.Network.Timestamp >= skillBoundaryTime) BlackMarket.Apply(item.Network, item.Network.Timestamp);
                    if (item.Network.Kind is "player-level" or "player-guild" && item.Network.Fields.TryGetProperty("EntityId", out var metadataId) &&
                        metadataId.TryGetInt32(out int metadataEntity) && Fight.Actors.TryGetValue(metadataEntity, out var metadataActor) && metadataActor.Player)
                        Fight.Actors[metadataEntity] = metadataActor with { Metadata = PlayerMetadata.Apply(metadataActor.Metadata, item.Network) };
                    Crafting.Apply(item.Network, skillsAwaitingIdentity || cachedLocalIdentity || item.Network.Timestamp < skillBoundaryTime ? null : Fight.Actors.GetValueOrDefault(Fight.LocalPlayer));
                    ShopPurchases.Apply(item.Network, skillsAwaitingIdentity || cachedLocalIdentity || item.Network.Timestamp < skillBoundaryTime ? null : Fight.Actors.GetValueOrDefault(Fight.LocalPlayer));
                    WorldObjects.Apply(item.Network);
                    if (item.Network.Timestamp >= skillBoundaryTime &&
                        FarmingKills.Apply(item.Network, Fight.Actors.GetValueOrDefault(NetworkEntity(item.Network, "EntityId"))?.Player == true) is int killedNpc)
                        KillTracking.Record(killedNpc);
                    var dungeonCheckpoint = MapEvents.Apply(item.Network);
                    AppearancePresets.Apply(item.Network);
                    if (item.Network.Kind is "appearance-current" or "appearance-presets" or "wing-change" or "visible-equipment" &&
                        !skillsAwaitingIdentity && !cachedLocalIdentity && item.Network.Timestamp >= skillBoundaryTime && Fight.Actors.TryGetValue(Fight.LocalPlayer, out var appearanceOwner))
                        accountCharacters.ApplyAppearance(appearanceOwner, AppearancePresets.Snapshot());
                    if (item.Network.Timestamp >= skillBoundaryTime) { farmingSouls.Apply(item.Network); farmingPets.Apply(item.Network, Fight.LocalPlayer); }
                    farmingLoot.Apply(item.Network);
                    GatheringTracking.Apply(WorldObjects, item.Network);
                    if (pendingGatheredObjects != null)
                        pendingOmittedGathers = Gathering.Apply(pendingGatheredObjects, pendingOmittedGathers, WorldObjects, item.Network);
                    else Gathering.Apply(Fight, WorldObjects, item.Network);
                    BossTelegraphs.Apply(Fight, item.Network);
                    if(item.Network.Timestamp>=skillBoundaryTime)BossAlerts.Apply(item.Network,Fight);
                    long spatialBoundary = Spatial.Boundary;
                    if (item.Network.Timestamp >= skillBoundaryTime)
                        Spatial.Apply(item.Network, skillsAwaitingIdentity || cachedLocalIdentity ? 0 : Fight.LocalPlayer);
                    if (spatialBoundary != Spatial.Boundary) PositionPrediction.Clear(Spatial.MapId);
                    if (item.Network.Timestamp >= skillBoundaryTime) {
                        if (item.Network.Kind == "account-login") PartyPosition.Clear(zone: true);
                        else PartyPosition.Apply(item.Network, Fight.LocalPlayer);
                    }
                    if (item.Network.Timestamp >= skillBoundaryTime)
                        Position.Apply(item.Network, skillsAwaitingIdentity || cachedLocalIdentity ? 0 : Fight.LocalPlayer);
                    if (!skillsAwaitingIdentity && !cachedLocalIdentity && item.Network.Timestamp >= skillBoundaryTime) {
                        PositionPrediction.BeginNetworkEvent(item.Network.Timestamp);
                        try {
                        PositionPrediction.ApplyFlight(item.Network, Fight.LocalPlayer);
                        if (Position.Latest is { } measuredPosition) PositionPrediction.Observe(measuredPosition);
                        if (Spatial.LastConstraint is { } proximity) PositionPrediction.Constrain(proximity);
                        PositionPrediction.ApplyCast(item.Network, Fight.LocalPlayer, Spatial);
                        PositionPrediction.ApplyVisibility(item.Network, Spatial, Position.Latest);
                        } finally { PositionPrediction.EndNetworkEvent(item.Network.Timestamp); }
                    }
                    if (!skillsAwaitingIdentity && !cachedLocalIdentity && item.Network.Timestamp >= skillBoundaryTime) {
                        Fight.LocalStats = CharacterReadings.Update(Fight.LocalStats, item.Network, Fight.LocalPlayer);
                        Fight.LocalResources = ResourceSnapshots.Apply(Fight.LocalResources, item.Network, Fight.LocalPlayer);
                    }
                    Fight.LocalPosition = Position.Latest;
                    var previousDungeon = dungeonRun.Current;
                    // Identity confirmation clears tracker clocks, but delayed packets
                    // still belong to the previous session. Keep the engine boundary.
                    if (item.Network.Timestamp >= skillBoundaryTime) {
                        (pendingObservedRun ?? observedRun).Apply(item.Network);
                        (pendingDungeonRun ?? dungeonRun).Apply(item.Network);
                        (pendingDungeonRun ?? dungeonRun).RecordSplit(dungeonCheckpoint);
                    }
                    if (item.Network.Kind == "local-zone" && Fight.HasEvents && item.Network.Timestamp >= lootMapTime &&
                        NetworkEntity(item.Network, "MapId") is > 0 and var changedMap && (changedMap != lootMapId || !DungeonRunTracker.Same(previousDungeon, dungeonRun.Current))) fightMapChanged = true;
                    if (!Fight.HasEvents && Fight.DungeonRun?.HasProgress == true && !DungeonRunTracker.Same(Fight.DungeonRun, dungeonRun.Current)) {
                        // A player can leave before the next autosave. Preserve the
                        // completed run under its own file ID before idle metadata changes.
                        var completedRun = Snapshot(); completedRun.Id = Guid.NewGuid().ToString("N"); completed.Enqueue(completedRun);
                    }
                    if (!Fight.HasEvents || Fight.DungeonRun == null && !fightMapChanged || DungeonRunTracker.Same(Fight.DungeonRun, dungeonRun.Current)) Fight.DungeonRun = dungeonRun.Current;
                    // A zone exit after the last hit must not erase that fight's
                    // attribution. Only subsequent combat can mix its contexts.
                    if (!Fight.HasEvents || ObservedRunTracker.Same(Fight.ObservedRun, observedRun.Current)) Fight.ObservedRun = observedRun.Current;
                    if (item.Network.Kind == "local-zone" && item.Network.Timestamp >= 0 && item.Network.Timestamp >= lootMapTime &&
                        item.Network.Fields.TryGetProperty("MapId", out var mapValue) && mapValue.ValueKind == System.Text.Json.JsonValueKind.Number && mapValue.TryGetInt32(out int map) && map > 0) {
                        lootMapId = map; lootMapTime = item.Network.Timestamp;
                    }
                    var received = inventoryLoot.Apply(item.Network, cachedLocalIdentity || skillsAwaitingIdentity ? null : Fight.Actors.GetValueOrDefault(Fight.LocalPlayer),lootMapId,(pendingDungeonRun??dungeonRun).Current?.Id);
                    Fight.Energy = inventoryLoot.Energy;
                    foreach (var attributed in received) {
                        var pickup = attributed with { MapId = lootMapId };
                        RecordInventoryGain(pickup);
                    }
                    dungeonRun.RecordUses(item.Network.Timestamp, inventoryLoot.Uses);
                    dungeonRun.RecordRewards(item.Network.Timestamp, received);
                    if (!Fight.HasEvents || Fight.DungeonRun == null && !fightMapChanged || DungeonRunTracker.Same(Fight.DungeonRun, dungeonRun.Current)) Fight.DungeonRun = dungeonRun.Current;
                    UpdateDetachedRunProgress();
                    if (item.Network.Kind == "entity-death" && item.Network.Timestamp >= 0 &&
                        item.Network.Fields.TryGetProperty("EntityId", out var deathId) && deathId.ValueKind == System.Text.Json.JsonValueKind.Number && deathId.TryGetInt32(out int dead) && dead > 0 &&
                        (!lastDeaths.TryGetValue(dead, out long previousDeath) || item.Network.Timestamp - previousDeath > 1000)) {
                        Fight.Deaths.Add(new(dead, item.Network.Timestamp)); lastDeaths[dead] = item.Network.Timestamp;
                        resumeTargets.Remove(dead); phaseAnchors.Remove(dead);
                    }
                    if (item.Network.Kind is "cast-start" or "charge-start" or "cast-stop" or "charge-stop") {
                        (Fight.LiveCasts ??= new()).Apply(item.Network);
                        recoveryCasts?.Apply(item.Network);
                    } else networkRetention.Add(Fight.Network, item.Network);
                    revision++; break;
                default: throw new ArgumentException("Incomplete or unsupported decoded event.");
            }
            if (item.Kind is "actor" or "local-player" or "character-link" or "party-profiles") CheckTrainingIdentity();
        }
    }
    void ClearSummonOwners()
    {
        foreach (var actor in Fight.Actors.Values.Where(a => a.OwnerId > 0).ToArray())
            Fight.Actors[actor.Id] = actor with { OwnerId = 0 };
    }
    void UpdateActor(Actor actor)
    {
        if (actor.Id <= 0 || actor.NpcId < 0 || actor.OwnerId < 0 || actor.OwnerId == actor.Id || (actor.NpcId > 0 && actor.Player) || string.IsNullOrWhiteSpace(actor.Name)) throw new ArgumentException("Invalid actor identity.");
        if (Fight.Actors.TryGetValue(actor.Id, out var previous))
        {
            bool replaced = previous.Server > 0 && actor.Server > 0 && previous.Server != actor.Server || previous.Player && actor.Player && previous.Name != actor.Name && previous.ProfileSource != "Captured group health; identity pending" ||
                previous.PersistentId > 0 && actor.PersistentId > 0 && previous.PersistentId != actor.PersistentId ||
                previous.NpcId > 0 && actor.NpcId > 0 && previous.NpcId != actor.NpcId ||
                previous.Player && actor.NpcId > 0 || previous.NpcId > 0 && actor.Player ||
                previous.OwnerId.HasValue && actor.OwnerId.HasValue && previous.OwnerId != actor.OwnerId;
            if (replaced)
            {
                BossAlerts.Remove(actor.Id);
                if (Fight.LocalTarget?.EntityId == actor.Id) Fight.LocalTarget = new(0, Fight.LocalTarget.Timestamp);
                if (Fight.PartyBossTarget?.EntityId == actor.Id) Fight.PartyBossTarget = new(0, Fight.PartyBossTarget.Timestamp);
                resumeTargets.Remove(actor.Id); phaseAnchors.Remove(actor.Id);
                // A reused entity ID must not rename a different creature/player in saved hits.
                bool participated = Fight.LiveCasts?.HasActor(actor.Id) == true || recoveryCasts?.HasActor(actor.Id) == true || Fight.Damage.Concat(Fight.Healing).Any(e => e.ActorId == actor.Id || e.TargetId == actor.Id || e.SourceActorId == actor.Id) ||
                    Fight.Telegraphs.Any(t => t.EntityId == actor.Id) ||
                    Fight.Actors.Values.Any(a => a.OwnerId == actor.Id) ||
                    Fight.Network.Any(e => SupportAttribution.Captured(e) == actor.Id) ||
                    Fight.Defense.Any(e => e.ActorId == actor.Id || e.TargetId == actor.Id || e.SourceActorId == actor.Id || e.SourceTargetId == actor.Id) || Fight.Deaths.Any(e => e.EntityId == actor.Id);
                if (participated) ArchiveActor(actor.Id);
                Fight.Builds.Remove(actor.Id);
            }
            else
            {
                if (previous.Cp > 0 && (previous.CpSource == "Entered locally" ||
                    (previous.CpSource == "Unknown" && previous.ProfileSource == "Entered locally") ||
                    (actor.Id == Fight.LocalPlayer && previous.CpSource is "Local character packet" or "Local CP update" && actor.CpSource is "Captured party" or "Player spawn packet")))
                    actor = actor with { Cp = previous.Cp, CpSource = previous.CpSource };
                actor = actor with { Player = actor.Player || previous.Player, Party = actor.Party || previous.Party,
                    Cp = actor.Cp > 0 || actor.CpSource is "Captured party" or "Player spawn packet" or "Local character packet" or "Local CP update" ? actor.Cp : previous.Cp, Job = actor.Job == "Unknown" ? previous.Job : actor.Job,
                    CpSource = actor.Cp > 0 || actor.CpSource is "Captured party" or "Player spawn packet" or "Local character packet" or "Local CP update" ? actor.CpSource : previous.CpSource,
                    CharacterId = actor.CharacterId > 0 ? actor.CharacterId : previous.CharacterId,
                    PersistentId = actor.PersistentId > 0 ? actor.PersistentId : previous.PersistentId,
                    Server = actor.Server > 0 ? actor.Server : previous.Server, NpcId = actor.NpcId > 0 ? actor.NpcId : previous.NpcId,
                    OwnerId = actor.OwnerId ?? previous.OwnerId,
                    CapturedBoss = actor.CapturedBoss ?? (previous.BossScene == bossScene ? previous.CapturedBoss : null),
                    Metadata = PlayerMetadata.Merge(previous.Metadata, actor.Metadata),
                    VisibleEquipment = actor.VisibleEquipment ?? previous.VisibleEquipment,
                    CapturedSkills = actor.CapturedSkills ?? previous.CapturedSkills,
                    Daevanion = actor.Daevanion ?? previous.Daevanion,
                    Wings = actor.Wings ?? previous.Wings,
                    Titles = actor.Titles ?? previous.Titles,
                    Collections = actor.Collections ?? previous.Collections,
                    Gear = actor.Gear ?? previous.Gear,
                    ProfileSource = previous.ProfileSource == "Captured group health; identity pending" && actor.Player ? actor.ProfileSource :
                        actor.Cp == 0 && actor.Job == "Unknown" ? previous.ProfileSource : actor.ProfileSource };
            }
        }
        actorCarryover.Observe(actor.Id);
        if (!actor.Player && actor.NpcId > 0 && actor.CapturedBoss == null)
            actor = actor with { CapturedBoss = DungeonMaps.IsBoss(actor.NpcId, bossMap) };
        actor = actor with { BossScene = bossScene };
        Fight.Actors[actor.Id] = actor; RefreshProfiles(); identityEvents++; revision++;
    }
    void ArchiveActor(int id)
    {
        int archived = int.MaxValue;
        while (Fight.Actors.ContainsKey(archived) || resumable?.Actors.ContainsKey(archived) == true) archived--;
        staggerBreaks.ArchiveActor(id, archived);
        Fight.LiveCasts?.ArchiveActor(id, archived);
        recoveryCasts?.ArchiveActor(id, archived);
        Fight.Actors[archived] = Fight.Actors[id] with { Id = archived, OriginalEntityId = id, Party = false };
        foreach (var child in Fight.Actors.Values.Where(a => a.OwnerId == id).ToArray())
            Fight.Actors[child.Id] = child with { OwnerId = archived };
        for (int i = 0; i < Fight.Network.Count; i++)
            if (SupportAttribution.Captured(Fight.Network[i]) == id)
                Fight.Network[i] = SupportAttribution.WithCaster(Fight.Network[i], archived);
        for (int i = 0; i < Fight.Damage.Count; i++) {
            var e = Fight.Damage[i];
            if (e.ActorId == id || e.TargetId == id || e.SourceActorId == id) Fight.Damage[i] = e with { ActorId = e.ActorId == id ? archived : e.ActorId, TargetId = e.TargetId == id ? archived : e.TargetId, SourceActorId = e.SourceActorId == id ? archived : e.SourceActorId };
        }
        for (int i = 0; i < Fight.Healing.Count; i++) {
            var e = Fight.Healing[i];
            if (e.ActorId == id || e.TargetId == id || e.SourceActorId == id) Fight.Healing[i] = e with { ActorId = e.ActorId == id ? archived : e.ActorId, TargetId = e.TargetId == id ? archived : e.TargetId, SourceActorId = e.SourceActorId == id ? archived : e.SourceActorId };
        }
        for (int i = 0; i < Fight.Defense.Count; i++) {
            var e = Fight.Defense[i];
            if (e.ActorId == id || e.TargetId == id || e.SourceActorId == id || e.SourceTargetId == id)
                Fight.Defense[i] = e with { ActorId = e.ActorId == id ? archived : e.ActorId, TargetId = e.TargetId == id ? archived : e.TargetId,
                    SourceActorId = e.SourceActorId == id ? archived : e.SourceActorId, SourceTargetId = e.SourceTargetId == id ? archived : e.SourceTargetId };
        }
        for (int i = 0; i < Fight.Deaths.Count; i++)
            if (Fight.Deaths[i].EntityId == id) Fight.Deaths[i] = Fight.Deaths[i] with { EntityId = archived };
        for (int i = 0; i < Fight.Telegraphs.Count; i++)
            if (Fight.Telegraphs[i].EntityId == id) Fight.Telegraphs[i] = Fight.Telegraphs[i] with { EntityId = archived };
        lastDeaths.Remove(id);
        if (Fight.Builds.Remove(id, out var build)) Fight.Builds[archived] = FightMetadata.Copy(build) with { ActorId = archived };
    }
    internal void RecordDamage(DamageEvent damage, bool healing)
    {
        if (damage.Timestamp < 0 || damage.Damage < 0 || damage.SourceActorId < 0) throw new ArgumentOutOfRangeException(nameof(damage));
        lock (Gate)
        {
            if (damage.SourceActorId == 0 && Fight.Actors.GetValueOrDefault(damage.ActorId) is { Player: false, OwnerId: > 0 } summon && summon.OwnerId != damage.ActorId) {
                EnsureActor(damage.ActorId);
                damage = damage with { SourceActorId = damage.ActorId, ActorId = summon.OwnerId.Value };
            }
            if (!healing && !skillsAwaitingIdentity && !cachedLocalIdentity && damage.Timestamp >= skillBoundaryTime)
                FarmingKills.Hit(damage.TargetId, damage.Timestamp, FightMetadata.InParty(Fight, damage.ActorId));
            BeginCombat(damage.Timestamp, damage.ActorId, damage.TargetId, healing);
            if (!healing && !skillsAwaitingIdentity && !cachedLocalIdentity) {
                Spatial.Combat(damage, Fight.LocalPlayer);
                if (Spatial.LastConstraint is { } proximity) PositionPrediction.Constrain(proximity);
            }
            if (Fight.LocalPlayer > 0 && damage.ActorId == Fight.LocalPlayer && !skillsAwaitingIdentity && !cachedLocalIdentity) {
                dungeonRun.RecordSkill(damage.Timestamp, damage.SkillCode);
                if (DungeonRunTracker.Same(Fight.DungeonRun, dungeonRun.Current)) Fight.DungeonRun = dungeonRun.Current;
                UpdateDetachedRunProgress();
            }
            if (!healing && damage.Damage > 0) ObservePartyCombat(damage.Timestamp, damage.ActorId, damage.TargetId);
            EnsureActor(damage.ActorId); EnsureActor(damage.TargetId);
            ObserveTraining(damage, healing);
            (healing ? Fight.Healing : Fight.Damage).Add(damage with { IsHeal = healing });
            revision++;
        }
    }
    void EnsureActor(int id) {
        if (id <= 0) return;
        actorCarryover.Observe(id);
        if (!Fight.Actors.ContainsKey(id)) Fight.Actors.Add(id, new(id, $"Entity {id}", false, false, 0, "Unknown", 0));
    }
    public Fight Snapshot()
    {
        lock (Gate) {
            profiles.Update(Fight, inferClasses: true); var copy = Fight.Copy();
            copy.LiveCooldowns = liveCooldowns.Copy();
            if (copy.HasEvents) (copy.StaggerBreaks, copy.StaggerBreaksTruncated) = staggerBreaks.Snapshot(copy.Start, copy.End);
            return copy;
        }
    }
    // UI-owned snapshots are read-only. Reuse one until the engine changes instead of
    // copying every event list every refresh, including when nothing was captured.
    public Fight SnapshotIfChanged(Fight? previous, ref long observedRevision)
    {
        lock (Gate)
        {
            if (previous != null && observedRevision == revision) return previous;
            var snapshot = Snapshot(); observedRevision = revision; return snapshot;
        }
    }
    public void SetLabel(string label) { lock (Gate) { Fight.Label = label; revision++; } }
    public void SetMetadata(Fight edited)
    {
        lock (Gate)
        {
            if (Fight.Id != edited.Id) throw new InvalidOperationException("The fight changed while editing. Open character details again for the current fight.");
            foreach (var (id, actor) in edited.Actors)
            {
                if (!Fight.Actors.TryGetValue(id, out var current) || current.Name != actor.Name || current.NpcId != actor.NpcId)
                    throw new InvalidOperationException("A character identity changed while editing. Open character details again.");
            }
            foreach (var (id, actor) in edited.Actors) Fight.Actors[id] = actor;
            Fight.Builds = edited.Builds.ToDictionary(p => p.Key, p => FightMetadata.Copy(p.Value));
            Fight.Specs = edited.Specs.ToDictionary(p => p.Key, p => p.Value with { Choices = p.Value.Choices.ToArray() });
            Fight.PartyKnown = edited.PartyKnown; Fight.PartySource = edited.PartySource; Fight.PartyLeaderName = edited.PartyLeaderName; revision++;
        }
    }
    public void NewFight(string label)
    {
        lock (Gate)
        {
            profiles.Update(Fight, inferClasses: true);
            var previous = Fight; fightMapChanged = false;
            var pets = previous.Pets;
            Fight = new() { Label = label, LocalPlayer = previous.LocalPlayer, Actors = actorCarryover.Select(previous), Specs = new(previous.Specs), Energy = previous.Energy, PartyProfiles = [..previous.PartyProfiles], LocalPing = previous.LocalPing, LocalStats = CharacterReadings.Copy(previous.LocalStats), LocalResources = previous.LocalResources,
                Builds = previous.Builds.Where(p => previous.Actors.GetValueOrDefault(p.Key)?.OriginalEntityId == 0).ToDictionary(p => p.Key, p => FightMetadata.Copy(p.Value)), PartyKnown = previous.PartyKnown, PartySource = previous.PartySource, PartyLeaderName = previous.PartyLeaderName };
            lastDeaths.Clear(); lastCombat = null; lastPartyCombat = null; recoveryBlocked = false; resumable = null; recoveryCasts = null; recoveryLoot = null; resumeTargets.Clear(); phaseAnchors.Clear();
            Fight.Pets = pets;
            networkRetention.Reset();BossAlerts.Clear();
            Fight.LocalTarget = previous.LocalTarget;
            Fight.PartyBossTarget = previous.PartyBossTarget;
            Fight.LocalPosition = previous.LocalPosition;
            fightMapChanged = false;
        Fight.ObservedRun = observedRun.Current;
            Fight.DungeonRun = dungeonRun.Current;
            revision++;
        }
    }
    public void EndFight(string label)
    {
        lock (Gate)
        {
            if (Fight.HasRecordedData) completed.Enqueue(Snapshot());
            NewFight(label);
        }
    }
    // The live UI calls this even when no packets arrive. Replay uses event timestamps.
    public void AdvanceTime(long timestamp)
    {
        lock (Gate)
        {
            AdvanceTraining(timestamp);
            if (lastCombat == null || timestamp - lastCombat.Value < IdleResetMs || !Fight.HasEvents) return;
            // A healing-only pause may also clear, without losing its contribution
            // or extending the boss recovery window indefinitely.
            if (resumable != null && Fight.Damage.Count == 0 && Fight.Defense.Count == 0 &&
                !Fight.MixedRunContexts && !resumable.MixedRunContexts && ObservedRunTracker.Same(Fight.ObservedRun, resumable.ObservedRun) && DungeonRunTracker.Same(Fight.DungeonRun, resumable.DungeonRun)) {
                var pending = resumable.Copy();
                pending.LiveCasts = recoveryCasts?.Copy();
                pending.LiveLoot = recoveryLoot?.Copy();
                pending.Pets = Fight.Pets;
                pending.LocalTarget = Fight.LocalTarget;
                pending.PartyBossTarget = Fight.PartyBossTarget;
                pending.LocalPosition = Fight.LocalPosition;
                pending.ObservedRun = Fight.ObservedRun; pending.DungeonRun = Fight.DungeonRun;
                pending.Healing.AddRange(Fight.Healing); pending.Deaths.AddRange(Fight.Deaths); NetworkSnapshotRetention.Merge(pending.Network, Fight.Network);
                Gathering.Merge(pending, Fight);
                BossTelegraphs.Merge(pending, Fight);
                foreach (var entry in Fight.Actors) pending.Actors[entry.Key] = entry.Value;
                (pending.StaggerBreaks, pending.StaggerBreaksTruncated) = staggerBreaks.Snapshot(pending.Start, pending.End);
                var keptTargets = resumeTargets; var keptAnchors = phaseAnchors; long stopped = resumeStoppedAt;
                completed.Enqueue(pending);
                // NewFight clears these sets, so retain independent copies.
                keptTargets = new(keptTargets); keptAnchors = new(keptAnchors);
                NewFight(Fight.Label);
                resumable = pending; recoveryCasts = pending.LiveCasts?.Copy() ?? new(); recoveryLoot = pending.LiveLoot?.Copy() ?? new(); resumeTargets = keptTargets; phaseAnchors = keptAnchors; resumeStoppedAt = stopped;
                return;
            }
            var previous = Snapshot();
            bool Friendly(int id) => id == previous.LocalPlayer || previous.Actors.GetValueOrDefault(id)?.Party == true;
            var targets = previous.Damage.Select(e => (e.ActorId, e.TargetId))
                .Concat(previous.Defense.Select(e => (e.ActorId, e.TargetId)))
                .Where(e => previous.LocalPlayer == 0 || Friendly(e.ActorId) || Friendly(e.TargetId))
                .SelectMany(e => Friendly(e.TargetId) ? new[] { e.ActorId } : new[] { e.TargetId })
                .Where(id => id > 0 && !Friendly(id) && previous.Actors.GetValueOrDefault(id)?.OriginalEntityId == 0)
                .Except(previous.Deaths.Select(d => d.EntityId)).ToHashSet();
            bool blocked = recoveryBlocked; long stoppedAt = lastCombat.Value;
            completed.Enqueue(previous);
            NewFight(previous.Label);
            if (blocked) return;
            resumable = previous; recoveryCasts = previous.LiveCasts?.Copy() ?? new(); recoveryLoot = previous.LiveLoot?.Copy() ?? new(); resumeTargets = targets; resumeStoppedAt = stoppedAt;
            // Cast records are NPC activity hints, not an authoritative boss flag.
            // Sustained combat also allows recovery when those hints were not captured.
            bool sustained = previous.Seconds >= 30;
            var hints = previous.Network.Select(e => e.Kind switch {
                "boss-cast" => NetworkEntity(e, "CasterId"), "boss-enrage" => NetworkEntity(e, "EntityId"), _ => 0
            }).Where(id => id > 0).ToHashSet();
            phaseAnchors = targets.Where(id => previous.Actors.GetValueOrDefault(id) is { NpcId: > 0 } &&
                (sustained || hints.Contains(id) || CapturedBoss.IsBoss(previous.Actors[id]))).ToHashSet();
        }
    }
    static int NetworkEntity(NetworkEvent e, string key) =>
        e.Fields.ValueKind == System.Text.Json.JsonValueKind.Object && e.Fields.TryGetProperty(key, out var value) &&
        value.ValueKind == System.Text.Json.JsonValueKind.Number && value.TryGetInt32(out int id) ? id : 0;

    void BeginCombat(long timestamp, int actor, int target, bool healing)
    {
        AdvanceTime(timestamp);
        if (Fight.HasEvents && (!ObservedRunTracker.Same(Fight.ObservedRun, observedRun.Current) || !DungeonRunTracker.Same(Fight.DungeonRun, dungeonRun.Current))) Fight.MixedRunContexts = true;
        fightMapChanged = false;
        Fight.ObservedRun = observedRun.Current;
            Fight.DungeonRun = dungeonRun.Current;
        bool Friendly(int id) => id == Fight.LocalPlayer || Fight.Actors.GetValueOrDefault(id)?.Party == true;
        // Nearby fights must not choose the next encounter for our party.
        if (Fight.LocalPlayer > 0 && !Friendly(actor) && !Friendly(target)) return;
        if (!healing && resumable != null)
        {
            long gap = timestamp - resumeStoppedAt;
            int hostile = Friendly(target) ? actor : target;
            var catalog = BossCatalogue.Default;
            int hostileNpc = Fight.Actors.GetValueOrDefault(hostile)?.NpcId ?? 0;
            var hostileActor = Fight.Actors.GetValueOrDefault(hostile);
            var nextBoss = catalog.Find(hostileNpc);
            bool differentBoss = hostileActor != null && CapturedBoss.IsBoss(hostileActor) && !phaseAnchors.Any(id =>
                resumable.Actors.GetValueOrDefault(id) is { } anchor && CapturedBoss.IsBoss(anchor) &&
                (nextBoss != null ? catalog.Find(anchor.NpcId)?.Key == nextBoss.Key : anchor.NpcId == hostileNpc));
            bool possibleAdd = !differentBoss && phaseAnchors.Count > 0 && gap <= AddPhaseWindowMs &&
                (Friendly(actor) || Friendly(target)) && !Friendly(hostile) &&
                Fight.Actors.GetValueOrDefault(hostile)?.Player != true;
            if (!Fight.MixedRunContexts && !resumable.MixedRunContexts && ObservedRunTracker.Same(Fight.ObservedRun, resumable.ObservedRun) && DungeonRunTracker.Same(Fight.DungeonRun, resumable.DungeonRun) &&
                gap <= RecoveryWindowMs && ((!Friendly(actor) && resumeTargets.Contains(actor)) || (!Friendly(target) && resumeTargets.Contains(target)) || possibleAdd))
            {
                var current = Fight;
                Fight = resumable.Copy();
                Fight.LiveCasts = recoveryCasts?.Copy();
                Fight.LiveLoot = recoveryLoot?.Copy();
                foreach (var entry in current.Actors) Fight.Actors[entry.Key] = entry.Value;
                Fight.LocalPlayer = current.LocalPlayer; Fight.PartyKnown = current.PartyKnown; Fight.PartySource = current.PartySource; Fight.PartyLeaderName = current.PartyLeaderName;
                Fight.PartyProfiles = current.PartyProfiles; Fight.LocalPing = current.LocalPing; Fight.LocalStats = current.LocalStats; Fight.LocalResources = current.LocalResources;
                Fight.Energy = current.Energy; Fight.Specs = current.Specs; Fight.Label = current.Label;
                Fight.Pets = current.Pets;
                Fight.LocalTarget = current.LocalTarget;
                Fight.PartyBossTarget = current.PartyBossTarget;
                Fight.LocalPosition = current.LocalPosition;
                Fight.ObservedRun = current.ObservedRun; Fight.DungeonRun = current.DungeonRun;
                foreach (var entry in current.Builds) Fight.Builds[entry.Key] = entry.Value;
                Fight.Damage.AddRange(current.Damage); Fight.Healing.AddRange(current.Healing); Fight.Defense.AddRange(current.Defense);
                Fight.Deaths.AddRange(current.Deaths); NetworkSnapshotRetention.Merge(Fight.Network, current.Network);
                Gathering.Merge(Fight, current);
                BossTelegraphs.Merge(Fight, current);
                networkRetention.Reset();BossAlerts.Clear();
                lastDeaths.Clear();
                foreach (var death in Fight.Deaths) lastDeaths[death.EntityId] = Math.Max(lastDeaths.GetValueOrDefault(death.EntityId), death.Timestamp);
                revision++;
            }
            // The next hostile exchange either resumes this encounter or commits a new one.
            resumable = null; recoveryCasts = null; recoveryLoot = null; resumeTargets.Clear(); phaseAnchors.Clear();
        }
        lastCombat = Math.Max(lastCombat ?? timestamp, timestamp);
    }
    void UpdateDetachedRunProgress()
    {
        var run = dungeonRun.Current;
        if (detachedRunProgress != null && !DungeonRunTracker.Same(detachedRunProgress.DungeonRun, run)) {
            completed.Enqueue(detachedRunProgress); detachedRunProgress = null;
        }
        if (run?.HasProgress != true) return;
        if (!Fight.MixedRunContexts && DungeonRunTracker.Same(Fight.DungeonRun, run)) {
            if (DungeonRunTracker.Same(detachedRunProgress?.DungeonRun, run)) detachedRunProgress = null;
            return;
        }
        if (!Fight.Actors.TryGetValue(Fight.LocalPlayer, out var owner) || !owner.Player) return;
        // A new run can receive rewards while the UI still shows combat from a
        // previous map. Keep a separate compact record, never relabel old hits.
        detachedRunProgress = new Fight { Id = run.Id, Label = "Dungeon progress", LocalPlayer = owner.Id, DungeonRun = run,
            Actors = new() { [owner.Id] = new(owner.Id, owner.Name, true, owner.Party, owner.Cp, owner.Job, owner.Server) {
                PersistentId = owner.PersistentId, CharacterId = owner.CharacterId, Metadata = owner.Metadata
            } }
        };
    }
    public Fight[] SaveCompleted(FightStore store) => SaveCompleted(store.Save);
    internal Fight[] SaveCompleted(Action<Fight> save)
    {
        // Serialize writers, but never keep ingestion waiting on disk I/O. A failed
        // write leaves the snapshot at the head for the next attempt.
        lock (saveGate)
        {
            lock (Gate) { if (detachedRunProgress != null) { completed.Enqueue(detachedRunProgress); detachedRunProgress = null; } }
            var saved = new List<Fight>();
            while (true)
            {
                Fight fight;
                lock (Gate) {
                    if (!completed.TryPeek(out fight!)) break;
                    if (fight.Id == Fight.Id) fight = Snapshot();
                    else if (fight.Id == resumable?.Id) fight = resumable.Copy();
                }
                save(fight);
                lock (Gate) completed.Dequeue();
                saved.Add(fight);
            }
            return saved.ToArray();
        }
    }
}
