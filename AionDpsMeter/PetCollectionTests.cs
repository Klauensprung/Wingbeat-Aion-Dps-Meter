using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class PetCollectionTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        NetworkEvent Event(string kind, long time, object fields) => new(kind, time, "synthetic", JsonSerializer.SerializeToElement(fields));
        NetworkEvent Gain(long time, int id, int souls) => Event("pet-progress", time, new { Gains = new[] { new CapturedPetProgress(id, souls) } });
        ExportReferenceCases(temp);
        var roster = Event("pet-roster", 100, new CapturedPetRoster(1002, [new(1002, 1)], [new(1002, 5)], 0));
        var first = PetCollection.Apply(null, roster)!;
        var sparse = PetCollection.Apply(null, Event("pet-roster", 90, new CapturedPetRoster(1002, [new(1002, 1)], [], 0)))!;
        check(sparse.Pets.Single().Souls == 0 && PetCollection.Apply(sparse, Gain(91, 1005, 2))!.Pets.Single(p => p.PetId == 1005) is { Level: 0, Souls: 2 },
            "Sparse full rosters establish zero omitted balances and permit exact later gains for unowned pets");
        check(PetCollection.KnownState(null, false, 1005) == null && PetCollection.KnownState(null, true, 1005) is { Level: 0, Souls: 0 },
            "Only a complete roster turns absence into known zero ownership and soul balance");
        var next = PetCollection.Apply(first, Gain(101, 1002, 2))!;
        check(first.Pets.Single().Souls == 5 && next.Pets.Single().Souls == 7 && next.Pets.Single().ObservedGains == 2,
            "Pet soul gains update a fresh snapshot without changing archived progress");
        var spent = PetCollection.Apply(next, Event("pet-progress-decrease", 102, new { Gains = new[] { new CapturedPetProgress(1002, 3) } }))!;
        check(spent.Pets.Single().Souls == 4 && spent.Pets.Single().ObservedGains == 2 && next.Pets.Single().Souls == 7,
            "Soul spending subtracts remaining progress without subtracting earned totals or mutating snapshots");
        var missingSpend = PetCollection.Apply(spent, Event("pet-progress-decrease", 103, new { Gains = new[] { new CapturedPetProgress(1002, 10) } }))!;
        check(missingSpend.Pets.Single().Souls == null, "An inconsistent soul balance becomes unknown rather than a fabricated negative count");
        var acquiredOther = Event("pet-added", 104, new CapturedPetLevel(1005, 2, 2));
        check(ReferenceEquals(first, PetCollection.Apply(first, acquiredOther, 1)), "Other characters' pet acquisitions do not modify the local collection");
        var upgraded = PetCollection.Apply(first, Event("pet-added", 105, new CapturedPetLevel(1002, 2, 1)), 1)!;
        check(upgraded.Pets.Single().Level == 2, "An acquisition notification can report a higher level for an already owned pet");
        check(upgraded.Pets.Single().Souls == 5, "Pet acquisition preserves a known soul balance until a separate spending notification changes it");
        var upgradeSpend = Event("pet-progress-decrease", 106, new { Gains = new[] { new CapturedPetProgress(1002, 3) } });
        var spendAfter = PetCollection.Apply(upgraded, upgradeSpend, 1)!;
        var spendBefore = PetCollection.Apply(PetCollection.Apply(first, upgradeSpend with { Timestamp = 104 }, 1),
            Event("pet-added", 105, new CapturedPetLevel(1002, 2, 1)), 1)!;
        check(spendAfter.Pets.Single() == spendBefore.Pets.Single() && spendAfter.Pets.Single() is { Level: 2, Souls: 2, ObservedGains: 0 },
            "Separate acquisition and spending updates yield the same balance in either arrival order");
        var sparseAcquired = PetCollection.Apply(sparse, Event("pet-added", 106, new CapturedPetLevel(1005, 1, 1)), 1)!;
        check(sparseAcquired.Pets.Single(p => p.PetId == 1005).Souls == 0,
            "Acquisition of a pet absent from a full roster preserves its known zero soul balance");
        check(ReferenceEquals(next, PetCollection.Apply(next, Gain(99, 1002, 2))), "Stale pet packets do not roll back current collection state");
        var leveled = PetCollection.Apply(next, Event("pet-level", 102, new CapturedPetLevel(1002, 2)))!;
        check(leveled.Pets.Single().Level == 2 && leveled.Pets.Single().Souls == null && leveled.Pets.Single().ObservedGains == 2,
            "Pet leveling preserves observed gains without inventing remaining souls");
        var partial = PetCollection.Apply(null, Gain(103, 99999, 3))!;
        var unknown = PetCollectionView.Build(PetCatalogue.Default, partial).Single(p => p.Id == 99999);
        check(!partial.HasRoster && unknown.Level == "?" && unknown.Souls == "?" && unknown.Gains == "3" && unknown.Captured,
            "Unlisted pets and partial gains remain visible without invented ownership, level or absolute progress");
        var activeOnly = PetCollectionView.Build(PetCatalogue.Default, new(104, 99999, true, []));
        check(activeOnly.Single(p => p.Id == 99999).Active == "Active", "Active pets remain visible even when roster entries are absent");
        var engine = new CombatEngine();
        engine.Apply(new("local-player", Actor: new(1, "Player 1", true, false, 0, "Cleric", 1)));
        engine.Apply(new("network", Network: roster));
        engine.Apply(new("network", Network: Gain(101, 1002, 2)));
        var archived = engine.Snapshot();
        engine.NewFight("Next fight");
        engine.Apply(new("network", Network: Gain(102, 1002, 1)));
        check(archived.Pets!.Pets.Single().Souls == 7 && engine.Fight.Pets!.Pets.Single().Souls == 8 && engine.Fight.Pets.Pets.Single().ObservedGains == 3,
            "Fight reset carries pet progress forward exactly once without modifying the prior fight");
        var store = new FightStore(Path.Combine(temp, "pets")); store.Save(archived);
        var loaded = store.Load(Path.Combine(store.Root, archived.Id + ".json"));
        check(loaded.Pets?.ActivePetId == 1002 && loaded.Pets.Pets.Single().Souls == 7,
            "Captured collection state survives saving and loading a local fight");
        var genusRoster = new CapturedPetRoster(1002, [new(1002, 1)], [], 0) { Genera = [new(2, 3, 1, [new(0, 2, 19, -5, true, false)])] };
        var genusState = PetCollection.Apply(null, Event("pet-roster", 100, genusRoster))!;
        var genusGain = PetCollection.Apply(genusState, Gain(101, 1002, 1))!;
        check(genusGain.Genera!.Single().Effects.Single() is { Value: -5, Locked: true, Activated: false } && genusState.Pets.Single().Souls == 0,
            "Pet soul gains preserve selected genus effects and signed stat values without changing earlier snapshots");
        var genusUpgradeEvent = Event("pet-added", 102, new CapturedPetLevel(1002, 1, 1, new(2, 4)));
        var genusUpgrade = PetCollection.Apply(genusGain, genusUpgradeEvent, 1)!;
        check(genusUpgrade.Genera!.Single().Level == 4 && genusUpgrade.Genera!.Single().Effects.Single().Value == -5 && genusGain.Genera!.Single().Level == 3 &&
            genusUpgrade.Pets.Single() == genusGain.Pets.Single(), "An acquisition can change a known genus level without changing the pet level, souls or selected-panel effects");
        check(ReferenceEquals(genusUpgrade, PetCollection.Apply(genusUpgrade, genusUpgradeEvent, 1)) &&
            ReferenceEquals(genusGain, PetCollection.Apply(genusGain, genusUpgradeEvent, 2)),
            "Duplicate genus updates and another character's acquisitions cannot alter local state");
        check(PetCollection.Apply(first, genusUpgradeEvent, 1)!.Genera == null,
            "Acquisition genus levels do not invent an unobserved panel or genus baseline");
        using (var genusFixtures = new StreamWriter(Path.Combine(temp, "reference-genus-state.jsonl"))) {
            PetCollectionSnapshot? state = null;
            void Capture(NetworkEvent message) {
                state = PetCollection.Apply(state, message, 1);
                genusFixtures.WriteLine(JsonSerializer.Serialize(new { Event = message, Expected = state?.Genera }));
            }
            Capture(Event("pet-roster", 100, genusRoster));
            Capture(genusUpgradeEvent);
            Capture(genusUpgradeEvent);
            Capture(Event("pet-added", 103, new CapturedPetLevel(1002, 2, 2, new(2, 20))));
            Capture(Event("pet-added", 104, new CapturedPetLevel(1002, 2, 1, new(3, 20))));
            Capture(Event("pet-roster", 105, genusRoster with { Genera = [] }));
            Capture(Event("pet-added", 106, new CapturedPetLevel(1002, 3, 1, new(2, 30))));
        }
        using (var fixtures = new ReferencePacketFixtures(Path.Combine(temp, "reference-pet-acquisition-synthetic.jsonl"))) {
            for (byte type = 0; type <= 6; type++) {
                var body = new PartyWireTests.Writer(); body.Var(1); body.Int(1002); body.Int(2); body.Raw(type); body.Int(type * 10); body.Long(123456789);
                var frame = new PartyWireTests.Writer(); frame.Var(body.Bytes.Count + 6); frame.Raw(0x09, 0x90); frame.Raw(body.Bytes.ToArray());
                fixtures.Add(new CapturedPayload(13328, 50000, frame.Bytes.ToArray()) { TimestampMs = 1 });
            }
        }
        var genusFight = engine.Snapshot(); genusFight.Pets = genusGain; store.Save(genusFight);
        check(store.Load(Path.Combine(store.Root, genusFight.Id + ".json")).Pets!.Genera!.Single().Level == 3,
            "Compact selected genus panels survive saved-fight persistence");
        check(PetCollection.Apply(genusGain, Event("pet-roster", 102, genusRoster with { Genera = [] }))!.Genera is { Length: 0 },
            "An authoritative empty genus list clears old selected-panel effects");
        engine.Apply(new("local-player", Actor: new(2, "Player 2", true, false, 0, "Cleric", 1)));
        check(engine.Fight.Pets == null, "Changing the local character clears the previous character's pet collection");
        var transition = new CombatEngine();
        void Send(NetworkEvent message) => transition.Apply(new("network", Network: message));
        void Prepare(long time) => Send(Event("local-zone", time, new { MapId = 1010, AwaitingIdentity = true }));
        Send(roster); Prepare(101);
        check(transition.Fight.Pets == null && transition.DrainPetNotices(101).Length == 0,
            "Pre-identity pet rosters remain unassigned and produce no notifications");
        transition.Apply(new("local-player", Actor: new(1, "First", true, false, 0, "Cleric", 1)));
        check(transition.Fight.Pets?.Pets.Single().Souls == 5, "Initial pet roster survives map preparation and binds to the login identity");
        var firstOwner = transition.Snapshot(); Prepare(110);
        Send(Event("pet-roster", 111, new CapturedPetRoster(1005, [new(1005, 2)], [new(1005, 12)], 0)));
        Send(Gain(112, 1005, 2)); Prepare(110); Send(Gain(109, 1002, 50));
        check(transition.Fight.Pets!.Pets.Single().PetId == 1002 && transition.Fight.Pets.Pets.Single().Souls == 5 && transition.DrainPetNotices(112).Length == 0,
            "New character pet rosters and gains cannot overwrite the prior owner or trigger their notifications");
        transition.Apply(new("local-player", Actor: new(2, "Second", true, false, 0, "Assassin", 1)));
        check(transition.Fight.Pets?.Pets is [{ PetId: 1005, Souls: 14, ObservedGains: 2 }] && firstOwner.Pets!.Pets.Single().Souls == 5 && transition.Fight.Network.Count == 0,
            "Pending pet baseline and gains bind once to the new character with duplicate and stale preparation ignored");
        Prepare(120); Send(roster with { Timestamp = 121 }); Prepare(122);
        transition.Apply(new("local-player", Actor: new(3, "Third", true, false, 0, "Cleric", 1)));
        check(transition.Fight.Pets == null, "A newer map preparation discards superseded pending pet data");
        bool rejected = false;
        try { PetCollection.Apply(first, Event("pet-progress", 105, new { Gains = new[] { new CapturedPetProgress(1002, 1), new CapturedPetProgress(1002, 2) } })); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected && first.Pets.Single().Souls == 5, "Invalid duplicate pet gains are rejected atomically");
        var refreshed = PetCollection.Apply(next, roster with { Timestamp = 106 })!;
        check(refreshed.Pets.Single().Souls == 5 && refreshed.Pets.Single().ObservedGains == 0,
            "A fresh absolute roster replaces accumulated progress rather than adding it twice");
        NetworkEvent Spend(long time, int amount) => Event("pet-progress-decrease", time, new { Gains = new[] { new CapturedPetProgress(1002, amount) } });
        var inferenceBase = PetCollection.Apply(null, Event("pet-roster", 200, new CapturedPetRoster(0, [new(1002, 1)], [new(1002, 100)], 0)))!;
        var partialSpend = PetCollection.Apply(inferenceBase, Spend(201, 10))!;
        var inferred = PetCollection.Apply(partialSpend, Spend(202, 15))!;
        check(partialSpend.Pets.Single() is { Level: 1, PendingSoulSpend: 10, LevelInferred: false } &&
            inferred.Pets.Single() is { Level: 2, Souls: 75, PendingSoulSpend: 0, LevelInferred: true } && inferenceBase.Pets.Single().Souls == 100,
            "Split exact-cost spending infers pet advancement without mutating prior snapshots");
        var restoredSpend = JsonSerializer.Deserialize<PetCollectionSnapshot>(JsonSerializer.Serialize(partialSpend))!;
        PetCollection.Validate(restoredSpend);
        check(PetCollection.Apply(restoredSpend, Spend(202, 15))!.Pets.Single() == inferred.Pets.Single(),
            "Saved pet snapshots preserve bounded pending spending for later reconciliation");
        var confirmed = PetCollection.Apply(inferred, Event("pet-added", 203, new CapturedPetLevel(1002, 2, 1)), 1)!;
        check(confirmed.Pets.Single() is { Level: 2, LevelInferred: false, Souls: 75 } &&
            PetCollection.Apply(inferred, Event("pet-added", 203, new CapturedPetLevel(1002, 1, 1)), 1)!.Pets.Single().LevelInferred,
            "A matching explicit acquisition confirms an inferred level while an older level cannot downgrade it");
        var inferredNotices = new PetLevelNotices();
        inferredNotices.Observe(partialSpend, inferred, "pet-progress-decrease");
        check(inferredNotices.Drain(202).Length == 0, "Soul-cost inference does not announce a directly confirmed pet level");
        inferredNotices.Observe(inferred, confirmed, "pet-added");
        check(inferredNotices.Drain(203) is [{ PetId: 1002, Level: 2 }], "Explicit confirmation still announces a level already shown as inferred");
        var resetInference = PetCollection.Apply(partialSpend, roster with { Timestamp = 204 })!;
        check(resetInference.Pets.Single() is { PendingSoulSpend: 0, AcquisitionSoulCredit: 0, LevelInferred: false },
            "Absolute pet roster clears transient inference state");
        var boundedSpend = PetCollection.Apply(inferenceBase, Spend(205, 1000000))!;
        check(boundedSpend.Pets.Single() is { PendingSoulSpend: 106, Level: 1, LevelInferred: false, Souls: null },
            "Unmatchable expenditure remains bounded and does not fabricate a level or negative balance");
        bool InvalidInference(PetProgressState pet) { try { PetCollection.Validate(inferred with { Pets = [pet] }); return false; } catch (InvalidDataException) { return true; } }
        check(InvalidInference(inferred.Pets[0] with { PendingSoulSpend = 107 }) && InvalidInference(inferred.Pets[0] with { AcquisitionSoulCredit = 6 }) &&
            InvalidInference(inferred.Pets[0] with { Level = null }), "Imported pet inference rejects invalid pending state, credit and inferred unknown levels");
        check(PetCollectionView.Build(PetCatalogue.Default, inferred).Single(p => p.Id == 1002).LevelSource == "Inferred" &&
            PetCollectionView.Build(PetCatalogue.Default, confirmed).Single(p => p.Id == 1002).LevelSource == "Captured" &&
            PetFarming.For(PetCatalogue.Default.Find(1002), inferred.Pets[0], 2).Status == "Complete (inferred)",
            "Collection and farming views distinguish inferred progression from explicitly captured levels");
        var recovery = new CombatEngine();
        recovery.Apply(new("local-player", Actor: new(1, "Player 1", true, false, 0, "Cleric", 1)));
        recovery.Apply(new("network", Network: roster));
        recovery.Apply(new("damage", Damage: new DamageEvent { ActorId = 1, TargetId = 100, Timestamp = 1000, Damage = 10 }));
        recovery.AdvanceTime(17000);
        recovery.Apply(new("network", Network: Gain(18000, 1002, 2)));
        recovery.Apply(new("heal", Damage: new DamageEvent { ActorId = 1, TargetId = 1, Timestamp = 19000, Damage = 10 }));
        recovery.AdvanceTime(35000);
        var completed = recovery.SaveCompleted(_ => { });
        check(completed.Last().Pets!.Pets.Single().Souls == 7,
            "Healing-only encounter recovery saves the latest collection instead of the older boss snapshot");
        recovery.Apply(new("damage", Damage: new DamageEvent { ActorId = 1, TargetId = 100, Timestamp = 36000, Damage = 10 }));
        check(recovery.Fight.Damage.Count == 2 && recovery.Fight.Pets!.Pets.Single().Souls == 7 && recovery.Fight.Pets.Pets.Single().ObservedGains == 2,
            "Resuming combat after idle recovery retains pet gains without replaying them");
    }
    static void ExportReferenceCases(string temp)
    {
        using var file = new StreamWriter(Path.Combine(temp, "reference-pet-state.jsonl"));
        foreach (string scenario in new[] { "ordinary", "spend-levels", "acquire-first", "split-spend", "spend-first" }) {
            PetCollectionSnapshot? state = null; long time = 1000;
            void Add(string kind, object fields) {
                var message = new NetworkEvent(kind, time++, "synthetic", JsonSerializer.SerializeToElement(fields));
                state = PetCollection.Apply(state, message, 1);
                file.WriteLine(JsonSerializer.Serialize(new { Scenario = scenario, Event = message,
                    Expected = state!.Pets.Select(p => new { p.PetId, p.Level, p.Souls }).ToArray() }));
            }
            void Spend(int amount) => Add("pet-progress-decrease", new { Gains = new[] { new CapturedPetProgress(1002, amount) } });
            bool owned = scenario is "ordinary" or "spend-levels" or "split-spend";
            Add("pet-roster", new CapturedPetRoster(0, owned ? [new(1002, 1)] : [], [new(1002, owned ? 100 : 30)], 0));
            switch (scenario) {
                case "ordinary":
                    Add("pet-progress", new { Gains = new[] { new CapturedPetProgress(1002, 3) } }); Spend(1); break;
                case "spend-levels": Spend(25); Spend(75); break;
                case "acquire-first": Add("pet-added", new CapturedPetLevel(1002, 1, 1)); Spend(5); Spend(25); break;
                case "split-spend": Spend(10); Spend(15); break;
                case "spend-first": Spend(25); Add("pet-added", new CapturedPetLevel(1002, 1, 1)); Spend(5); break;
            }
        }
        for (int level = 0; level <= 3; level++) for (int amount = 0; amount <= 110; amount++) {
            string scenario = $"boundary-{level}-{amount}";
            PetCollectionSnapshot? state = null;
            var messages = new[] {
                new NetworkEvent("pet-roster", 1000, "synthetic", JsonSerializer.SerializeToElement(new CapturedPetRoster(0, level > 0 ? [new(1002, level)] : [], [new(1002, 1000000)], 0))),
                new NetworkEvent("pet-progress-decrease", 1001, "synthetic", JsonSerializer.SerializeToElement(new { Gains = new[] { new CapturedPetProgress(1002, amount) } }))
            };
            foreach (var message in messages) {
                state = PetCollection.Apply(state, message, 1);
                file.WriteLine(JsonSerializer.Serialize(new { Scenario = scenario, Event = message,
                    Expected = state!.Pets.Select(p => new { p.PetId, p.Level, p.Souls }).ToArray() }));
            }
        }
    }

}
