namespace AionDpsMeter.Local;

internal sealed record ObservedRunMember(string Character, int Server, string Job, long Damage, long Healing, long DamageTaken,
    int Deaths, double CapturedDps, double CapturedHps);
internal sealed record ObservedRunGroup(string Map, string Character, DateTime Recorded, int Fights, double CapturedSeconds,
    string Status, Fight[] Encounters, ObservedRunMember[] Members, string ClearTime = "Not captured", int CapturedBossKills = 0, string ReportedDeaths = "Not captured")
{
    public DungeonSplit[] Splits { get; init; } = [];
    public int OmittedSplits { get; init; }
    public int[] Skills { get; init; } = [];
    public PartyMemberSummary[] Party { get; init; } = [];
    public long OmittedPartyObservations { get; init; }
    public long OmittedSkillEvents { get; init; }
    public DungeonItemTotal[] UsedItems { get; init; } = [];
    public decimal OmittedUses { get; init; }
    public DungeonItemTotal[] ReceivedItems { get; init; } = [];
    public decimal OmittedReceived { get; init; }
}
internal static class ObservedRunGroups
{
    internal static ObservedRunGroup[] Build(IEnumerable<Fight> source)
    {
        var fights = source.Where(f => !f.Demo && f.Training == null && (f.HasEvents || f.DungeonRun?.HasProgress == true) && !f.MixedRunContexts && (f.ObservedRun != null || f.DungeonRun != null) &&
            f.Actors.GetValueOrDefault(f.LocalPlayer) is { Player: true, Server: > 0 }).DistinctBy(f => f.Id).ToArray();
        static long CapturedId(Actor actor) => actor.PersistentId > 0 ? actor.PersistentId : actor.CharacterId;
        var knownIds = fights.SelectMany(f => f.Actors.Values).Where(a => a.Player && a.Server > 0 && CapturedId(a) > 0)
            .GroupBy(a => (a.Server, a.Name)).ToDictionary(g => g.Key, g => g.Select(CapturedId).Distinct().ToArray());
        string Identity(Actor actor, string fight) {
            if (actor.Server <= 0) return $"{fight}:entity:{actor.Id}";
            long characterId = CapturedId(actor);
            if (characterId == 0 && knownIds.TryGetValue((actor.Server, actor.Name), out var ids)) {
                if (ids.Length != 1) return $"{fight}:entity:{actor.Id}";
                characterId = ids[0];
            }
            return characterId > 0 ? $"{actor.Server}:id:{characterId}" : $"{actor.Server}:name:{actor.Name}";
        }
        return fights.GroupBy(f => {
            var run = f.ObservedRun;
            return (MapId: f.DungeonRun?.MapId ?? run!.MapId,
                Key: f.DungeonRun?.Id ?? $"zone:{run!.ServerTimestampMs}:{run.Variant}:{run.Reference}", Local: Identity(f.Actors[f.LocalPlayer], f.Id));
        }).Select(group => {
            var encounters = group.OrderBy(f => f.Start).ToArray();
            var combat = encounters.Where(f => f.HasEvents).ToArray();
            double seconds = combat.Length == 0 ? 0 : Math.Max(1, (combat.Max(f => f.End) - combat.Min(f => f.Start)) / 1000d);
            var dungeon = encounters.Select(f => f.DungeonRun).Where(r => r != null).OrderBy(r => r!.LastObserved).LastOrDefault();
            var contributions = encounters.SelectMany(f => Analysis.Totals(f).Where(row =>
                f.Actors.GetValueOrDefault(row.Id) is { Player: true } actor && (actor.Party || row.Id == f.LocalPlayer))
                .Select(row => (Fight: f, Row: row, Actor: f.Actors[row.Id])));
            var members = contributions.GroupBy(c => Identity(c.Actor, c.Fight.Id)).Select(member => {
                var actor = member.Last().Actor;
                long damage = member.Sum(c => c.Row.Damage), healing = member.Sum(c => c.Row.Healing);
                long taken = member.Sum(c => c.Fight.ArchiveSummary?.Stats.GetValueOrDefault(c.Actor.Id)?.DamageTaken ??
                    c.Fight.Damage.Where(h => h.TargetId == c.Actor.Id).Sum(h => (long)h.Damage));
                int deaths = member.Sum(c => c.Fight.ArchiveSummary?.DeathCounts.GetValueOrDefault(c.Actor.Id) ??
                    c.Fight.Deaths.Count(d => d.EntityId == c.Actor.Id));
                return new ObservedRunMember(actor.Name, actor.Server, actor.Job, damage, healing, taken, deaths,
                    Math.Round(damage / Math.Max(1, seconds), 1), Math.Round(healing / Math.Max(1, seconds), 1));
            }).OrderByDescending(m => m.Damage).ToArray();
            return new ObservedRunGroup(ZoneNames.Default.Find(group.Key.MapId), encounters[^1].Name(encounters[^1].LocalPlayer),
                encounters[0].Created.ToLocalTime(), combat.Length, Math.Round(seconds, 1),
                dungeon?.ClearedAt != null ? dungeon.SawPrepare ? "Completion captured" : "Completion captured; partial entry" :
                encounters.Any(f => f.ObservedRun?.State == 3) ? "Phase 3 observed; clear unconfirmed" : "Partial capture; clear unconfirmed", encounters, members,
                dungeon?.ClearSeconds is double clear ? $"{clear:N1} s" : "Not captured", dungeon?.BossKills?.Length ?? 0,
                dungeon?.ReportedDeaths is int reported ? reported.ToString() : "Not captured") { Party = dungeon?.Party ?? [], OmittedPartyObservations = dungeon?.OmittedPartyObservations ?? 0, Skills = dungeon?.Skills ?? [], OmittedSkillEvents = dungeon?.OmittedSkillEvents ?? 0, Splits = dungeon?.Splits ?? [], OmittedSplits = dungeon?.OmittedSplits ?? 0, UsedItems = dungeon?.UsedItems ?? [], OmittedUses = dungeon?.OmittedUses ?? 0, ReceivedItems = dungeon?.ReceivedItems ?? [], OmittedReceived = dungeon?.OmittedReceived ?? 0 };
        }).OrderByDescending(r => r.Recorded).ToArray();
    }
}
