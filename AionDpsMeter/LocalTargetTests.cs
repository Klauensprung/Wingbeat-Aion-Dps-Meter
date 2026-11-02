using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class LocalTargetTests
{
    internal static void Run(Action<bool, string> check)
    {
        DecodeResult Decode(params byte[] body) => new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, [(byte)(body.Length + 6), 0x35, 0x38, ..body]) { TimestampMs = 1000 });
        var update = Decode(1, 0, 100).Events.Single().Network!;
        check(update.Kind == "target" && LocalTarget.Apply(null, update, 1)?.EntityId == 100 && LocalTarget.Apply(null, update, 2) == null,
            "Target updates associate the actor with its selected target and ignore other actors");
        check(LocalTarget.Apply(null, Decode(1, 1, 100, 7, 0, 0, 0).Events.Single().Network!, 1)?.EntityId == 100 &&
            LocalTarget.Apply(null, Decode(1, 0, 0).Events.Single().Network!, 1)?.EntityId == 0,
            "Target updates support optional part IDs and explicit target clearing");
        DecodeResult Reply(params byte[] body) => new EuGameDecoder().Decode(new CapturedPayload(13328, 50000,
            [(byte)(body.Length + 6), 0x34, 0x38, ..body]) { TimestampMs = 1000 });
        check(Reply(0, 0, 0, 100).Events.Single().Network!.Kind == "local-target" && Reply(0, 1, 0, 100).Events.Count == 0 &&
            Reply(1, 0, 0, 100).Events.Count == 0 && Reply(0, 0, 0, 0).Events.Single().Network!.Fields.GetProperty("EntityId").GetInt32() == 0,
            "Only successful complete target replies update local selection; zero explicitly clears it");
        var selected = new NetworkEvent("local-target", 1000, "synthetic", JsonSerializer.SerializeToElement(new { EntityId = 100 }));
        check(Decode(0x80).Events.Count == 0 && Decode(1, 0, 0, 0, 0).Events.Count == 0,
            "Truncated target varints and unrecognized long layouts emit no selection");
        var engine = new CombatEngine();
        engine.Apply(new("local-player", Actor: new(1, "Player 1", true, false, 0, "Cleric", 1)));
        engine.Apply(new("network", Network: selected));
        engine.NewFight("Reset");
        var snapshot = engine.Snapshot();
        engine.Apply(new("network", Network: new("target", 2000, "synthetic", JsonSerializer.SerializeToElement(new { ActorId = 2, TargetId = 101 }))));
        engine.Apply(new("network", Network: selected with { Timestamp = 999, Fields = JsonSerializer.SerializeToElement(new { EntityId = 102 }) }));
        check(engine.Fight.LocalTarget?.EntityId == 100 && snapshot.LocalTarget?.EntityId == 100,
            "Local target survives fight reset and ignores other players' targets and stale selection packets");
        engine.Apply(new("actor", Actor: new(100, "NPC 1000", false, false, 0, "Unknown", 0) { NpcId = 1000 }));
        engine.Apply(new("actor", Actor: new(100, "NPC 1001", false, false, 0, "Unknown", 0) { NpcId = 1001 }));
        check(engine.Fight.LocalTarget?.EntityId == 0 && snapshot.LocalTarget?.EntityId == 100,
            "Recycled target identities invalidate the live selection without modifying saved snapshots");
        engine.Apply(new("network", Network: selected with { Timestamp = 2000 }));
        engine.Apply(new("local-player", Actor: new(1, "Player 1", true, false, 0, "Cleric", 1)));
        check(engine.Fight.LocalTarget == null, "Character/zone reset clears stale in-game targeting");
        var fight = new Fight { Damage = [new DamageEvent { ActorId = 1, TargetId = 100, Damage = 500, Timestamp = 1000 }] };
        check(OverlayCombatFilter.Apply(fight, false, -1).Damage.Count == 0 && OverlayCombatFilter.Apply(fight, false, 100).Damage.Count == 1,
            "An unknown game target filters to no events instead of silently displaying all-target damage");
    }
}
