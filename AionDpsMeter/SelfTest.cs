using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;
using SharpPcap;

namespace AionDpsMeter.Local;

public static class SelfTest
{
    public static int CheckNpcap()
    {
        var lines = new List<string>(); int opened = 0;
        try
        {
            foreach (var device in SharpPcap.LibPcap.LibPcapLiveDeviceList.Instance)
            {
                try { device.Open(SharpPcap.DeviceModes.None, 100); device.Filter = "tcp"; opened++; lines.Add("PASS open/filter/close: " + device.Description); }
                catch (Exception ex) { lines.Add("UNAVAILABLE " + device.Description + ": " + ex.Message); }
                finally { try { device.Close(); } catch { } }
            }
        }
        catch (Exception ex) { lines.Add(ex.Message); }
        if (opened > 0)
        {
            try
            {
                Directory.CreateDirectory("test-artifacts");
                const string path = "test-artifacts/synthetic-recording.pcap";
                using (var writer = new SharpPcap.LibPcap.CaptureFileWriterDevice(path, FileMode.Create))
                {
                    writer.Open(new DeviceConfiguration { LinkLayerType = PacketDotNet.LinkLayers.Ethernet, Snaplen = 65535 });
                    writer.Write(new RawCapture(PacketDotNet.LinkLayers.Ethernet, new PosixTimeval(), new byte[60]));
                    writer.Close();
                }
                using var reader = new BinaryReader(File.OpenRead(path));
                if (reader.ReadUInt32() != 0xa1b2c3d4) throw new Exception("Invalid PCAP magic.");
                reader.BaseStream.Position = 20;
                if (reader.ReadUInt32() != 1 || reader.BaseStream.Length != 100) throw new Exception("Invalid PCAP link type or record length.");
                lines.Add("PASS synthetic PCAP recording header and packet length. No live traffic recorded.");
            }
            catch (Exception ex) { lines.Add("FAIL recording: " + ex.Message); File.WriteAllLines("npcap-check-results.txt", lines); return 1; }
        }
        lines.Add($"{opened} adapters opened. No packet capture was started; game decoding remains unverified without game traffic.");
        File.WriteAllLines("npcap-check-results.txt", lines);
        return opened > 0 ? 0 : 1;
    }
    public static Fight Demo()
    {
        var f = new Fight { Demo = true, Label = "Demo training encounter", LocalPlayer = 1 };
        for (int i = 1; i <= 10; i++) f.Actors[i] = new(i, $"Party {i:00}", true, true, 15000 + i * 20, "Gladiator", 1);
        for (int i = 11; i <= 13; i++) f.Actors[i] = new(i, $"Opponent {i - 10}", true, false, 15500, "Gladiator", 1);
        f.Actors[100] = new(100, "Training target", false, false, 0, "Unknown", 0);
        for (int second = 0; second <= 30; second++)
        for (int actor = 1; actor <= 10; actor++)
        {
            f.Damage.Add(new DamageEvent { ActorId = actor, TargetId = second % 5 == 0 ? 11 + actor % 3 : 100, SkillCode = 10010000 + actor * 10000, Timestamp = 100000 + second * 1000, Damage = 1000 + actor * 100 + second * 10 });
            if (actor == 10) f.Healing.Add(new DamageEvent { ActorId = 10, TargetId = 1 + second % 10, SkillCode = 10990000, Timestamp = 100000 + second * 1000, Damage = 500, IsHeal = true });
        }
        for (int i = 11; i <= 13; i++) f.Damage.Add(new DamageEvent { ActorId = i, TargetId = 1, SkillCode = 11000000, Timestamp = 110000, Damage = 500 });
        f.Defense.Add(new DefenseEvent { ActorId = 1, TargetId = 11, Timestamp = 120000, Outcome = DefenseOutcome.Evade });
        f.Defense.Add(new DefenseEvent { ActorId = 11, TargetId = 1, Timestamp = 120000, Outcome = DefenseOutcome.Block });
        f.Deaths.AddRange([new(1, 115000), new(1, 125000), new(3, 120000)]);
        f.Energy = new(840, 120, true, 100000);
        f.Specs[10020000] = new(10020000, 10, [1, 3]);
        return f;
    }
    public static int Run()
    {
        var results = new List<string>();
        string temp = Path.Combine(AppContext.BaseDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        void Check(bool condition, string label) { if (!condition) throw new Exception(label); results.Add("PASS " + label); }
        try
        {
            var f = Demo();
            var totals = Analysis.Totals(f);
            Check(totals.Count(r => r.Role == "Party") == 10, "All ten party actors retained");
            Check(totals.Sum(r => r.Damage) == f.Damage.Sum(e => (long)e.Damage), "Damage totals conserve all events");
            Check(f.Seconds == 30 && totals.First(r => r.Id == 1).DPS == Math.Round(f.Damage.Where(e => e.ActorId == 1).Sum(e => (long)e.Damage) / 30d, 1), "Shared fight duration and DPS");
            Check(totals.Single(r => r.Id == 10).Healing == 15500, "Healing kept separate from damage");
            var meter = MeterPresentation.Build(f, totals);
            Check(meter.Rows.Length == 10 && meter.Amount == 527000 && Math.Abs(meter.Rows.Sum(r => r.Share) - 100) < .0001 && meter.Rows.All(r => r.Id <= 10), "Party meter excludes opponent damage and accounts for every party member");
            Check(meter.Rows[0].Id == 10 && meter.Rows[0].Fraction == 1 && meter.Rows[^1].Id == 1 && Math.Abs(meter.Rate - 527000d / 30) < .0001, "Meter ranks damage against a shared duration and leading bar");
            var topThree = MeterPresentation.Build(f, totals, limit: 3);
            Check(topThree.Amount == meter.Amount && topThree.Rows[0].Share == meter.Rows[0].Share && topThree.Rows.Sum(r => r.Share) < 100, "Limiting visible rows does not inflate damage shares");
            var pinned = MeterPresentation.Build(f, totals, limit: 3, pinLocal: true);
            Check(pinned.Rows.Select(r => r.Id).SequenceEqual(new[] { 1, 10, 9 }) && pinned.Amount == meter.Amount &&
                pinned.Rows[0].Fraction == meter.Rows[^1].Fraction && pinned.Rows[1].Share == meter.Rows[0].Share,
                "Local player stays above the row cutoff without changing totals, shares or leader scaling");
            var waiting = new Fight { LocalPlayer = 1, Actors = new(f.Actors) };
            var idlePinned = MeterPresentation.Build(waiting, Analysis.Totals(waiting), pinLocal: true);
            var healingPinned = MeterPresentation.Build(f, totals, healing: true, pinLocal: true);
            Check(idlePinned.Rows.Length == 10 && idlePinned.Rows[0] is { Id: 1, Local: true, Amount: 0, Rate: 0 } &&
                healingPinned.Rows.Length == 10 && healingPinned.Rows.Take(2).Select(r => r.Id).SequenceEqual(new[] { 1, 10 }) && healingPinned.Amount == 15500,
                "All ten party members remain visible at zero with the local player first in healing mode");
            var healingMeter = MeterPresentation.Build(f, totals, healing: true);
            Check(healingMeter.Rows.Length == 10 && healingMeter.Rows[0].Id == 10 && healingMeter.Amount == 15500 && healingMeter.Rows[0].Share == 100, "Healing mode ranks healers without damage contamination and retains inactive party members");
            var noRoster = Demo();
            foreach (var id in noRoster.Actors.Keys.ToArray()) noRoster.Actors[id] = noRoster.Actors[id] with { Party = false };
            noRoster.Actors[13] = noRoster.Actors[13] with { Player = false, Job = "Unknown" };
            var playerMeter = MeterPresentation.Build(noRoster, Analysis.Totals(noRoster), limit: 50);
            Check(playerMeter.Scope == "Players" && playerMeter.Rows.Length == 12 && playerMeter.Rows.All(r => r.Id != 13), "Missing roster falls back to known players without treating unknown actors as players");
            Check(MeterPresentation.Build(noRoster, Analysis.Totals(noRoster), allActors: true, limit: 50).Rows.Length == 13, "All actors mode retains unclassified combatants");
            var unknownClass = new Actor(21, "Example player", true, false, 0, "Unknown", 1);
            Check(MeterPresentation.ColorFor(unknownClass) == MeterPresentation.ColorFor(unknownClass with { Id = 987 }) && MeterPresentation.ColorFor(unknownClass) != MeterPresentation.ColorFor(unknownClass with { Player = false }), "Player colour persists across entity ID changes; unclassified actors stay neutral");
            Check(Analysis.Opponents(f).Length == 3, "Concurrent PvP opponents all included");
            Check(Analysis.Defense(f).Single(r => r.Character == "Party 01").Avoided == 1, "Outgoing avoidance attributed to attacker");
            Check(Analysis.Defense(f).Single(r => r.Character == "Party 01").Blocked == 1, "Incoming block attributed to defender");
            var store = new FightStore(temp); store.Save(f); var copy = store.Load(store.Files().Single());
            Check(copy.Damage.Count == f.Damage.Count && copy.Healing.Count == f.Healing.Count && copy.Specs[10020000].Choices.SequenceEqual(new[] { 1, 3 }), "Fight persistence round-trip");
            store.Save(f); Check(store.Files().Count() == 1, "Autosave replaces same fight atomically");
            var unsafeFight = new Fight { Id = "../escape" };
            bool rejected = false; try { store.Save(unsafeFight); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Fight IDs cannot escape storage directory");
            Check(Benchmarks.Trends(new[] { f }).Length == 0 && Benchmarks.Report(f, new[] { f }).All(r => r.LocalPercentile == null), "Demo excluded from comparisons");
            f.Demo = false;
            var actor = Analysis.Totals(f).First(r => r.Id == 1);
            var peers = Enumerable.Range(0, 10).Select(_ => { var p = Demo(); p.Demo = false; return p; }).ToArray();
            Check(Benchmarks.Peers(f, actor, peers).Length == 130, "Cohort includes matching player observations");
            Check(Benchmarks.Report(f, peers).First(r => r.Character == actor.Character).TopFivePercentDPS != null, "Top 5% requires adequate local observations");
            peers[0].Label = "Different encounter";
            Check(Benchmarks.Peers(f, actor, peers).Length == 117, "Different encounters excluded");
            Check(Benchmarks.Report(f, Array.Empty<Fight>()).All(r => r.LocalPercentile == null), "No fabricated percentiles without samples");
            var tcp = new TcpAssembler();
            Check(tcp.Push(100, [1, 2]).SelectMany(x => x).SequenceEqual(new byte[] { 1, 2 }), "TCP initial payload");
            Check(!tcp.Push(104, [5, 6]).Any(), "TCP out-of-order segment buffered");
            Check(tcp.Push(102, [3, 4]).SelectMany(x => x).SequenceEqual(new byte[] { 3, 4, 5, 6 }), "TCP gap filled in order");
            Check(!tcp.Push(102, [3, 4]).Any(), "TCP retransmission ignored");
            Check(tcp.Push(105, [6, 7, 8]).SelectMany(x => x).SequenceEqual(new byte[] { 7, 8 }), "TCP overlap trimmed");
            var wrap = new TcpAssembler(); wrap.Push(uint.MaxValue, [1, 2]).ToArray();
            Check(wrap.Push(1, [3]).Single().SequenceEqual(new byte[] { 3 }), "TCP sequence wrap handled");
            var engine = new CombatEngine();
            for (int i = 0; i < 100; i++) engine.Process(new CapturedPayload(20000, 30000, new byte[i]));
            Check(!engine.Snapshot().HasEvents, "Empty / zero payloads do not manufacture combat events");
            engine.RecordDamage(new DamageEvent { ActorId = 1, TargetId = 100, SkillCode = 11020000, Timestamp = 100000, Damage = 50 }, false);
            engine.RecordDamage(new DamageEvent { ActorId = 1, TargetId = 100, SkillCode = 11020000, Timestamp = 101000, Damage = 75 }, false);
            Check(engine.Snapshot().Actors[1].Job == "Unknown", "Opaque skill IDs do not invent character class");
            string firstFightId = engine.Snapshot().Id;
            engine.RecordDamage(new DamageEvent { ActorId = 1, TargetId = 100, SkillCode = 11020000, Timestamp = 117000, Damage = 100 }, false);
            engine.SaveCompleted(store);
            Check(engine.Snapshot().Id == firstFightId && engine.Snapshot().Damage.Count == 3, "Damage-free boss phases preserve the active fight");
            var refs = typeof(CombatEngine).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
            var allowedRefs = new HashSet<string> { "WindowsBase", "PresentationCore", "PresentationFramework", "System.Xaml", "netstandard", "mscorlib", "SharpPcap", "PacketDotNet" };
            Check(refs.All(n => n != null && (n.StartsWith("System.") || n.StartsWith("Microsoft.") || allowedRefs.Contains(n))), "Runtime references limited to .NET and declared capture libraries");
            Check(engine.UnsupportedPayloads == 100, "Unsupported payloads reported explicitly");
            var eventFile = Path.Combine(temp, "events.jsonl");
            File.WriteAllLines(eventFile, new[] {
                JsonSerializer.Serialize(new DecodedCombatEvent("actor", Actor: new Actor(7, "Independent tester", true, true, 12000, "Unknown", 1))),
                JsonSerializer.Serialize(new DecodedCombatEvent("damage", Damage: new DamageEvent { ActorId = 7, TargetId = 100, Timestamp = 1000, Damage = 42, SkillCode = 99 })),
                JsonSerializer.Serialize(new DecodedCombatEvent("heal", Damage: new DamageEvent { ActorId = 7, TargetId = 7, Timestamp = 2000, Damage = 20, SkillCode = 98 }))
            });
            var normalized = new CombatEngine(); foreach (var item in CombatEventFile.Read(eventFile)) normalized.Apply(item);
            Check(normalized.Snapshot().Damage.Sum(e => e.Damage) == 42 && normalized.Snapshot().Healing.Sum(e => e.Damage) == 20 && normalized.Snapshot().Name(7) == "Independent tester", "Independent normalized event import");
            File.WriteAllText(eventFile, "{\"Kind\":\"damage\",\"Damage\":{\"Damage\":-5}}");
            bool badEvent = false; try { CombatEventFile.Read(eventFile); } catch (InvalidDataException) { badEvent = true; }
            Check(badEvent, "Malformed normalized events rejected before import");
            Check(NpcapSetup.IsLoadFailure(new TypeInitializationException("NativeCapture", new DllNotFoundException())), "Missing Npcap inside a native initialization failure prompts installation");
            Check(NpcapSetup.IsLoadFailure(new BadImageFormatException()) && NpcapSetup.IsLoadFailure(new EntryPointNotFoundException()), "Incompatible capture libraries offer Npcap repair");
            Check(!NpcapSetup.IsLoadFailure(new UnauthorizedAccessException()) && !NpcapSetup.IsLoadFailure(new InvalidOperationException("No adapters")), "Permissions and missing adapters are not misreported as a missing driver");
            WireTests.Run(Check, temp);
            TelemetryTests.Run(Check, temp);
            PlayerProfileCacheTests.Run(Check, temp);
            DisplayTests.Run(Check, temp);
            AssetTests.Run(Check, temp);
            ReadingTests.Run(Check, temp);
            OverlayBehaviorTests.Run(Check, temp);
            PvpTests.Run(Check);
            IngestionTests.Run(Check, temp);
            EncounterTests.Run(Check, temp);
            BossCatalogueTests.Run(Check);
            EncounterOverviewTests.Run(Check);
            FeatureTests.Run(Check, temp);
            RotationTests.Run(Check);
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "self-test-results.txt"), results.Append($"Passed {results.Count} checks."));
            return 0;
        }
        catch (Exception ex) { File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "self-test-results.txt"), results.Append("FAIL " + ex)); return 1; }
    }
}
