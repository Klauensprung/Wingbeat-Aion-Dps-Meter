using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Threading;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    internal async Task<int> MeasureWindowSession(string[] args)
    {
        string output = "test-artifacts/window-session-memory.json";
        try {
            int outputAt = Array.IndexOf(args, "--output"), roundsAt = Array.IndexOf(args, "--rounds");
            if (outputAt >= 0 && outputAt + 1 < args.Length) output = Path.GetFullPath(args[outputAt + 1]);
            int rounds = roundsAt >= 0 && roundsAt + 1 < args.Length ? int.Parse(args[roundsAt + 1]) : 100;
            if (rounds is < 25 or > 500) throw new ArgumentException("Window benchmark rounds must be between 25 and 500.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            timer.Stop(); FinishAutosave(); viewed = null; archive.Clear(); engine = new CombatEngine();
            preferences = new() { ReminderWindowsNotifications = false };
            Title = "Wingbeat - isolated session memory benchmark";
            var samples = new List<object>(); var watch = Stopwatch.StartNew();
            long allocated = GC.GetTotalAllocatedBytes(true), origin = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int refreshes = 0;
            void Network(string kind, long at, object value) => engine.Apply(new("network", Network: new(kind, at, "synthetic", JsonSerializer.SerializeToElement(value))));
            engine.Apply(new("local-player", Actor: new(1, "Synthetic local", true, true, 90000, "Cleric", 1)));
            for (int i = 2; i <= 6; i++) engine.Apply(new("actor", Actor: new(i, "Synthetic party " + i, true, true, 80000, "Gladiator", 1)));
            Network("local-zone", origin, new { MapId = 1010, X = 0, Y = 0, Z = 0 });
            Network("skill-levels", origin, new SkillLevelReading(true, [new(17010000, 13)]));
            Network("skill-choices", origin, new { Skills = new SkillChoiceValue[] { new(17010000, [1, 2]) } });
            Network("pet-roster", origin, new CapturedPetRoster(1002, [new(1002, 1)], [new(1002, 1)], 0));
            long inventoryCount = 1000;
            Network("inventory", origin, new InventoryBatch(true, [new(9000000000L, 110120003, inventoryCount, 1, 1, 0)]));
            string[] viewNames = ["Encounter", "Party", "Skills", "Healing", "Saved fights", "Boss analysis", "Rotations", "Player stats"];
            var measuredTabs = viewNames.Select(name => tabs.Items.Cast<TabItem>().Single(t => t.Header?.ToString() == name)).ToArray();
            void WriteReport(bool complete) {
                using var process = Process.GetCurrentProcess(); process.Refresh();
                File.WriteAllText(output, JsonSerializer.Serialize(new {
                    Complete = complete, Rounds = rounds, HitsPerRound = 1000, Refreshes = refreshes,
                    Views = measuredTabs.Select(t => t.Header?.ToString()).ToArray(), ElapsedSeconds = watch.Elapsed.TotalSeconds,
                    AllocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated, PeakWorkingSetBytes = process.PeakWorkingSet64,
                    ArchiveRawEvents = archive.Sum(f => f.Damage.Count + f.Healing.Count + f.Network.Count), SummariesValid = SummariesValid(), Samples = samples,
                    MetadataPerFight = new { OwnedPets = 136, GenusSlots = 30, DaevanionNodes = 270, Titles = 194, Wings = 7, Skins = 1000, Decorations = 10, Monoliths = 100 },
                    Scope = "Real main-window refresh/layout/dispatcher rendering with synthetic combat, current metadata, resets and compact archive summaries. Forced-GC samples measure retained memory. Excludes Npcap and file writes per fight. Does not exercise long single fights, secondary windows or overlays."
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            bool SummariesValid() => archive.All(f => f.DungeonRun is { Party.Length: 7, Splits.Length: 10, Skills.Length: 1, ClearedAt: not null } run &&
                run.ReceivedItems.SingleOrDefault(i => i.ItemId == 110120003)?.Count == 15 && run.UsedItems.SingleOrDefault(i => i.ItemId == 110120003)?.Count == 5);
            for (int round = 0; round < rounds; round++) {
                long time = origin + round * 60000L; int boss = 100 + round;
                Network("local-zone", time, new { MapId = 600012 });
                Network("dungeon-state", time, new DungeonStateReading(600012, 1, null, null));
                Network("dungeon-state", time, new DungeonStateReading(600012, 2, time, time + 60000));
                engine.Apply(new("party", Party: new([1, 2, 3, 4, 5, 6], "Synthetic roster") { Timestamp = time,
                    Summaries = Enumerable.Range(1, 7).Select(i => new PartyMemberSummary("Synthetic member " + i, 1, "Cleric", 50, 200, 90000 + round)).ToArray() }));
                Network("map-event-join", time, new MapEventReading(10, "Synthetic checkpoint", 1, 1, 0));
                engine.Apply(new("actor", Actor: new(boss, "Synthetic boss " + round, false, false, 0, "Unknown", 0)));
                Network("boss-cast", time, new { CasterId = boss, SkillCode = 123 });
                Network("visible-equipment", time, new EquipmentReading(1, true, [new(1, 110220003, 0, 5, true)]));
                Network("local-combat-power", time, new { CombatPower = 90000 + round });
                // Fresh decoded-sized snapshots on every fight expose retention
                // that a single shared baseline would hide.
                Network("pet-roster", time, new CapturedPetRoster(1002,
                    Enumerable.Range(1000, 136).Select(id => new CapturedPetLevel(id, 3)).ToArray(),
                    Enumerable.Range(1000, 136).Select(id => new CapturedPetProgress(id, round)).ToArray(), 0) {
                    Genera = Enumerable.Range(2, 5).Select(type => new PetGenus((byte)type, 3, 0,
                        Enumerable.Range(0, 6).Select(slot => new PetGenusEffect((byte)slot, 1, 19, round, false, true)).ToArray())).ToArray()
                });
                Network("daevanion", time, new { Boards = Enumerable.Range(1, 6).Select(board => new DaevanionBoard(board, Enumerable.Range(1, 45).ToArray())).ToArray() });
                Network("titles", time, new TitleSnapshot(time, Enumerable.Range(1, 194).ToArray(), [new(1, 1), new(2, 2), new(3, 3)]));
                Network("wings", time, new { Wings = Enumerable.Range(1, 7).Select(id => new CapturedWing(id, round % 20)).ToArray() });
                Network("skins", time, new { Entries = Enumerable.Range(1, 1000).Select(id => new CollectionValue(id, 1)).ToArray() });
                Network("decorations", time, new { Entries = Enumerable.Range(1, 10).Select(id => new CollectionValue(id, id)).ToArray() });
                Network("monoliths", time, new { Entries = Enumerable.Range(1, 100).Select(id => new CollectionValue(id, round)).ToArray() });
                for (int hit = 0; hit < 1000; hit++) {
                    long at = time + hit * 20;
                    engine.Apply(new("damage", Damage: new() { ActorId = 1 + hit % 6, TargetId = boss, SkillCode = 17010000 + hit % 10, Timestamp = at, Damage = 1000 }));
                    if (hit % 100 == 0) {
                        bool use = hit / 100 % 2 != 0; inventoryCount += use ? -1 : 3;
                        Network("inventory-update", at, new InventoryBatch(false,
                            [new(9000000000L, 110120003, inventoryCount, 1, 1, use ? (byte)0 : (byte)5, use ? (byte)14 : (byte)0)]));
                        Network("map-event-progress", at, new MapEventReading(10, null, hit == 900 ? (byte)3 : (byte)1, 1, hit / 100 + 1));
                        Network("abnormal-apply", at, new { TargetId = 1, CasterId = 1, Slot = hit % 20, AbnormalId = 1000, DurationMs = 2000 });
                        Network("actor-position", at, new MovementReading(1, hit, round, 0, false, 0));
                        Network("target", at, new { ActorId = boss, TargetId = 1 + hit % 6 });
                    }
                    if (hit % 250 == 249) {
                        tabs.SelectedItem = measuredTabs[(round * 4 + hit / 250) % measuredTabs.Length];
                        Refresh(); UpdateLayout(); refreshes++;
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    }
                }
                Network("dungeon-result", time + 20000, new DungeonResultReading(600012, true, null, 1, []));
                Network("local-zone", time + 20001, new { MapId = 1010 });
                engine.EndFight("Synthetic reset");
                engine.SaveCompleted(f => archive.Add(FightArchiveSummary.Create(f)));
                Refresh(); UpdateLayout(); refreshes++;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                if (round == 4 || (round + 1) % 25 == 0 || round + 1 == rounds) {
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    using var process = Process.GetCurrentProcess(); process.Refresh();
                    samples.Add(new { Fights = round + 1, ManagedBytes = GC.GetTotalMemory(true), WorkingSetBytes = process.WorkingSet64,
                        PrivateBytes = process.PrivateMemorySize64, Actors = engine.Fight.Actors.Count, ArchiveCount = archive.Count,
                        Checkpoints = archive.Sum(f => f.DungeonRun?.Splits.Length ?? 0), RosterMembers = archive.Sum(f => f.DungeonRun?.Party.Length ?? 0),
                        Windows = System.Windows.Application.Current.Windows.Count, QueuedFights = engine.HasCompletedFights });
                    WriteReport(false);
                }
            }
            WriteReport(true);
            return archive.Count == rounds && SummariesValid() && archive.All(f => f.Damage.Count + f.Healing.Count + f.Network.Count == 0) && !engine.HasCompletedFights ? 0 : 1;
        } catch (Exception ex) {
            File.WriteAllText(output + ".error.txt", ex.ToString()); return 1;
        }
    }
}
