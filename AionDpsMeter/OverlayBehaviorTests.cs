using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class OverlayBehaviorTests
{
    public static void Run(Action<bool, string> check, string temp)
    {
        check(!new Preferences().OverlayPinLocal && !JsonSerializer.Deserialize<Preferences>("{}")!.OverlayPinLocal &&
            JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(new Preferences { OverlayPinLocal = true }))!.OverlayPinLocal,
            "Pinning is opt-in for fresh and existing settings and persists when enabled");
        var colours = new ParticipantColors();
        var roster = new Fight();
        var rows = Enumerable.Range(1, 50).Select(id => new MeterRow(id, "Same name", "Cleric", id, id, 1, 1, "#FFFFFF", false, true)).ToArray();
        var data = new MeterData(rows, 1275, 1275, "PvP");
        var coloured = colours.Apply(roster, data);
        check(coloured.Rows.Select(r => r.Color).Distinct().Count() == 50, "Participant colours remain unique beyond the initial palette, including same-class opponents");
        var reordered = colours.Apply(roster, data with { Rows = rows.Reverse().Select(r => r with { Name = "Captured name", Job = "Ranger" }).ToArray() });
        var joined = colours.Apply(roster, data with { Rows = [rows[0] with { Id = 100 }, ..rows] });
        check(coloured.Rows.All(r => reordered.Rows.Single(x => x.Id == r.Id).Color == r.Color && joined.Rows.Single(x => x.Id == r.Id).Color == r.Color) &&
            colours.Apply(roster, data with { Rows = [rows[7]] }).Rows[0].Color == coloured.Rows[7].Color,
            "Participant colours survive rank changes, new arrivals, identity updates and filtered damage/healing views");
        var duel = PvpTests.Duel();
        check(PvpAnalysis.Meter(duel, PvpAnalysis.Build(duel), pinLocal: true).Rows[0].Id == 1 &&
            PvpAnalysis.Meter(duel, PvpAnalysis.Build(duel)).Rows[0].Id == 2,
            "PvP pinning changes rank only when enabled");
        var engine = new CombatEngine();
        foreach (var actor in duel.Actors.Values) engine.Apply(new("actor", Actor: actor));
        engine.Apply(new("local-player", Actor: duel.Actors[1]));
        engine.Apply(new("actor", Actor: new(9, "Boss", false, false, 0, "Unknown", 0) { NpcId = 100 }));
        foreach (var hit in duel.Damage) engine.Apply(new("damage", Damage: hit));
        void Death(object fields, long time) => engine.Apply(new("network", Network: new("entity-death", time, "04 8D", JsonSerializer.SerializeToElement(fields))));
        Death(new { EntityId = 1 }, 21000); Death(new { EntityId = 1 }, 21500);
        Death(new { EntityId = 1 }, 20000); Death(new { EntityId = 1 }, 30000);
        Death(new { EntityId = 2 }, 30000); Death(new { EntityId = "bad" }, 40000); Death("bad", 40000);
        var snapshot = engine.Snapshot();
        check(snapshot.Deaths.Count == 3 && MeterPresentation.Build(snapshot, Analysis.Totals(snapshot)).Rows.Single(r => r.Id == 1).Deaths == 2 &&
            PvpAnalysis.Meter(snapshot, PvpAnalysis.Build(snapshot)).Rows.Single(r => r.Id == 2).Deaths == 1,
            "Death counts follow victims in both overlays, ignore duplicate/older reports and tolerate malformed metadata");
        var store = new FightStore(Path.Combine(temp, "overlay-behavior")); store.Save(snapshot);
        check(store.Load(Path.Combine(store.Root, snapshot.Id + ".json")).Deaths.SequenceEqual(snapshot.Deaths) && JsonSerializer.Deserialize<Fight>("{}")!.Deaths.Count == 0,
            "Death history survives saving and older fights default to zero deaths");
        engine.Apply(new("damage", Damage: duel.Damage[0] with { Timestamp = 20001 }));
        check(engine.Fight.Id == snapshot.Id && engine.Fight.Damage.Count == 4 && engine.Fight.Deaths.Count == 3,
            "Continued combat preserves damage and observed deaths");
        engine.Apply(new("actor", Actor: new(2, "OldPlayer", true, false, 0, "Cleric", 2301)));
        engine.Apply(new("actor", Actor: new(2, "NewTarget", false, false, 0, "Unknown", 0) { NpcId = 101 }));
        check(engine.Fight.Id == snapshot.Id && engine.Fight.Damage.Count == 4 &&
            engine.Fight.Actors.Values.Any(a => a.Name == "OldPlayer") && engine.Fight.Deaths.All(d => d.EntityId != 2) && snapshot.Deaths.Any(d => d.EntityId == 2),
            "Recycled target IDs retain encounter totals and archive previous deaths without altering prior snapshots");
        engine.EndFight("Next pull"); engine.SaveCompleted(store);
        check(engine.Fight.Deaths.Count == 0 && engine.Fight.Actors.Values.All(a => a.OriginalEntityId == 0) && !engine.Fight.HasEvents && engine.Fight.Id != snapshot.Id,
            "Explicit New fight clears combat and death counts");
        Death(new { EntityId = 1 }, 30000);
        check(engine.Fight.Deaths.Count == 1, "Death deduplication resets with the encounter");
        var decoder = new EuGameDecoder();
        byte[] body = [12, ..BitConverter.GetBytes(17010020u), 13, 0, 0];
        var decoded = decoder.Decode(new CapturedPayload(13328, 50000, [(byte)(body.Length + 6), 4, 0x8d, ..body]) { TimestampMs = 1000 });
        check(decoded.Events.Single().Network is { Kind: "entity-death" } death && death.Fields.GetProperty("EntityId").GetInt32() == 12,
            "Full death records count the victim even when the killer has no player identity");
        body = [12, 0, 0, 0, 0, 0, 0, 0];
        check(new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, [(byte)(body.Length + 6), 4, 0x8d, ..body])).Events.Count == 0,
            "Ambiguous empty appearance records do not manufacture deaths");
    }
}
