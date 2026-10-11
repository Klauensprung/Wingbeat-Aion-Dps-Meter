using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

public sealed record Actor(int Id, string Name, bool Player, bool Party, int Cp, string Job, int Server)
{
    // Optional metadata keeps old saved fights compatible. Id is the live entity ID.
    public int NpcId { get; init; }
    public string ProfileSource { get; init; } = "Captured";
}
public sealed record Spec(int Skill, int Level, int[] Choices);
public sealed class Fight
{
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
    public List<NetworkEvent> Network { get; set; } = new();
    public Dictionary<int, Spec> Specs { get; set; } = new();
    public Dictionary<int, ActorBuild> Builds { get; set; } = new();
    public bool PartyKnown { get; set; }
    public string PartySource { get; set; } = "Not captured";
    public LootEnergyEvent? Energy { get; set; }
    public long Start => Damage.Select(e => e.Timestamp).Concat(Healing.Select(e => e.Timestamp)).Concat(Defense.Select(e => e.Timestamp)).DefaultIfEmpty(0).Min();
    public long End => Damage.Select(e => e.Timestamp).Concat(Healing.Select(e => e.Timestamp)).Concat(Defense.Select(e => e.Timestamp)).DefaultIfEmpty(0).Max();
    public double Seconds => Math.Max(1, (End - Start) / 1000d);
    public string Name(int id) => Actors.TryGetValue(id, out var a) ? a.Name : $"Entity {id}";
    public bool HasEvents => Damage.Count + Healing.Count + Defense.Count > 0;
    public bool HasRecordedData => HasEvents || Network.Count > 0 || Energy != null || Builds.Count > 0 || PartyKnown || Actors.Values.Any(a => a.ProfileSource == "Entered locally");
    public Fight Copy() => new() { Version = Version, Id = Id, Label = Label, Created = Created, Demo = Demo, LocalPlayer = LocalPlayer,
        Actors = new(Actors), Damage = new(Damage), Healing = new(Healing), Defense = new(Defense), Network = new(Network),
        Specs = Specs.ToDictionary(p => p.Key, p => p.Value with { Choices = p.Value.Choices.ToArray() }), Energy = Energy,
        Builds = Builds.ToDictionary(p => p.Key, p => FightMetadata.Copy(p.Value)), PartyKnown = PartyKnown, PartySource = PartySource };
}

public sealed record TotalRow(int Id, string Character, string Role, int CP, long Damage, double DPS, long Healing, double HPS, double CritPercent);
public sealed record SkillRow(string Character, string Skill, int SkillCode, int Hits, long Damage, double DPS, double CritPercent);
public sealed record OpponentRow(int Id, string Opponent, string Identity, long DamageToUs, long DamageFromUs, double DPS, long Healing);
public sealed record DefenseRow(string Character, int OutgoingHits, int Avoided, double? LandedPercent, int Blocked, int Parried, int Evaded, int Resisted, long DamageTaken, double DamageTakenPerSecond);
public sealed record TimelineMark(int ActorId, int SkillCode, int Damage, double Seconds, double Fraction, bool Healing = false);
public static class Analysis
{
    public static TotalRow[] Totals(Fight f)
    {
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
        if (!Guid.TryParseExact(fight.Id, "N", out _)) throw new InvalidDataException("Invalid fight ID.");
        var path = Path.Combine(Root, fight.Id + ".json");
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(fight, Options));
        File.Move(temp, path, true);
    }
    public Fight Load(string path)
    {
        if (new FileInfo(path).Length > 128 * 1024 * 1024) throw new InvalidDataException("Fight exceeds 128 MB limit.");
        var f = JsonSerializer.Deserialize<Fight>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("Empty fight.");
        if (f.Version != 1 || !Guid.TryParseExact(f.Id, "N", out _) || string.IsNullOrWhiteSpace(f.Label) || f.Actors == null || f.Damage == null || f.Healing == null || f.Defense == null || f.Specs == null || f.Actors.Any(a => a.Value == null || a.Value.Name == null || a.Value.Job == null) || f.Specs.Any(s => s.Value == null || s.Value.Choices == null) || f.Damage.Any(e => e == null || e.Damage < 0 || e.Timestamp < 0) || f.Healing.Any(e => e == null || e.Damage < 0 || e.Timestamp < 0) || f.Defense.Any(e => e == null || e.Timestamp < 0)) throw new InvalidDataException("Unsupported or invalid fight data.");
        if (f.Network == null || f.Network.Count > 250000 || f.Network.Any(e => e == null || e.Timestamp < 0 || string.IsNullOrWhiteSpace(e.Kind) || string.IsNullOrWhiteSpace(e.Opcode) || e.Fields.ValueKind != JsonValueKind.Object)) throw new InvalidDataException("Invalid network event data.");
        if (f.Actors.Values.Any(a => a.NpcId < 0 || (a.NpcId > 0 && a.Player))) throw new InvalidDataException("Invalid NPC identity metadata.");
        if (f.Builds == null || f.PartySource == null || f.Builds.Count > 512 || f.Actors.Values.Any(a => a.Cp < 0 || a.Server < 0 || a.ProfileSource == null)) throw new InvalidDataException("Invalid character metadata.");
        foreach (var (id, build) in f.Builds) { if (build == null || id != build.ActorId || f.Actors.GetValueOrDefault(id)?.Player != true) throw new InvalidDataException("Invalid loadout owner."); FightMetadata.Validate(build); }
        if (f.PartyKnown)
        {
            var roster = f.Actors.Values.Where(a => a.Party || a.Id == f.LocalPlayer).ToArray();
            FightMetadata.Validate(new PartyRoster(roster.Select(a => a.Id).ToArray(), f.PartySource));
            if (roster.Any(a => !a.Player)) throw new InvalidDataException("Party roster contains a non-player.");
        }
        return f;
    }
    public IEnumerable<string> Files() => Directory.EnumerateFiles(Root, "*.json").OrderByDescending(File.GetLastWriteTimeUtc);
}
