using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

// Opt-in diagnostics for data preparation and WPF rotation rendering, not capture.
public static class UiPerformance
{
    static double Previous(Fight f, int[] actors)
    {
        double checksum = 0;
        foreach (int actor in actors)
        foreach (var e in f.Damage.Where(e => e.ActorId == actor).TakeLast(5000))
        {
            checksum += (e.Timestamp - f.Start) / 1000d;
            checksum += (e.Timestamp - f.Start) / 1000d / f.Seconds;
        }
        return checksum;
    }
    static double Current(Fight f, int[] actors) => Analysis.Timeline(f, actors).Sum(e => e.Seconds + e.Fraction);
    static object Measure(Fight f, string scenario)
    {
        var actors = f.Damage.Select(e => e.ActorId).Distinct().Take(10).ToArray();
        double oldValue = Previous(f, actors), newValue = Current(f, actors);
        if (Math.Abs(oldValue - newValue) > Math.Max(1, Math.Abs(oldValue)) * 1e-10) throw new InvalidDataException("Timeline benchmark results differ.");
        (double Ms, long Bytes) Time(Func<double> run)
        {
            var samples = new List<(double Ms, long Bytes)>();
            for (int i = 0; i < 3; i++)
            {
                long allocation = GC.GetAllocatedBytesForCurrentThread(); var sw = Stopwatch.StartNew();
                _ = run(); sw.Stop(); samples.Add((sw.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocation));
            }
            return samples.OrderBy(s => s.Ms).ElementAt(1);
        }
        var before = Time(() => Previous(f, actors)); var after = Time(() => Current(f, actors));
        return new { Scenario = scenario, Events = f.Damage.Count, BeforeMs = before.Ms, AfterMs = after.Ms,
            BeforeAllocatedBytes = before.Bytes, AfterAllocatedBytes = after.Bytes, Equivalent = true };
    }
    static object MeasureArchive()
    {
        var current = FeatureTests.Sample(); var actor = Analysis.Totals(current).Single(r => r.Id == 1);
        var archive = Enumerable.Range(1, 100).Select(FeatureTests.Sample).ToArray();
        foreach (var fight in archive)
            for (int i = 0; i < 5000; i++) fight.Damage.Add(new() { ActorId = i % 10 + 1, TargetId = 100, SkillCode = 17010001 + i % 20 * 10000, Timestamp = 1000 + i * 6, Damage = 100 + i % 300 });
        var sw = Stopwatch.StartNew(); var report = Benchmarks.Report(current, archive); sw.Stop(); double reportMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart(); var peers = Benchmarks.Peers(current, actor, archive);
        var skills = Benchmarks.Cohort(current, actor, archive, id => id.ToString(), peers);
        var specs = Benchmarks.Specs(current, actor, archive, id => id.ToString(), peers);
        var equipment = Benchmarks.Equipment(current, actor, archive, peers);
        var openers = Benchmarks.Openers(current, actor, archive, id => id.ToString(), peers);
        var differences = Benchmarks.Differences(current, actor, archive, peers); sw.Stop();
        return new { Fights = archive.Length, DamageEvents = archive.Sum(f => f.Damage.Count), ReportMs = reportMs, CombinedComparisonMs = sw.Elapsed.TotalMilliseconds,
            MatchingObservations = peers.Length, Rows = report.Length + skills.Length + specs.Length + equipment.Length + openers.Length + differences.Length };
    }
    public static int Run(string[] args)
    {
        try
        {
            int outputIndex = Array.IndexOf(args, "--output");
            if (outputIndex < 0 || outputIndex + 1 >= args.Length) throw new ArgumentException("Missing --output.");
            var reports = new List<object>();
            int fightIndex = Array.IndexOf(args, "--fight");
            if (fightIndex >= 0 && fightIndex + 1 < args.Length)
            {
                string path = Path.GetFullPath(args[fightIndex + 1]);
                var f = new FightStore(Path.GetDirectoryName(path)!).Load(path);
                if (f.Damage.Count > 5000) throw new ArgumentException("Baseline benchmark is limited to 5,000 hits because its cost is quadratic.");
                reports.Add(Measure(f, "Saved fight"));
            }
            foreach (int count in new[] { 1000, 5000 })
            {
                var f = new Fight();
                for (int i = 0; i < count; i++) f.Damage.Add(new DamageEvent { ActorId = i % 10, Timestamp = 100000 + i * 20, Damage = i, SkillCode = 17040120 });
                reports.Add(Measure(f, "Synthetic ten actors"));
            }
            string output = Path.GetFullPath(args[outputIndex + 1]);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonSerializer.Serialize(new { Scope = "Timeline data: median of three warm runs. Archive: one populated run. RotationRendering: warmed WPF build/layout/bitmap render plus 10,000 pointer hit tests; excludes ingestion.", Results = reports, Archive = MeasureArchive(), RotationRendering = RotationTests.Benchmark() }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex) { LocalLog.Write("UI benchmark failed: " + ex); return 1; }
    }
}
