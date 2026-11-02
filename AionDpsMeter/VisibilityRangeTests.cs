using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class VisibilityRangeTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var ranges = new VisibilityRanges();
        var classes = new VisibilityRanges();
        double[] distances = [8190, 8191, 14175, 14176, 30975, 30976];
        var distant = distances.Select((distance, i) => new SpatialEntity(i + 1, 2000001 + i, 0, 0, 5000, distance, 0, 0, 5000, Kind: "npc")).ToArray();
        classes.Classify(1010, distant, new(1010, 0, 0, 0, 5000), null, 5000, 0);
        int[] classifications = distant.Select(e => classes.NpcClass(e.NpcId)).ToArray();
        check(classifications.SequenceEqual(new[] { 0, 1, 1, 2, 2, 3 }),
            "Measured NPC visibility promotes range classes at the reference 1.05 distance boundaries");
        check(classes.Find(1010, distant[1]) == new VisibilityRange(new(9000, 11800), new(11500, 13500)) &&
            classes.Find(1010, distant[3]) == new VisibilityRange(new(20000, 25500), new(26000, 29500)) && classes.Find(1010, distant[5]) == null,
            "Extended NPC classes use their distinct bands and effectively unbounded NPCs provide no radius constraint");
        classes.Classify(1010, distant.Select(e => e with { X = 100 }), new(1010, 0, 0, 0, 5100), null, 5100, 0);
        check(distant.Select(e => classes.NpcClass(e.NpcId)).SequenceEqual(classifications), "Closer sightings never downgrade learned NPC range classes");
        var skipped = new VisibilityRanges();
        skipped.Classify(1010, distant.Select(e => e with { Moving = true }), new(1010, 0, 0, 0, 5000), null, 5000, 0);
        skipped.Classify(1010, distant, new(1110, 0, 0, 0, 5000), null, 5000, 0);
        skipped.Classify(1010, distant, null, new(0, 0, 0, 0, 0, 301, 1), 5000, 0);
        check(distant.All(e => skipped.NpcClass(e.NpcId) == 0), "Moving NPCs, wrong-map measurements and uncertain estimates cannot teach extended visibility");
        var npc = new SpatialEntity(100, 2000001, 0, 0, 5000, 5000, 0, 0, 5000, Kind: "npc");
        var player = new CapturedPosition(1010, 0, 0, 0, 5000);
        for (int i = 0; i < 19; i++) ranges.Learn(1010, npc, true, player, 5000);
        check(ranges.Find(1010, npc) == VisibilityRanges.Default, "Map visibility ranges retain defaults until twenty usable samples exist");
        ranges.Learn(1010, npc, true, player, 5000);
        var learned = ranges.Find(1010, npc)!;
        check(learned.Enter == new VisibilityBand(5000, 5000) && learned.Leave == VisibilityRanges.Default.Leave,
            "Learned visibility quantiles replace only the sufficiently sampled direction");
        var obj = npc with { NpcId = 0, ObjectId = 123, Kind = "object" };
        check(ranges.Find(1010, obj) == null, "An unlearned object cannot inherit the ordinary NPC visibility radius");
        for (int i = 0; i < 2; i++) {
            ranges.Learn(1010, obj, true, player, 5000);
            ranges.Learn(1010, obj with { X = 7000 }, false, player, 5000);
        }
        var objectRange = ranges.Find(1010, obj);
        check(objectRange == new VisibilityRange(new(4000, 7000), new(5000, 8000)), "Object entry and exit medians produce asymmetric visibility bands");
        var invalid = new VisibilityRanges();
        var inferredRanges = new VisibilityRanges();
        var center = new PositionMoments(0, 0, null, 0, 0, 300, 1);
        for (int i = 0; i < 2; i++) {
            inferredRanges.Learn(1010, obj with { Z = 9000 }, true, null, 5000, center);
            inferredRanges.Learn(1010, obj with { X = 7000, Z = 9000 }, false, null, 5000, center);
        }
        check(inferredRanges.Find(1010, obj) == objectRange,
            "Precise inferred centers teach unknown object ranges using horizontal distance when height is unknown");
        long inferredRevision = inferredRanges.Revision;
        inferredRanges.Learn(1010, obj, true, null, 5000, center);
        check(inferredRanges.Revision == inferredRevision, "Known object ranges cannot reinforce themselves from inferred positions");
        var rejectedInference = new VisibilityRanges();
        foreach (double accuracy in new[] { 300.01, double.NaN, double.PositiveInfinity, -1 })
            rejectedInference.Learn(1010, obj, true, null, 5000, center with { Accuracy = accuracy });
        rejectedInference.Learn(1010, npc, true, null, 5000, center);
        check(rejectedInference.Revision == 0, "Invalid or uncertain centers and ordinary NPCs cannot teach inferred visibility ranges");
        var directPreference = new VisibilityRanges();
        for (int i = 0; i < 2; i++) {
            directPreference.Learn(1010, obj, true, player, 5000, center with { X = 4000 });
            directPreference.Learn(1010, obj with { X = 7000 }, false, player, 5000, center with { X = 4000 });
        }
        check(directPreference.Find(1010, obj) == objectRange, "Fresh captured positions take precedence over inferred object-learning centers");
        for (int i = 0; i < 25; i++) {
            invalid.Learn(1010, npc with { Moving = true }, true, player, 5000);
            invalid.Learn(1010, npc, true, player, 5501);
            invalid.Learn(1110, npc, true, player, 5000);
        }
        check(invalid.Find(1010, npc) == VisibilityRanges.Default, "Moving entities, stale measurements and mismatched maps cannot teach visibility ranges");
        var scene = new SpatialEvidence();
        NetworkEvent Event(string kind, long time, object data) => new(kind, time, "test", JsonSerializer.SerializeToElement(data));
        scene.Apply(Event("local-zone", 1000, new { MapId = 1010 }), 1);
        var estimator = new PositionEstimator(1234); estimator.Constrain(new(1010, 200, 0, 0, 5000, 5000, 3500, false, "test"));
        estimator.ApplyVisibility(Event("clock", 5000, new { }), scene, null);
        var enter = Event("actor-position", 5000, new { ActorId = 100, X = 7000, Y = 0, Z = 50, EntityKind = "npc", VisibilityReason = 1 });
        scene.Apply(enter, 1); var before = estimator.Moments!;
        estimator.ApplyVisibility(enter, scene, null); var after = estimator.Moments!;
        check(after.X > before.X + 300 && after.Z == null, "A valid entry edge weights the existing cloud toward its plausible visibility band without copying NPC height");
        estimator.ApplyVisibility(enter, scene, null);
        check(estimator.Moments == after, "Duplicate view edges cannot repeatedly narrow the particle distribution");
        var empty = new PositionEstimator(1); empty.ApplyVisibility(enter, scene, null);
        check(empty.Moments is { Accuracy: > 2500 } && empty.Current == null, "A lone visible NPC initializes broad particles without publishing an overconfident player position");
        SpatialEntity[] Players(int count, int far) => Enumerable.Range(0, count).Select(i => new SpatialEntity(i + 1, 0, 0, 0, 1000,
            i < far ? 11000 : 1000, 0, 0, 1000, Kind: "player")).ToArray();
        var globalCases = new List<object>();
        bool thresholds = true;
        foreach (var c in new (int Count, int Far, long At, long Measured, bool Expected)[] { (9, 9, 5000L, 1000L, false),
            (10, 4, 5000L, 1000L, false), (10, 5, 5000L, 1000L, true), (10, 10, 4999L, 1000L, false),
            (10, 10, 9001L, 1000L, false), (10, 10, 9000L, 1000L, true) }) {
            var learnedGlobal = new VisibilityRanges();
            learnedGlobal.DetectEverywhere(1010, Players(c.Count, c.Far), new(1010, 0, 0, 0, c.Measured), c.At, 1000);
            bool detected = learnedGlobal.Everywhere(1010, "player"); thresholds &= detected == c.Expected;
            globalCases.Add(new { c.Count, c.Far, c.At, c.Measured, Detected = detected });
        }
        check(thresholds, "Global visibility requires ten entities, at least half beyond the distance threshold, completed warmup and a recent measurement");
        var global = new VisibilityRanges();
        global.DetectEverywhere(1010, Players(10, 5), new(1010, 0, 0, 0, 1000), 5000, 1000);
        check(global.Find(1010, Players(1, 0)[0]) == null && global.Find(1110, Players(1, 0)[0]) != null && global.Find(1010, npc) != null,
            "Global visibility suppresses only the learned entity kind on that map");
        global.DetectEverywhere(1010, Players(10, 0), new(1010, 0, 0, 0, 5000), 5500, 1000);
        check(global.Everywhere(1010, "player"), "Global visibility classification runs once per map entry");
        global.DetectEverywhere(1010, Players(10, 0), new(1010, 0, 0, 0, 10000), 14000, 10000);
        check(!global.Everywhere(1010, "player"), "A later map entry can clear an outdated global-visibility classification");
        File.WriteAllText(Path.Combine(temp, "visibility-range-results.json"), JsonSerializer.Serialize(new { Learned = learned, Object = objectRange,
            InferredObject = inferredRanges.Find(1010, obj), DirectPreference = directPreference.Find(1010, obj),
            Before = before, After = after, Distances = distances, Classes = classifications, GlobalCases = globalCases }));
    }
}
