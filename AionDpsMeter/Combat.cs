using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

public sealed record Actor(int Id, string Name, bool Player, bool Party, int Cp, string Job, int Server)
{
    // Archived identities use a separate key when the game reuses their entity ID.
    public int OriginalEntityId { get; init; }
    public string CachedJob { get; init; } = "Unknown";
    public int NpcId { get; init; }
    public bool? CapturedBoss { get; init; }
    internal long BossScene { get; init; }
    public int? OwnerId { get; init; }
    public VisibleEquipmentSnapshot? VisibleEquipment { get; init; }
    public CapturedSkillSnapshot? CapturedSkills { get; init; }
    public DaevanionSnapshot? Daevanion { get; init; }
    public WingSnapshot? Wings { get; init; }
    public TitleSnapshot? Titles { get; init; }
    public CharacterCollectionSnapshot? Collections { get; init; }
    public EquippedGearSnapshot? Gear { get; init; }
    public PlayerMetadataSnapshot? Metadata { get; init; }
    public uint CharacterId { get; init; }
    // Full wire character identity, separate from live entity IDs and legacy links.
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public long PersistentId { get; init; }
    public string ProfileSource { get; init; } = "Captured";
    public string CpSource { get; init; } = "Unknown";
}
public sealed record Spec(int Skill, int Level, int[] Choices);
public sealed class Fight
{
    internal FightArchiveSummary? ArchiveSummary { get; init; }
    internal BossRecord[] BossRecords { get; init; } = [];
    internal (long Start, long End)? DisplayWindow { get; init; }
    public TrainingInfo? Training { get; set; }
    public PetCollectionSnapshot? Pets { get; set; }
    public LocalTargetState? LocalTarget { get; set; }
    public List<BossTelegraph> Telegraphs { get; set; } = new();
    public long OmittedTelegraphs { get; set; }
    public long OmittedGathers { get; set; }
    public Dictionary<int, long> GatheredObjects { get; set; } = new();
    public string? PartyLeaderName { get; set; }
    public LocalTargetState? PartyBossTarget { get; set; }
    public CapturedPosition? LocalPosition { get; set; }
    public ObservedRun? ObservedRun { get; set; }
    public DungeonRun? DungeonRun { get; set; }
    public bool MixedRunContexts { get; set; }
    public EffectSummary[]? SavedEffects { get; set; }
    public ShieldSummary[]? SavedShields { get; set; }
    public ResourceSnapshot? LocalResources { get; set; }
    public SavedLootSummary? SavedLoot { get; set; }
    internal LootLedger? LiveLoot { get; set; }
    public SavedCastOutcomes? SavedCasts { get; set; }
    public StaggerBreak[]? StaggerBreaks { get; set; }
    public bool StaggerBreaksTruncated { get; set; }
    internal CastOutcomeAccumulator? LiveCasts { get; set; }
    internal CooldownTracker? LiveCooldowns { get; set; }
    internal Fight ForStorage() {
        // Share immutable snapshot event lists while streaming; do not allocate
        // another complete fight merely to replace its diagnostic network list.
        var copy = (Fight)MemberwiseClone();
        copy.Network = FightStorageEvents.Select(this); copy.SavedEffects = EffectAnalysis.Summarize(this); copy.SavedShields = ShieldAnalysis.Summarize(this);
        copy.SavedLoot = SavedLoot ?? LootLedger.FromFight(this).Summary();
        copy.SavedCasts = CastOutcomes.Build(this);
        return copy;
    }
    public int Version { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = "Local fight";
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public bool Demo { get; set; }
    public int LocalPlayer { get; set; }
    public Dictionary<int, Actor> Actors { get; set; } = new();
    public List<DamageEvent> Damage { get; set; } = new();
    public List<DamageEvent> Healing { get; set; } = new();
    public List<DefenseEvent> Defense { get; set; } = new();
    public List<DeathEvent> Deaths { get; set; } = new();
    public List<NetworkEvent> Network { get; set; } = new();
    public Dictionary<int, Spec> Specs { get; set; } = new();
    public Dictionary<int, ActorBuild> Builds { get; set; } = new();
    public bool PartyKnown { get; set; }
    public string PartySource { get; set; } = "Not captured";
    public LootEnergyEvent? Energy { get; set; }
    public PartyProfile[] PartyProfiles { get; set; } = [];
    public PingReading? LocalPing { get; set; }
    public PlayerStatReading? LocalStats { get; set; }
    public long Start => DisplayWindow?.Start ?? Training?.Start ?? ArchiveSummary?.Start ?? Damage.Select(e => e.Timestamp).Concat(Healing.Select(e => e.Timestamp)).Concat(Defense.Select(e => e.Timestamp)).DefaultIfEmpty(0).Min();
    public long End => DisplayWindow?.End ?? Training?.End ?? ArchiveSummary?.End ?? Damage.Select(e => e.Timestamp).Concat(Healing.Select(e => e.Timestamp)).Concat(Defense.Select(e => e.Timestamp)).DefaultIfEmpty(0).Max();
    public double Seconds => Math.Max(1, (End - Start) / 1000d);
    public string Name(int id)
    {
        if (!Actors.TryGetValue(id, out var actor) || string.IsNullOrWhiteSpace(actor.Name)) return $"Entity {id}";
        if (actor.NpcId > 0 && (actor.Name == $"NPC {actor.NpcId}" || actor.Name == $"Entity {id}"))
            return NpcNames.Default.GetName(actor.NpcId);
        return actor.Name;
    }
    public bool HasEvents => ArchiveSummary?.HasEvents ?? Damage.Count + Healing.Count + Defense.Count > 0;
    public bool HasRecordedData => LiveLoot?.HasData == true || LiveCasts?.HasData == true || SavedCasts?.Rows.Length > 0 || SavedCasts?.OmittedStarts > 0 || Telegraphs.Count > 0 || OmittedTelegraphs > 0 || OmittedGathers > 0 || GatheredObjects.Count > 0 || DungeonRun?.HasProgress == true || SavedLoot?.Rows.Length > 0 || SavedLoot?.OmittedItems > 0 || SavedLoot?.OmittedSessions > 0 || Training != null || HasEvents || Deaths.Count > 0 || LocalStats != null || Network.Count > 0 || Energy != null || Builds.Count > 0 || PartyKnown || Actors.Values.Any(a => a.ProfileSource == "Entered locally");
    public Fight Copy() => new() { LiveCooldowns = LiveCooldowns?.Copy(), StaggerBreaks = StaggerBreaks?.ToArray(), StaggerBreaksTruncated = StaggerBreaksTruncated, LiveLoot = LiveLoot?.Copy(), LiveCasts = LiveCasts?.Copy(), Telegraphs = new(Telegraphs), OmittedTelegraphs = OmittedTelegraphs, GatheredObjects = new(GatheredObjects), OmittedGathers = OmittedGathers, LocalResources = LocalResources, SavedShields = SavedShields, SavedLoot = SavedLoot, SavedCasts = SavedCasts == null ? null : CastOutcomes.Copy(SavedCasts), SavedEffects = SavedEffects, DungeonRun = DungeonRun, ObservedRun = ObservedRun, MixedRunContexts = MixedRunContexts, LocalPosition = LocalPosition, LocalTarget = LocalTarget, PartyBossTarget = PartyBossTarget, Pets = Pets, BossRecords = BossRecords, Training = Training, DisplayWindow = DisplayWindow, ArchiveSummary = ArchiveSummary, Version = Version, Id = Id, Label = Label, Created = Created, Demo = Demo, LocalPlayer = LocalPlayer,
        Actors = new(Actors), Damage = new(Damage), Healing = new(Healing), Defense = new(Defense), Network = new(Network), Deaths = new(Deaths), PartyProfiles = [..PartyProfiles], LocalPing = LocalPing, LocalStats = CharacterReadings.Copy(LocalStats),
        Specs = Specs.ToDictionary(p => p.Key, p => p.Value with { Choices = p.Value.Choices.ToArray() }), Energy = Energy,
        Builds = Builds.ToDictionary(p => p.Key, p => FightMetadata.Copy(p.Value)), PartyKnown = PartyKnown, PartySource = PartySource, PartyLeaderName = PartyLeaderName };
}

public sealed record TotalRow(int Id, string Character, string Role, int CP, long Damage, double DPS, long Healing, double HPS, double CritPercent);
public sealed record SkillRow(string Character, string Skill, int SkillCode, int Hits, long Damage, double DPS, double CritPercent)
{
    public double DamageShare { get; init; }
}
public sealed record OpponentRow(int Id, string Opponent, string Identity, long DamageToUs, long DamageFromUs, double DPS, long Healing)
{
    public int ConfirmedKills { get; init; }
    public int ObservedDeaths { get; init; }
    public long Peak5sToUs { get; init; }
    public long Peak5sFromUs { get; init; }
}
public sealed record DefenseRow(string Character, int OutgoingHits, int Avoided, double? LandedPercent, int Blocked, int Parried, int Evaded, int Resisted, long DamageTaken, double DamageTakenPerSecond);
public sealed record TimelineMark(int ActorId, int SkillCode, long Damage, double Seconds, double Fraction, bool Healing = false);
public static class Analysis
{
    public static TotalRow[] Totals(Fight f)
    {
        if (f.ArchiveSummary is { } summary) return summary.Totals;
        double seconds = f.Seconds;
        var damageByActor = f.Damage.ToLookup(e => e.ActorId);
        var healingByActor = f.Healing.ToLookup(e => e.ActorId);
        return damageByActor.Select(g => g.Key).Concat(healingByActor.Select(g => g.Key))
            .Concat(f.Actors.Values.Where(a => a.Player && (a.Party || a.Id == f.LocalPlayer)).Select(a => a.Id)).Distinct().Select(id => {
        var damage = damageByActor[id].ToArray();
        long total = damage.Sum(e => (long)e.Damage), heal = healingByActor[id].Sum(e => (long)e.Damage);
        f.Actors.TryGetValue(id, out var actor);
        return new TotalRow(id, f.Name(id), actor?.Party == true ? "Party" : actor?.Player == true ? "Player" : actor?.NpcId > 0 ? "NPC" : "Unclassified / NPC", actor?.Cp ?? 0, total, Math.Round(total / seconds, 1), heal, Math.Round(heal / seconds, 1), damage.Length == 0 ? 0 : Math.Round(100d * damage.Count(e => e.IsCritical) / damage.Length, 1));
    }).OrderByDescending(r => r.Damage).ToArray();
    }
    public static SkillRow[] Skills(Fight f, Func<int, string> name)
    {
        double seconds = f.Seconds;
        return f.Damage.GroupBy(e => (e.ActorId, e.SkillCode)).Select(g => new SkillRow(f.Name(g.Key.ActorId), name(g.Key.SkillCode), g.Key.SkillCode, g.Count(), g.Sum(e => (long)e.Damage), Math.Round(g.Sum(e => (long)e.Damage) / seconds, 1), Math.Round(100d * g.Count(e => e.IsCritical) / g.Count(), 1))).OrderByDescending(s => s.Damage).ToArray();
    }
    public static OpponentRow[] Opponents(Fight f) => PvpAnalysis.Build(f).Opponents;
    public static DefenseRow[] Defense(Fight f)
    {
        var incoming = f.Defense.ToLookup(e => e.TargetId);
        var avoided = f.Defense.Where(e => e.Outcome is DefenseOutcome.Evade or DefenseOutcome.Resist or DefenseOutcome.TargetAvoided).ToLookup(e => e.ActorId);
        var outgoing = f.Damage.Where(e => e.Damage > 0).ToLookup(e => e.ActorId);
        var taken = f.Damage.ToLookup(e => e.TargetId);
        double seconds = f.Seconds;
        return f.Actors.Values.Where(a => a.Id > 0).Select(a => {
            int hits = outgoing[a.Id].Count(), misses = avoided[a.Id].Count();
            var outcomes = incoming[a.Id];
            return new DefenseRow(a.Name, hits, misses, hits + misses == 0 ? null : Math.Round(100d * hits / (hits + misses), 1),
                outcomes.Count(e => e.Outcome == DefenseOutcome.Block), outcomes.Count(e => e.Outcome == DefenseOutcome.Parry),
                outcomes.Count(e => e.Outcome == DefenseOutcome.Evade), outcomes.Count(e => e.Outcome == DefenseOutcome.Resist), taken[a.Id].Sum(e => (long)e.Damage), Math.Round(taken[a.Id].Sum(e => (long)e.Damage) / seconds, 1));
        }).ToArray();
    }
    public static TimelineMark[] Timeline(Fight f, IEnumerable<int> actorIds, bool includeHealing = false)
    {
        long start = f.Start;
        double seconds = Math.Max(1, (f.End - start) / 1000d);
        var byActor = f.Damage.Select(e => (Hit: e, Healing: false)).Concat(includeHealing ? f.Healing.Select(e => (Hit: e, Healing: true)) : [])
            .OrderBy(e => e.Hit.Timestamp).ToLookup(e => e.Hit.ActorId);
        return actorIds.Distinct().SelectMany(id => byActor[id].TakeLast(5000).Select(e => {
            double offset = (e.Hit.Timestamp - start) / 1000d;
            return new TimelineMark(id, e.Hit.SkillCode, e.Hit.Damage, offset, offset / seconds, e.Healing);
        })).ToArray();
    }
}

public sealed class FightStore
{
    public string Root { get; }
    static readonly JsonSerializerOptions Options = new() { WriteIndented = false, MaxDepth = 64 };
    public FightStore(string root) { Root = root; Directory.CreateDirectory(root); }
    public void Save(Fight fight)
    {
        if (fight.ArchiveSummary != null) throw new InvalidOperationException("Load full fight details before saving an archive entry.");
        if (fight.DisplayWindow != null) throw new InvalidOperationException("Save the full encounter rather than a filtered display.");
        if (!Guid.TryParseExact(fight.Id, "N", out _)) throw new InvalidDataException("Invalid fight ID.");
        var path = Path.Combine(Root, fight.Id + ".json");
        var temp = path + ".tmp";
        // Stream directly to the temporary file: large encounters must not also
        // allocate a complete UTF-16 JSON string before they can be saved.
        using (var output = File.Create(temp)) JsonSerializer.Serialize(output, fight.ForStorage(), Options);
        File.Move(temp, path, true);
    }
    public Fight Load(string path)
    {
        if (new FileInfo(path).Length > 128 * 1024 * 1024) throw new InvalidDataException("Fight exceeds 128 MB limit.");
        using var input = File.OpenRead(path);
        var f = JsonSerializer.Deserialize<Fight>(input, Options) ?? throw new InvalidDataException("Empty fight.");
        if (f.Version != 1 || !Guid.TryParseExact(f.Id, "N", out _) || string.IsNullOrWhiteSpace(f.Label) || f.Actors == null || f.Damage == null || f.Healing == null || f.Defense == null || f.Specs == null || f.Actors.Any(a => a.Value == null || a.Value.Name == null || a.Value.Job == null) || f.Specs.Any(s => s.Value == null || s.Value.Choices == null) || f.Damage.Any(e => e == null || e.Damage < 0 || e.Timestamp < 0 || e.SourceActorId < 0) || f.Healing.Any(e => e == null || e.Damage < 0 || e.Timestamp < 0 || e.SourceActorId < 0) || f.Defense.Any(e => e == null || e.Timestamp < 0 || e.SourceActorId < 0 || e.SourceTargetId < 0)) throw new InvalidDataException("Unsupported or invalid fight data.");
        if (f.Deaths == null || f.Deaths.Any(d => d == null || d.EntityId <= 0 || d.Timestamp < 0)) throw new InvalidDataException("Invalid death events.");
        if (f.Training is { } training && (training.Start < 0 || training.End < training.Start ||
            training.LimitSeconds != 0 && (training.LimitSeconds < 30 || training.LimitSeconds > 300) ||
            training.Group == null || training.Group.Length > 120 || training.Notes == null || training.Notes.Length > 4000 ||
            f.LocalPlayer <= 0 || f.Damage.Any(h => h.ActorId != f.LocalPlayer || h.Timestamp < training.Start || h.Timestamp >= training.End)))
            throw new InvalidDataException("Invalid training attempt.");
        if (f.Network == null || f.Network.Any(e => e == null || e.Timestamp < 0 || string.IsNullOrWhiteSpace(e.Kind) || string.IsNullOrWhiteSpace(e.Opcode) || e.Fields.ValueKind != JsonValueKind.Object)) throw new InvalidDataException("Invalid network event data.");
        if (f.Actors.Values.Any(a => a.OriginalEntityId < 0 || a.NpcId < 0 || a.OwnerId < 0 || a.OwnerId == a.Id || (a.NpcId > 0 && a.Player))) throw new InvalidDataException("Invalid NPC identity metadata.");
        foreach (var actor in f.Actors.Values) if (actor.VisibleEquipment != null) VisibleEquipment.Validate(actor.VisibleEquipment);
        foreach (var actor in f.Actors.Values) if (actor.CapturedSkills != null) CapturedSkills.Validate(actor.CapturedSkills);
        foreach (var actor in f.Actors.Values) if (actor.Daevanion != null) Daevanion.Validate(actor.Daevanion);
        foreach (var actor in f.Actors.Values) if (actor.Wings != null) Wings.Validate(actor.Wings);
        foreach (var actor in f.Actors.Values) if (actor.Titles != null) Titles.Validate(actor.Titles);
        foreach (var actor in f.Actors.Values) if (actor.Collections != null) CharacterCollections.Validate(actor.Collections);
        foreach (var actor in f.Actors.Values) if (actor.Gear != null) EquippedGear.Validate(actor.Gear);
        foreach (var actor in f.Actors.Values) if (actor.Metadata != null) PlayerMetadata.Validate(actor.Metadata);
        BossTelegraphs.Validate(f);
        if (f.OmittedGathers < 0 || f.GatheredObjects == null || f.GatheredObjects.Count > 4096 || f.GatheredObjects.Any(p => p.Key <= 0 || p.Value <= 0)) throw new InvalidDataException("Invalid gathering summary.");
        if (f.PartyLeaderName?.Length > 4096 || f.Builds == null || f.PartySource == null || f.Builds.Count > 512 || f.Actors.Values.Any(a => a.Cp < 0 || a.Server < 0 || a.ProfileSource == null)) throw new InvalidDataException("Invalid character metadata.");
        PlayerTelemetry.Validate(f.PartyProfiles);
        if (f.Pets != null) PetCollection.Validate(f.Pets);
        if (f.LocalPosition != null) LocalPositionTracker.Validate(f.LocalPosition);
        if (f.ObservedRun != null) ObservedRunTracker.Validate(f.ObservedRun);
        if (f.DungeonRun != null) DungeonRunTracker.Validate(f.DungeonRun);
        if (f.SavedLoot != null) LootLedger.Validate(f.SavedLoot);
        if (f.SavedCasts != null) CastOutcomes.Validate(f.SavedCasts);
        if (f.StaggerBreaks != null) StaggerBreakTracker.Validate(f.StaggerBreaks);
        if (f.SavedShields != null) ShieldAnalysis.Validate(f.SavedShields);
        if (f.LocalResources != null) ResourceSnapshots.Validate(f.LocalResources);
        if (f.SavedEffects != null && f.SavedEffects.Any(s => s == null || s.TargetId <= 0 || s.CasterId < 0 || s.EffectId <= 0 ||
            s.SkillCode < 0 || s.TrackedMs < 0 || s.TrackedMs > Math.Max(1, f.End - f.Start) || s.Segments <= 0))
            throw new InvalidDataException("Invalid saved effect summaries.");
        if (f.PartyBossTarget is { } bossTarget && (bossTarget.EntityId < 0 || bossTarget.Timestamp < 0)) throw new InvalidDataException("Invalid saved party boss target.");
        if (f.LocalTarget is { } target && (target.EntityId < 0 || target.Timestamp < 0)) throw new InvalidDataException("Invalid saved local target.");
        if (f.LocalStats != null) CharacterReadings.Validate(f.LocalStats);
        if (f.LocalPing is { } ping && (ping.Milliseconds is < 0 or > 1000 || ping.Timestamp < 0 || string.IsNullOrWhiteSpace(ping.Source))) throw new InvalidDataException("Invalid local ping metadata.");
        foreach (var (id, build) in f.Builds) { if (build == null || id != build.ActorId || f.Actors.GetValueOrDefault(id)?.Player != true) throw new InvalidDataException("Invalid loadout owner."); FightMetadata.Validate(build); }
        if (f.PartyKnown)
        {
            var roster = f.Actors.Values.Where(a => a.Party || a.Id == f.LocalPlayer).ToArray();
            FightMetadata.Validate(new PartyRoster(roster.Select(a => a.Id).ToArray(), f.PartySource));
            if (roster.Any(a => !a.Player)) throw new InvalidDataException("Party roster contains a non-player.");
        }
        f.Healing.RemoveAll(HealingSkills.SummonInitialization);
        return f;
    }
    public IEnumerable<string> Files() => Directory.EnumerateFiles(Root, "*.json").OrderByDescending(File.GetLastWriteTimeUtc);
}
