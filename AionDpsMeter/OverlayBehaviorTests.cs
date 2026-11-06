using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class OverlayBehaviorTests
{
    public static void Run(Action<bool, string> check, string temp)
    {
        var healthEngine=new CombatEngine();healthEngine.Apply(new("local-player",Actor:new(9161,"Self",true,true,0,"Cleric",1)));
        healthEngine.Apply(new("network",Network:new("hp",1000,"test",JsonSerializer.SerializeToElement(new{EntityId=9161,CurrentHp=22111,MaximumHp=22111}))));
        healthEngine.NewFight("Next fight");
        healthEngine.Apply(new("network",Network:new("hp",2000,"test",JsonSerializer.SerializeToElement(new{EntityId=9161,CurrentHp=22015}))));
        check(CapturedHealth.Build(healthEngine.Snapshot())[9161] is {Value:22015,Maximum:22111},"Fight reset retains confirmed local maximum HP when subsequent packets contain only current HP");
        for(int i=0;i<10;i++)healthEngine.NewFight("Next fight");
        check(healthEngine.Fight.Network.Count==0&&healthEngine.Fight.InitialHealth.Count==1,"HP carryover retains one compact state per actor rather than previous fight events");
        healthEngine.NewFight("New identity",preserveHealth:false);
        check(!CapturedHealth.Build(healthEngine.Snapshot()).ContainsKey(9161),"Identity-boundary reset discards previous character health");
        var healthFight = FeatureTests.Sample();
        void Hp(int id, long hp, long time) => healthFight.Network.Add(new("hp", time, "", JsonSerializer.SerializeToElement(new { EntityId = id, CurrentHp = hp })));
        Hp(1, 5000, 100); Hp(1, 6000, 90); Hp(2, 7000, 300);
        healthFight.Deaths.Add(new(1, 200)); healthFight.Deaths.Add(new(2, 200));
        var readings = CapturedHealth.Build(healthFight);
        check(readings[1].Value == 0 && readings[2].Value == 7000,
            "Captured health orders samples and deaths by timestamp and recognizes newer positive health after revival");
        Hp(1, 8000, 200); Hp(3, -1, 200); Hp(4, 10, -1);
        healthFight.Network.Add(new("hp", 300, "", JsonSerializer.SerializeToElement(new { EntityId = "invalid", CurrentHp = 100 })));
        healthFight.Network.Add(new("hp", 300, "", JsonSerializer.SerializeToElement("invalid")));
        readings = CapturedHealth.Build(healthFight);
        check(readings[1].Value == 0 && !readings.ContainsKey(3) && !readings.ContainsKey(4),
            "Death wins tied health timestamps and malformed or negative health is ignored");
        healthFight.Actors[99] = healthFight.Actors[2] with { Id = 99, OriginalEntityId = 2 };
        healthFight.Deaths.Clear();
        readings = CapturedHealth.Build(healthFight);
        check(!readings.ContainsKey(2) && !readings.ContainsKey(99) && CapturedHealth.Build(new Fight()).Count == 0,
            "Recycled entity IDs do not inherit ambiguous health and new fights start without stale health");
        var targeted = FeatureTests.Sample();
        targeted.Actors[100] = targeted.Actors[100] with { NpcId = 2300371 };
        targeted.Actors[101] = new(101, "Nearby creature", false, false, 0, "Unknown", 0);
        targeted.Actors[11] = new(11, "Nearby player", true, false, 0, "Ranger", 0);
        targeted.Damage.Add(new() { ActorId = 1, TargetId = 101, Damage = 9000, Timestamp = 61000 });
        targeted.Damage.Add(new() { ActorId = 100, TargetId = 1, Damage = 4000, Timestamp = 20000 });
        targeted.Damage.Add(new() { ActorId = 11, TargetId = 100, Damage = 500, Timestamp = 20000 });
        var bossView = OverlayCombatFilter.Apply(targeted, true, 0);
        var targetView = OverlayCombatFilter.Apply(targeted, false, 100);
        check(bossView.Damage.Count == 3 && bossView.Damage.All(h => h.TargetId == 100) &&
            targetView.Damage.SequenceEqual(bossView.Damage) && targetView.Seconds == targeted.Seconds &&
            PersonalSkills.Build(targetView, false).Sum(s => s.Rate) == 2000 / targeted.Seconds,
            "Boss and selected-target filters retain outgoing damage and full-encounter DPS duration");
        check(ReferenceEquals(OverlayCombatFilter.Apply(targeted, false, 0), targeted) && targeted.Damage.Count == 5 &&
            ReferenceEquals(targeted.Actors, targetView.Actors) && OverlayCombatFilter.Apply(targeted, false, 999).Damage.Count == 0,
            "Target views preserve the original snapshot, share metadata and handle empty targets");
        check(OverlayCombatFilter.Apply(targeted, false, 1).Healing.Sum(h => h.Damage) == 350 &&
            OverlayCombatFilter.Targets(targeted).Distinct().Count() == OverlayCombatFilter.Targets(targeted).Length,
            "Healing target filters select recipients and target choices are unique");
        var targetTotals = Analysis.Totals(targetView);
        check(MeterPresentation.Build(targetView, targetTotals, actorScope: "Self").Rows.Select(r => r.Id).SequenceEqual(new[] { 1 }) &&
            MeterPresentation.Build(targetView, targetTotals, actorScope: "Players", limit: 50).Rows.Any(r => r.Id == 11) &&
            MeterPresentation.Build(targetView, targetTotals, actorScope: "Party").Rows.All(r => r.Id != 11),
            "Self, nearby players and party scopes select distinct actors after target filtering");
        bool rejectedView = false;
        try { new FightStore(Path.Combine(temp, "filtered-view")).Save(targetView.Copy()); }
        catch (InvalidOperationException) { rejectedView = true; }
        check(rejectedView, "A filtered display or its copy cannot overwrite a saved encounter");
        check(!new Preferences().OverlayShowSkills && JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(new Preferences { OverlayShowSkills = true }))!.OverlayShowSkills,
            "Personal skill view is optional and persists through preferences");
        var skillsFight = FeatureTests.Sample();
        skillsFight.Damage.Add(new() { ActorId = 2, TargetId = 100, SkillCode = 999, Damage = 999999, Timestamp = 5000 });
        skillsFight.Damage.Add(new() { ActorId = 1, TargetId = 100, SkillCode = 17010001, Damage = 500, Timestamp = 5000, IsCritical = true });
        var ownSkills = PersonalSkills.Build(skillsFight, false);
        check(ownSkills.Length == 2 && ownSkills[0].Code == 17010001 && ownSkills.Sum(s => s.Amount) == 2500 &&
            Math.Abs(ownSkills.Sum(s => s.Share) - 100) < .001 && ownSkills[0].Fraction == 1 && ownSkills[0].Hits == 2 && ownSkills[0].Crit == 100 &&
            Math.Abs(ownSkills.Sum(s => s.Rate) - 2500 / skillsFight.Seconds) < .001,
            "Personal skills rank only local damage and preserve totals, shares, hits, crit and fight-wide DPS");
        check(PersonalSkills.Build(skillsFight, true).Single().Amount == 1000,
            "Personal healing skills use only local healing events");
        skillsFight.LocalPlayer = 0;
        check(PersonalSkills.Build(skillsFight, false).Length == 0, "Missing local identity never guesses another player's skills");
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
        var crowded = new Fight();
        for (int id = 1; id <= 10000; id++) crowded.Actors[id] = new(id, $"Entity {id}", false, false, 0, "Unknown", 0);
        var fresh = new ParticipantColors();
        check(fresh.Apply(crowded, data).Rows.Select(r => r.Color).SequenceEqual(new ParticipantColors().Apply(new Fight(), data).Rows.Select(r => r.Color)),
            "Unrelated captured entities do not consume overlay participant colours");
        var many = Enumerable.Range(1, 10000).Select(id => rows[0] with { Id = id }).ToArray();
        var manyColours = fresh.Apply(crowded, data with { Rows = many });
        check(manyColours.Rows.Select(r => r.Color).Distinct().Count() == many.Length,
            "Large sessions exhaust the rounded palette safely without hanging or duplicating participant colours");
        check(fresh.Apply(new Fight(), data).Rows.Select(r => r.Color).SequenceEqual(coloured.Rows.Select(r => r.Color)),
            "Switching encounters resets participant colours after a large session");
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
        byte[] body = [12, ..BitConverter.GetBytes(17010020u), 13, 0, 0, 0, 0, 0, 0, 0, ..new byte[4], 0, 0];
        var decoded = decoder.Decode(new CapturedPayload(13328, 50000, [(byte)(body.Length + 6), 4, 0x8d, ..body]) { TimestampMs = 1000 });
        check(decoded.Events.Single().Network is { Kind: "entity-death" } death && death.Fields.GetProperty("EntityId").GetInt32() == 12,
            "Full death records count the victim even when the killer has no player identity");
        body = [12, 0, 0, 0, 0, 0, 0, 0];
        check(new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, [(byte)(body.Length + 6), 4, 0x8d, ..body])).Events.Count == 0,
            "Incomplete death records do not manufacture deaths");
    }
}
