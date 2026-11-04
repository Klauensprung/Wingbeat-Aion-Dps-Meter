using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class PetLevelNoticeTests
{
    internal static void Run(Action<bool, string> check)
    {
        var engine = new CombatEngine();
        engine.Apply(new("local-player", Actor: new(1, "Player 1", true, false, 0, "Cleric", 1)));
        void Send(string kind, long time, object fields) => engine.Apply(new("network", Network: new(kind, time, "synthetic", JsonSerializer.SerializeToElement(fields))));
        Send("pet-roster", 1000, new CapturedPetRoster(1002, [new(1002, 1)], [], 0));
        check(engine.DrainPetNotices(1000).Length == 0, "Loading a pet roster does not announce existing pet levels");
        Send("pet-level", 2000, new CapturedPetLevel(1002, 2));
        Send("pet-level", 2000, new CapturedPetLevel(1002, 2));
        Send("pet-level", 1500, new CapturedPetLevel(1002, 3));
        engine.NewFight("New fight");
        check(engine.DrainPetNotices(2100) is [{ PetId: 1002, Level: 2 }] && engine.DrainPetNotices(2100).Length == 0,
            "Explicit pet level increases notify once across fight reset, ignoring duplicate and stale packets");
        Send("pet-level", 2200, new CapturedPetLevel(1005, 2));
        check(engine.DrainPetNotices(2300).Length == 0, "An unknown previous pet level is not treated as a newly earned level");
        Send("pet-level", 2400, new CapturedPetLevel(1002, 3));
        engine.Apply(new("local-player", Actor: new(1, "Player 1", true, false, 0, "Cleric", 1)));
        check(engine.DrainPetNotices(2500).Length == 0, "Character/zone boundaries discard pending pet notices from the earlier session");
        var queue = new PetLevelNotices();
        PetCollectionSnapshot State(int level, long time) => new(time, 0, true, [new(1002, level, 0, 0)]);
        queue.Observe(State(1, 1000), State(2, 1001), "pet-level");
        check(queue.Drain(31002).Length == 0, "Delayed pet notices older than thirty seconds are not replayed after a pause");
        for (int i = 0; i < 100; i++) queue.Observe(State(1, 1000 + i), State(2, 1001 + i), "pet-level");
        check(queue.Count == 64 && queue.Drain(2000).Length == 64 && queue.Count == 0,
            "Pending pet notices use a bounded queue that releases all entries after draining");
        queue.Observe(State(1, 1000), State(3, 2000), "pet-roster");
        check(queue.Count == 0, "A refreshed roster baseline cannot produce a batch of level-up announcements");
        var acquired = new CombatEngine();
        acquired.Apply(new("local-player", Actor: new(1, "Player 1", true, false, 0, "Cleric", 1)));
        void AddPet(long time, int level) => acquired.Apply(new("network", Network: new("pet-added", time, "synthetic", JsonSerializer.SerializeToElement(new CapturedPetLevel(1005, level)))));
        AddPet(1000, 1); AddPet(1001, 1);
        check(acquired.Fight.Pets?.Pets is [{ PetId: 1005, Level: 1, Souls: null }] &&
            acquired.DrainPetNotices(1002) is [{ PetId: 1005, Acquired: true }],
            "An explicit acquisition establishes ownership and announces once without inventing remaining souls");
        acquired.Apply(new("network", Network: new("pet-level", 2000, "synthetic", JsonSerializer.SerializeToElement(new CapturedPetLevel(1005, 2)))));
        acquired.DrainPetNotices(2000); AddPet(3000, 1);
        check(acquired.Fight.Pets?.Pets.Single().Level == 2 && acquired.DrainPetNotices(3000).Length == 0,
            "Repeated acquisition cannot downgrade a leveled pet or produce another collection notice");
        var rosterOnly = new CombatEngine();
        rosterOnly.Apply(new("network", Network: new("pet-roster", 1000, "synthetic", JsonSerializer.SerializeToElement(new CapturedPetRoster(1005, [new(1005, 1)], [], 0)))));
        rosterOnly.Apply(new("network", Network: new("pet-added", 1001, "synthetic", JsonSerializer.SerializeToElement(new CapturedPetLevel(1005, 1)))));
        check(rosterOnly.DrainPetNotices(1002).Length == 0, "Pets already present in a captured roster are not reannounced as newly collected");
        var gathering = new PetGatheringProgress();
        var pet = PetCatalogue.Default.Pets.First(p => p.Level2Souls > 0);
        PetCollectionSnapshot Progress(int? level, int? souls, long gains) => new(1000,0,true,[new(pet.Id,level,souls,gains)]);
        var before = Progress(0,0,0); var after = Progress(0,2,2);
        gathering.Observe(null,before,"pet-roster");
        check(gathering.Rows(before).Length == 0,"Loading existing pet totals never opens a pickup overlay");
        gathering.Observe(before,after,"pet-progress");
        check(gathering.Rows(after) is [{ Gained: 2 }] && gathering.Rows(after)[0].Progress.Contains("2 souls held") &&
            gathering.Rows(after)[0].Progress.Contains($"{Math.Max(0,pet.SummonSouls-2)} more"),"Pet pickups show held souls and the remaining collection requirement");
        var leveled = Progress(1,3,2);
        check(gathering.Rows(leveled)[0].Progress.Contains($"{Math.Max(0,pet.Level2Souls!.Value-3)} more for level 2"),"Collected pets automatically show the next level's remaining souls");
        gathering.Observe(after,leveled,"pet-progress-decrease");
        check(gathering.Rows(leveled)[0].Gained == 2,"Spending souls does not count as collecting more souls");
        check(gathering.Rows(Progress(null,null,2))[0].Progress.Contains("Soul total unknown"),"Missing roster totals stay unknown rather than treating pickup counts as inventory");
        gathering.Clear(); check(gathering.Rows(after).Length == 0,"Character boundaries clear recent pet collection rows");
        var pickupEngine = new CombatEngine();
        pickupEngine.Apply(new("local-player",Actor:new(1,"Collector",true,true,0,"Cleric",1)));
        pickupEngine.Apply(new("network",Network:new("pet-roster",1000,"test",JsonSerializer.SerializeToElement(new CapturedPetRoster(0,[],[],0)))));
        pickupEngine.Apply(new("network",Network:new("pet-progress",1100,"test",JsonSerializer.SerializeToElement(new { Gains = new[]{new CapturedPetProgress(pet.Id,2)} }))));
        pickupEngine.NewFight("Next fight");
        check(pickupEngine.PetGathering.Rows(pickupEngine.CurrentPets()) is [{ Gained:2 }],"Live pet pickup overlay receives decoded gains and survives DPS resets");
    }
}
