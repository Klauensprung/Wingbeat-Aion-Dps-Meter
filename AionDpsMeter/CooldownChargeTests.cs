using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class CooldownChargeTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        NetworkEvent Charge(long at, int count, int ms, int skill = 17100120, int actor = 1) =>
            new("skill-charges", at, "test", JsonSerializer.SerializeToElement(new { ActorId = actor, SkillCode = skill, Charges = count, RechargeMs = ms }));
        NetworkEvent Cooldown(long at, int ms) => new("skill-cooldown", at, "test", JsonSerializer.SerializeToElement(new { SkillCode = 17100120, RemainingMs = ms }));
        var cases = new (string Name, NetworkEvent[] Events, long Now, ChargeReading? Expected)[] {
            ("Initial charges", [Charge(1000, 2, 5000)], 1000, new(2, 5, 5)),
            ("Same variant duration", [Charge(1000, 2, 5000), Charge(2000, 1, 3000)], 2000, new(1, 3, 5)),
            ("Changed variant duration", [Charge(1000, 2, 5000), Charge(2000, 1, 3000, 17100121)], 2000, new(1, 3, 3)),
            ("Cooldown retains charges", [Charge(1000, 2, 5000), Cooldown(2000, 1000)], 2000, new(2, 1, 5)),
            ("Zero retains charges", [Charge(1000, 2, 5000), Cooldown(2000, 0)], 2000, new(2, 0, 5)),
            ("Expiry does not invent charge", [Charge(1000, 0, 5000)], 7000, new(0, 0, 5)),
            ("Foreign actor", [Charge(1000, 2, 5000, actor: 2)], 1000, null),
            ("New positive cast clears charges", [Charge(1000, 2, 5000), new("skill-use", 2000, "test", JsonSerializer.SerializeToElement(new { ActorId = 1, SkillCode = 17100120, CooldownMs = 8000 }))], 2000, null)
        };
        var fixtures = new List<object>();
        foreach (var scenario in cases) {
            var state = new CooldownTracker(); foreach (var e in scenario.Events) state.Capture(e, 1);
            var actual = state.Charges(17100000, scenario.Now);
            check(actual == scenario.Expected, "Compact charge state: " + scenario.Name);
            fixtures.Add(new { scenario.Name, scenario.Events, scenario.Now, Expected = actual });
        }
        File.WriteAllText(Path.Combine(temp, "cooldown-charge-reference.json"), JsonSerializer.Serialize(fixtures));
        var live = new CooldownTracker(); live.Capture(Charge(1000, 2, 5000), 1);
        var snapshot = new Fight { LocalPlayer = 1, LiveCooldowns = live.Copy() }; snapshot.Actors[1] = new(1, "Local", true, false, 0, "Cleric", 1);
        var observer = new CooldownTracker(); observer.Observe(snapshot, 2000, 100);
        observer.Observe(snapshot, 2000, 1100);
        check(observer.Charges(17100000, 1100) == new ChargeReading(2, 3, 5), "Repeated compact charge snapshots preserve monotonic recharge time");
        var settingsRow = CooldownPresentation.Rows(new Preferences { CooldownSkills = [17100000] }, new SkillNames(), observer, true, 1100).Single();
        check(settingsRow.Remaining.EndsWith("recharge") && settingsRow.Status == "Last reported: 2 charge(s)",
            "Cooldown settings distinguish the recharge timer from last reported charge availability");
        live.Reset(); snapshot.LiveCooldowns = live.Copy(); observer.Observe(snapshot, 2000, 1100);
        check(observer.Charges(17100000, 1100) == null, "Charge state removal propagates to an existing observer");
        var projections = new List<object>(); bool projectionBounds = true;
        foreach (int count in new[] { 0, 1, 2, 3 }) foreach (int capacity in new[] { 1, 2, 3, 5 })
        foreach (int duration in new[] { 500, 5000 }) foreach (int offset in new[] { -1, 0, 1, 499, 500, 5000, 15000, 60000 }) {
            var state = new CooldownCharges(); var e = Charge(1000, count, duration); state.Apply(e, 1, 1000, 1000);
            long now = 1000 + duration + offset;
            var projected = state.Display(17100000, now, capacity)!;
            projectionBounds &= projected.Count >= count && projected.Count <= projected.Capacity && projected.RemainingSeconds >= 0 &&
                state.Reading(17100000, now)!.Count == count;
            projections.Add(new { Timer = new { charges = count, endsAt = 1000 + duration, durationMs = duration }, Capacity = capacity, Now = now, Expected = projected });
        }
        check(projectionBounds, "Charge recovery estimates stay within capacity and never mutate the reported count");
        File.WriteAllText(Path.Combine(temp, "charge-display-reference.json"), JsonSerializer.Serialize(projections));
        check(SkillChargeCapacity.LoadError == null && SkillChargeCapacity.Capacity(17100000, 8, [1]) == 3 &&
            SkillChargeCapacity.Capacity(17100000, 7, [1]) == 1 && SkillChargeCapacity.Capacity(17100000, 8, [2]) == 1 &&
            SkillChargeCapacity.Capacity(17100120, 8, [1]) == 1 && SkillChargeCapacity.Capacity(17100121, 8, [1]) == 1,
            "Charge capacity requires level and specialization and preserves exact/tens metadata precedence");
        using var catalogue = JsonDocument.Parse(typeof(SkillChargeCapacity).Assembly.GetManifestResourceStream("Wingbeat.SkillCharges.json")!);
        var capacityFixtures = new List<object>();
        foreach (var key in catalogue.RootElement.GetProperty("Rows").EnumerateObject())
        foreach (int id in new[] { int.Parse(key.Name), int.Parse(key.Name) + 1 })
        foreach (int level in new[] { 0, 7, 8, 12, 16, 20, 255 })
        foreach (int[] choices in new int[][] { [], [1], [4], [1, 2, 3, 4, 5] })
            capacityFixtures.Add(new { Skill = id, Level = level, Choices = choices, Expected = SkillChargeCapacity.Capacity(id, level, choices) });
        File.WriteAllText(Path.Combine(temp, "charge-capacity-reference.json"), JsonSerializer.Serialize(capacityFixtures));
        live.Capture(Charge(1000, 0, 1000), 1); snapshot.LiveCooldowns = live.Copy();
        snapshot.Actors[1] = snapshot.Actors[1] with { CapturedSkills = new(1000, 1000, true, true, [new(17100000, 8)], [new(17100000, [1])]) };
        observer.Observe(snapshot, 1000, 1000);
        check(observer.ChargeDisplay(17100000, 3000) is { Count: 2, Capacity: 3, RemainingSeconds: 1 },
            "Live charge display recovers multiple charges using captured specialization capacity");
        snapshot.Actors[1] = snapshot.Actors[1] with { CapturedSkills = null }; observer.Observe(snapshot, 1000, 1000);
        check(observer.ChargeDisplay(17100000, 3000) is { Count: 1, Capacity: 1 }, "Missing loadout clears earlier charge capacity");
        var reopened=new CooldownTracker();reopened.Observe(snapshot,3500,100);
        check(reopened.ChargeDisplay(17100000,100,3) is {Count:2,Capacity:3,RemainingSeconds:.5},
            "Reopening after multiple recharge intervals preserves elapsed time before observer creation");
        var delayed=new CooldownCharges();delayed.Apply(Charge(1000,0,1000),1,3500,100);
        check(delayed.Display(17100000,100,3)==reopened.ChargeDisplay(17100000,100,3),
            "Delayed legacy charge reports preserve elapsed recharge intervals");
    }
}
