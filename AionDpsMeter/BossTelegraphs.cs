using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal sealed record TelegraphNotice(int EntityId, int Skill, byte Action, ushort DurationMs);
public sealed record BossTelegraph(long Timestamp, int EntityId, int NpcId, int Skill, byte Action, ushort DurationMs, int Areas);

internal static class BossTelegraphs
{
    internal const int Limit = 2048;
    internal static TelegraphNotice Read(ref WireReader r)
    {
        int entity = r.Var(), skill = unchecked((int)r.U32()); byte action = r.Byte();
        // Sequence, process, position and direction are not needed for the summary.
        r.Take(4 + 2 + 12 + 4); ushort duration = r.U16();
        if (r.Remaining != 0) throw new InvalidDataException("Unexpected telegraph trailer.");
        return new(entity, skill, action, duration);
    }
    internal static void Apply(Fight fight, NetworkEvent message)
    {
        if (message.Kind != "telegraph" || message.Timestamp < 0) return;
        var notice = message.Fields.Deserialize<TelegraphNotice>();
        if (notice == null || fight.Actors.GetValueOrDefault(notice.EntityId) is not { Player: false, NpcId: > 0 } actor ||
            !CapturedBoss.IsBoss(actor)) return;
        Add(fight, new(message.Timestamp, actor.Id, actor.NpcId, notice.Skill, notice.Action, notice.DurationMs, 1));
    }
    static void Add(Fight fight, BossTelegraph value)
    {
        var list = fight.Telegraphs;
        if (list.Count > 0 && list[^1] is var last && last.EntityId == value.EntityId && last.NpcId == value.NpcId &&
            last.Action == value.Action && value.Timestamp >= last.Timestamp && value.Timestamp - last.Timestamp < 1000 &&
            last.Areas <= int.MaxValue - value.Areas) {
            list[^1] = last with { Areas = last.Areas + value.Areas }; return;
        }
        if (list.Count >= Limit) { fight.OmittedTelegraphs = checked(fight.OmittedTelegraphs + value.Areas); return; }
        list.Add(value);
    }
    internal static void Merge(Fight destination, Fight source)
    {
        var ordered = destination.Telegraphs.Concat(source.Telegraphs).OrderBy(t => t.Timestamp).ToArray();
        destination.Telegraphs.Clear();
        destination.OmittedTelegraphs = checked(destination.OmittedTelegraphs + source.OmittedTelegraphs);
        foreach (var value in ordered) Add(destination, value);
    }
    internal static void Validate(Fight fight)
    {
        if (fight.OmittedTelegraphs < 0 || fight.Telegraphs == null || fight.Telegraphs.Count > Limit ||
            fight.Telegraphs.Any(t => t == null || t.Timestamp < 0 || t.EntityId <= 0 || t.NpcId <= 0 || t.Areas <= 0))
            throw new InvalidDataException("Invalid boss telegraph summary.");
    }
}
