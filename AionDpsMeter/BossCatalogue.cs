using System.Collections.Frozen;
using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

public sealed record BossVariant(int NpcId, string Context, string Level, string Type, string Location);
public sealed record BossDefinition(int Key, string Name, bool FieldBoss, string[] InstanceKeys, BossVariant[] Variants, string Source, string SourceSha256);
public sealed record DungeonDefinition(string Key, string Name, string Kind, int[] BossKeys, string Source, string SourceSha256);
public sealed record BossCatalogueData(int Version, string ClientVersion, string Region, string RetrievedUtc, string Source, string Verification, DungeonDefinition[] Dungeons, BossDefinition[] Bosses);
public sealed record BossCatalogueRow(string Boss, string Category, string PossibleInstances, string NpcIds, string Source);

public sealed class BossCatalogue
{
    public static BossCatalogue Default { get; } = new();
    readonly FrozenDictionary<int, BossDefinition> byNpc;
    readonly FrozenDictionary<string, DungeonDefinition> dungeons;
    public BossDefinition[] Bosses { get; }
    public string ClientVersion { get; } = "Unavailable";
    public string? LoadError { get; }
    public int NpcCount => byNpc.Count;
    public int DungeonCount => dungeons.Count;
    public int AmbiguousNpcCount { get; }
    public BossCatalogue()
    {
        try {
            using var stream = typeof(BossCatalogue).Assembly.GetManifestResourceStream("Wingbeat.Bosses.json") ?? throw new InvalidDataException("Bundled boss catalogue is missing.");
            var data = JsonSerializer.Deserialize<BossCatalogueData>(stream) ?? throw new InvalidDataException("Empty boss catalogue.");
            Validate(data);
            Bosses = data.Bosses; ClientVersion = data.ClientVersion;
            dungeons = data.Dungeons.ToFrozenDictionary(d => d.Key, StringComparer.Ordinal);
            var groups = Bosses.SelectMany(b => b.Variants.Select(v => (v.NpcId, Boss: b))).GroupBy(v => v.NpcId).ToArray();
            AmbiguousNpcCount = groups.Count(g => g.Select(x => x.Boss.Key).Distinct().Count() > 1);
            byNpc = groups.Where(g => g.Select(x => x.Boss.Key).Distinct().Count() == 1).ToFrozenDictionary(g => g.Key, g => g.First().Boss);
        }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException) {
            Bosses = []; byNpc = new Dictionary<int, BossDefinition>().ToFrozenDictionary();
            dungeons = new Dictionary<string, DungeonDefinition>().ToFrozenDictionary();
            LoadError = "Boss catalogue unavailable: " + ex.Message;
        }
    }
    internal static void Validate(BossCatalogueData data)
    {
        if (data.Version != 1 || string.IsNullOrWhiteSpace(data.ClientVersion) || data.Bosses == null || data.Dungeons == null ||
            data.Bosses.Length is 0 or > 20000 || data.Dungeons.Length is 0 or > 5000) throw new InvalidDataException("Invalid boss catalogue header.");
        if (data.Bosses.Any(b => b == null || b.Key <= 0 || string.IsNullOrWhiteSpace(b.Name) || b.Name.Length > 240 || b.InstanceKeys == null || b.Variants == null || b.Variants.Length == 0 ||
            b.Variants.Any(v => v == null || v.NpcId <= 0 || v.Context == null || v.Type == null || v.Location == null)) ||
            data.Bosses.Select(b => b.Key).Distinct().Count() != data.Bosses.Length ||
            data.Dungeons.Any(d => d == null || string.IsNullOrWhiteSpace(d.Key) || string.IsNullOrWhiteSpace(d.Name) || d.BossKeys == null) ||
            data.Dungeons.Select(d => d.Key).Distinct().Count() != data.Dungeons.Length) throw new InvalidDataException("Invalid boss catalogue entries.");
        var keys = data.Dungeons.Select(d => d.Key).ToHashSet(); var bosses = data.Bosses.Select(b => b.Key).ToHashSet();
        if (data.Bosses.Any(b => b.InstanceKeys.Any(k => !keys.Contains(k))) || data.Dungeons.Any(d => d.BossKeys.Any(k => !bosses.Contains(k))))
            throw new InvalidDataException("Unresolved boss catalogue associations.");
    }
    public BossDefinition? Find(int npcId) => byNpc.GetValueOrDefault(npcId);
    // Hero/legend rank alone is not a reliable encounter classification.
    public bool IsEncounterBoss(int npcId) => Find(npcId) is { } boss && (boss.FieldBoss || boss.InstanceKeys.Length > 0);
    public string PossibleInstances(BossDefinition boss) => boss.InstanceKeys.Length == 0 ? boss.FieldBoss ? "Open world" : "Unassigned" :
        string.Join(" / ", boss.InstanceKeys.Select(k => dungeons[k].Name).Distinct().Order());
    BossCatalogueRow[]? rows;
    public BossCatalogueRow[] Rows() => rows ??= Bosses.Select(b => new BossCatalogueRow(b.Name, b.FieldBoss ? "Field boss" : b.InstanceKeys.Length > 0 ? "Instance boss" : "Hero / legend (unassigned)",
        PossibleInstances(b), string.Join(", ", b.Variants.Select(v => v.NpcId).Distinct().Order()), b.Source)).OrderBy(b => b.Boss).ToArray();
}

public sealed record BossAnalysisRow(string Boss, string PossibleInstances, string NpcIds, int Entities, int Hits, long DamageToBoss, long DamageFromBoss, int ObservedDeaths, double FirstSeen, double LastSeen);
public static class BossAnalysis
{
    public static BossAnalysisRow[] Build(Fight fight)
    {
        var catalog = BossCatalogue.Default;
        var engaged = fight.Damage.SelectMany(h => new[] { h.ActorId, h.TargetId }).ToHashSet();
        var targets = fight.Actors.Values.Where(a => !a.Player && a.NpcId > 0 && engaged.Contains(a.Id) && catalog.IsEncounterBoss(a.NpcId))
            .GroupBy(a => catalog.Find(a.NpcId)!.Key);
        var outgoing = fight.Damage.ToLookup(h => h.TargetId); var incoming = fight.Damage.ToLookup(h => h.ActorId);
        var deaths = fight.Deaths.ToLookup(d => d.EntityId);
        return targets.Select(group => {
            var boss = catalog.Find(group.First().NpcId)!;
            var dealt = group.SelectMany(a => outgoing[a.Id]).ToArray(); var received = group.SelectMany(a => incoming[a.Id]).ToArray();
            var times = dealt.Concat(received).Select(h => h.Timestamp).ToArray();
            return new BossAnalysisRow(boss.Name, catalog.PossibleInstances(boss), string.Join(", ", group.Select(a => a.NpcId).Distinct().Order()), group.Count(),
                dealt.Length, dealt.Sum(h => (long)h.Damage), received.Sum(h => (long)h.Damage), group.Sum(a => deaths[a.Id].Count()),
                Math.Round((times.Min() - fight.Start) / 1000d, 1), Math.Round((times.Max() - fight.Start) / 1000d, 1));
        }).OrderBy(b => b.FirstSeen).ToArray();
    }
    sealed record SavedSummary(string Text);
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Fight, SavedSummary> summaries = new();
    // Used only for immutable archive snapshots, never the live mutable engine fight.
    public static string Summary(Fight fight) => fight.ArchiveSummary?.Bosses ?? summaries.GetValue(fight, f => new(string.Join(", ", Build(f).Select(b => b.Boss)))).Text;
}
