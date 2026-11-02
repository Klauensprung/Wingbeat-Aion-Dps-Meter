using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal sealed record BattleScore([property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long Id, byte Kills, byte Deaths, byte Assists);
internal sealed record BattleScores(BattleScore[] Scores);
internal sealed record BattleScoreRow(string CharacterId, string Name, string Server, string Class, byte Kills, byte Deaths, byte Assists);
internal static class BattleScoreWire
{
    internal static BattleScores Read(ref WireReader r)
    {
        int count = r.Var(); if (count > 4096) throw new InvalidDataException("Battle scoreboard exceeds bounds.");
        var scores = new BattleScore[count];
        for (int i = 0; i < count; i++) {
            long id = unchecked((long)r.U64()); r.Bit(); r.Bit();
            scores[i] = new(id, r.Byte(), r.Byte(), r.Byte()); r.Byte();
        }
        if (r.Remaining != 0) throw new InvalidDataException("Unexpected battle scoreboard trailer.");
        return new(scores);
    }
}
internal sealed class BattleScoreTracker
{
    readonly Dictionary<long, (BattleScore Score, long At)> scores = new();
    long boundary = -1;
    internal void Clear(long at) { if (at < boundary) return; scores.Clear(); boundary = at; }
    internal void Apply(NetworkEvent e)
    {
        if (e.Kind != "scoreboard" || e.Timestamp < boundary) return;
        var reading = e.Fields.Deserialize<BattleScores>(); if (reading?.Scores == null || reading.Scores.Length > 4096) return;
        foreach (var score in reading.Scores) {
            if (scores.TryGetValue(score.Id, out var old) && old.At > e.Timestamp) continue;
            if (!scores.ContainsKey(score.Id) && scores.Count >= 2048) scores.Remove(scores.MinBy(p => p.Value.At).Key);
            scores[score.Id] = (score, e.Timestamp);
        }
    }
    internal BattleScore[] Snapshot() => scores.Values.Select(v => v.Score).OrderByDescending(s => s.Kills).ThenByDescending(s => s.Assists).ThenBy(s => s.Deaths).ToArray();
    internal BattleScoreRow[] Rows(IEnumerable<Actor> actors)
    {
        var identities = actors.Where(a => a.Player && a.OriginalEntityId == 0 && a.PersistentId != 0).GroupBy(a => a.PersistentId)
            .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
        return Snapshot().Select(s => {
            var actor = identities.GetValueOrDefault(s.Id);
            return new BattleScoreRow(s.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), actor?.Name ?? "Not identified", actor?.Server.ToString() ?? "Unknown", actor?.Job ?? "Unknown", s.Kills, s.Deaths, s.Assists);
        }).ToArray();
    }
}
