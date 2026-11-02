using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class ArchiveMemoryTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var current = FeatureTests.Sample();
        var full = Enumerable.Range(1, 100).Select(FeatureTests.Sample).ToArray();
        // Include incoming damage, a healing opener and a tied first timestamp.
        foreach (var f in full) {
            f.Healing.Add(new() { ActorId = 1, TargetId = 1, Timestamp = 0, Damage = 40, SkillCode = 17100001 });
            f.Damage.Add(new() { ActorId = 100, TargetId = 1, Timestamp = 1000, Damage = 80 });
        }
        var compact = full.Select(FightArchiveSummary.Create).ToArray();
        var rich = FeatureTests.Sample();
        rich.Actors[rich.LocalPlayer] = rich.Actors[rich.LocalPlayer] with {
            Collections = new(new(1, [new(100, 2)])), Daevanion = new(1, [new(100, [1, 2])]),
            Wings = new(1, [new(100, 3)]), Titles = new(1, [100], [new(1, 100)]),
            VisibleEquipment = new(1, true, [new(1, 100, 0, 1, true)])
        };
        var richSummary = FightArchiveSummary.Create(rich);
        check(richSummary.Actors[rich.LocalPlayer] is { Collections: null, Daevanion: null, Wings: null, Titles: null, VisibleEquipment: null } && rich.Actors[rich.LocalPlayer].Collections != null,
            "Archive list summaries release detail-only collection and appearance snapshots without mutating the full fight");
        var richStore = new FightStore(Path.Combine(temp, "collection-details")); richStore.Save(rich);
        var restored = richStore.Load(Path.Combine(richStore.Root, rich.Id + ".json")).Actors[rich.LocalPlayer];
        check(restored.Collections?.Skins?.Entries.Single().Value == 2 && restored.Daevanion?.Boards.Single().Nodes.Length == 2 &&
            restored.Wings?.Owned.Single().Level == 3 && restored.Titles?.Owned.Single() == 100 && restored.VisibleEquipment?.Items.Length == 1,
            "Opening saved fight details restores collection and appearance metadata omitted from archive list summaries");
        var actor = Analysis.Totals(current).Single(r => r.Id == 1);
        string Name(int code) => "Skill " + code;
        check(compact.All(f => f.Damage.Count + f.Healing.Count + f.Defense.Count + f.Network.Count + f.Deaths.Count == 0),
            "Archive summaries retain no raw combat or network events");
        check(Benchmarks.Trends(full).SequenceEqual(Benchmarks.Trends(compact)), "Compact archive preserves every DPS/HPS trend");
        check(Benchmarks.Report(current, full).SequenceEqual(Benchmarks.Report(current, compact)), "Compact archive preserves report percentiles and cohort counts");
        check(Benchmarks.Cohort(current, actor, full, Name).SequenceEqual(Benchmarks.Cohort(current, actor, compact, Name)), "Compact archive preserves skill shares and hit rates");
        check(Benchmarks.Differences(current, actor, full).SequenceEqual(Benchmarks.Differences(current, actor, compact)), "Compact archive preserves opening damage, damage received and crit comparisons");
        check(Benchmarks.Openers(current, actor, full, Name).SequenceEqual(Benchmarks.Openers(current, actor, compact, Name)), "Compact archive preserves first skill and healing opener comparisons");
        check(Benchmarks.Specs(current, actor, full, Name).SequenceEqual(Benchmarks.Specs(current, actor, compact, Name)) &&
            Benchmarks.Equipment(current, actor, full).SequenceEqual(Benchmarks.Equipment(current, actor, compact)), "Compact archive preserves loadout comparisons");
        check(EncounterOverview.History(current, current, full, "Example").Select(f => (f.Id, f.Seconds, Title: EncounterOverview.Title(f)))
            .SequenceEqual(EncounterOverview.History(current, current, compact, "Example").Select(f => (f.Id, f.Seconds, Title: EncounterOverview.Title(f)))),
            "Compact history preserves searches, ordering, encounter names and durations");
        var store = new FightStore(Path.Combine(temp, "compact-history"));
        store.Save(full[0]);
        bool refused = false;
        try { store.Save(compact[0].Copy()); } catch (InvalidOperationException) { refused = true; }
        check(refused && store.Load(store.Files().Single()).Damage.Count == full[0].Damage.Count,
            "Archive summaries cannot overwrite full fight files; opening detail restores all events");
        var demo = SelfTest.Demo(); var demoSummary = FightArchiveSummary.Create(demo);
        check(demoSummary.ArchiveSummary!.HasPvp == PvpAnalysis.Build(demo).HasOpponents &&
            demoSummary.ArchiveSummary.Events == demo.Damage.Count + demo.Healing.Count && !Benchmarks.Trends([demoSummary]).Any(),
            "Compact archive preserves PvP presence, event counts and demo exclusion");
        var healingOnly = FeatureTests.Sample(); healingOnly.Damage.Clear();
        healingOnly.Actors[999] = new(999, "Earlier bystander", true, false, 0, "Unknown", 1);
        var healingSummary = FightArchiveSummary.Create(healingOnly);
        check(Benchmarks.Trends([healingOnly]).SequenceEqual(Benchmarks.Trends([healingSummary])) &&
            !EncounterOverview.HasLocalDamage(healingSummary) && healingSummary.HasEvents && healingSummary.Seconds == healingOnly.Seconds,
            "Healing-only archive entries preserve HPS and duration without appearing as local damage fights");
        check(!healingSummary.Actors.ContainsKey(999) && !healingSummary.ArchiveSummary!.Names.Contains("Earlier bystander") &&
            !healingSummary.ArchiveSummary.PlayerNames.Contains("Earlier bystander") && healingSummary.ArchiveSummary.PlayerNames.Contains("Example cleric"),
            "History search includes this fight's participants rather than unrelated earlier bystanders");
    }

    // Opt-in read-only measurement against an existing archive, in a separate process.
    internal static int Measure(string[] args)
    {
        try {
            int folderIndex = Array.IndexOf(args, "--folder"), outputIndex = Array.IndexOf(args, "--output");
            if (folderIndex < 0 || folderIndex + 1 >= args.Length || outputIndex < 0 || outputIndex + 1 >= args.Length)
                throw new ArgumentException("Expected --folder and --output paths.");
            string folder = args[folderIndex + 1], output = args[outputIndex + 1];
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
            var store = new FightStore(folder);
            var entries = new List<Fight>();
            long bytes = 0, events = 0; int skipped = 0;
            var timer = Stopwatch.StartNew();
            foreach (var file in store.Files()) {
                try { var full = store.Load(file); bytes += new FileInfo(file).Length;
                    events += full.Damage.Count + full.Healing.Count + full.Defense.Count + full.Network.Count;
                    entries.Add(FightArchiveSummary.Create(full)); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentException) { skipped++; }
            }
            long retained = GC.GetTotalMemory(forceFullCollection: true);
            long? catalogueBytes = null, lootBytes = null;
            LootLedger? loot = null;
            if (args.Contains("--with-loot")) {
                _ = ItemNames.Default.Count; _ = ZoneNames.Default.Count;
                long afterCatalogues = GC.GetTotalMemory(forceFullCollection: true);
                catalogueBytes = afterCatalogues - retained;
                loot = new();
                // Forty synthetic sessions, each overflowing the per-session item
                // limit, exercise eviction without collecting private player data.
                for (int session = 0; session < 40; session++)
                    for (int item = 0; item < 600; item++) {
                        long time = 1700000000000L + session * 3600000L + item;
                        loot.Add(new(time, (uint)(session * 600 + item + 1), 500000000 + item, 1, 1, 1, 1010), "Synthetic player");
                        if (item % 100 == 0) _ = loot.OverlaySnapshot();
                    }
                lootBytes = GC.GetTotalMemory(forceFullCollection: true) - afterCatalogues;
            }
            using var process = Process.GetCurrentProcess();
            File.WriteAllText(output, JsonSerializer.Serialize(new { Fights = entries.Count, Skipped = skipped, FileBytes = bytes,
                EventsOnDisk = events, RetainedRawEvents = entries.Sum(f => f.Damage.Count + f.Healing.Count + f.Network.Count),
                ManagedBytes = retained, CatalogueManagedDelta = catalogueBytes, LootManagedDelta = lootBytes,
                LootRetainedRows = loot?.Rows().Length, LootOmittedSessions = loot?.OmittedSessions, LootOmittedItems = loot?.OmittedItems,
                WorkingSetBytes = process.WorkingSet64, PrivateBytes = process.PrivateMemorySize64, PeakWorkingSetBytes = process.PeakWorkingSet64,
                ElapsedSeconds = timer.Elapsed.TotalSeconds }, new JsonSerializerOptions { WriteIndented = true }));
            GC.KeepAlive(loot);
            GC.KeepAlive(entries);
            return 0;
        } catch (Exception ex) { LocalLog.Write("Archive measurement failed: " + ex); return 1; }
    }
}
