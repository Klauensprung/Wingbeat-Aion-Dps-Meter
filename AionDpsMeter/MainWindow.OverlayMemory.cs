using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int CountAliveOverlays(IEnumerable<WeakReference<Window>> references) => references.Count(r => r.TryGetTarget(out _));
    [MethodImpl(MethodImplOptions.NoInlining)]
    WeakReference<Window> ExerciseGenusLifetime()
    {
        var originalEngine = engine; var originalViewed = viewed; Window? window = null;
        try {
            engine = new CombatEngine(); viewed = null;
            engine.Apply(new("local-player", Actor: new(1, "Lifetime test", true, true, 0, "Cleric", 2301)));
            engine.Apply(new("network", Network: new("pet-roster", 1, "synthetic", JsonSerializer.SerializeToElement(
                new CapturedPetRoster(1002, [new(1002, 1)], [], 0) {
                    Genera = [new(2, 1, 0, [new(1, 2, 146, 1035, false, true)])]
                }))));
            var existing = Application.Current.Windows.Cast<Window>().ToHashSet();
            ShowPetGenus();
            window = Application.Current.Windows.Cast<Window>().Single(w => !existing.Contains(w));
            window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            return new(window);
        } finally { window?.Close(); engine = originalEngine; viewed = originalViewed; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference<Window>[] ExerciseOverlayLifetime()
    {
        var settings = new Preferences(); long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var spawn = PetSpawns.Default.ForPet(1002)[0].Spawns[0];
        var position = new CapturedPosition(1010, spawn.X, spawn.Y, spawn.Z, now);
        var collection = new PetCollectionSnapshot(now, 1002, true, [new(1002, 1, 1, 0)]);
        var nearby = new PetNearbyTracker().Update(position, collection, 2);
        var map = new PetMapOverlayWindow(settings, () => { }); map.Update(nearby, position, now, true, 2);
        var tracker = new PetTrackerOverlayWindow(settings, () => { }, _ => { }, () => { }); tracker.Update(nearby, position, now, true, 2);
        var ledger = new LootLedger(); ledger.Add(new(now, 1, 110120003, 2, 30, 1, 1010), "Synthetic player");
        var loot = new LootOverlayWindow(settings, () => { }, () => { }); loot.Update(ledger.OverlaySnapshot(), true);
        var currencies = new CurrencyOverlayWindow(settings, () => { });
        currencies.Update([new(930100001, 0), new(930100003, 25000), new(930100031, 5000000000), new(512000014, 960)], true, "Synthetic player");
        var tickets = new TicketOverlayWindow(settings, () => { });
        tickets.Update([new(60000001, 100, 40, 0, 0, now), new(3, 0, 0, 18227000, 25200000, now)], true, true, "Synthetic player", 2301, now);
        var farming=new FarmingOverlayWindow(settings,()=>{},()=>{});
        farming.Update(new([new(new("npc",100,10),"Example target",new("npc",100,3,10,false,[]),2)],new(now,now,3,180,2,false),[new("spawn","npc:100","Example target",null,null)],true),true,1);
        Window[] windows = [map, tracker, loot, currencies, tickets, farming];
        try {
            foreach (var window in windows) {
                window.Show(); window.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
            }
            return windows.Select(w => new WeakReference<Window>(w)).ToArray();
        } finally { foreach (var window in windows) window.Close(); }
    }
    internal void MeasureOverlayLifetime()
    {
        bool running = timer.IsEnabled; timer.Stop();
        var references = new List<WeakReference<Window>>(); var samples = new List<object>();
        int baselineWindows = Application.Current.Windows.Count;
        void Drain() {
            var frame = new DispatcherFrame();
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        }
        try {
            ClosedWindowAutomationTests.Run(this);
            for (int cycle = 0; cycle < 12; cycle++) {
                references.AddRange(ExerciseOverlayLifetime()); references.Add(ExerciseGenusLifetime());
                Drain();
                if (cycle is 1 or 5 or 11) {
                    using var process = Process.GetCurrentProcess(); process.Refresh();
                    samples.Add(new { Cycles = cycle + 1, ManagedBytes = GC.GetTotalMemory(true), WorkingSetBytes = process.WorkingSet64,
                        PrivateBytes = process.PrivateMemorySize64, WindowCount = Application.Current.Windows.Count,
                        ClosedWindowsStillAlive = CountAliveOverlays(references) });
                }
            }
            int immediateAlive = CountAliveOverlays(references);
            // WPF may queue weak-event cleanup after its current render/idle pass.
            // Measure both immediate retention and retention after a bounded settle period.
            var settleFrame = new DispatcherFrame();
            var settle = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            settle.Tick += (_, _) => { settle.Stop(); settleFrame.Continue = false; };
            settle.Start(); Dispatcher.PushFrame(settleFrame);
            Drain();
            int alive = CountAliveOverlays(references);
            File.WriteAllText("test-artifacts/overlay-memory.json", JsonSerializer.Serialize(new {
                Scope = "Synthetic WPF map/tracker/loot/currency/ticket/farming overlays and live pet genus window create, show, render, close (including placement and genus refresh timers); GC after dispatcher idle. Excludes live capture and long-running active fights.",
                RetainedTypes = references.Select(r => r.TryGetTarget(out var w) ? w.GetType().Name : null).Where(n => n != null).GroupBy(n => n).ToDictionary(g => g.Key!, g => g.Count()),
                RetainedIndices = references.Select((r, i) => new { Reference = r, Index = i }).Where(p => p.Reference.TryGetTarget(out _)).Select(p => p.Index).ToArray(),
                ImmediateClosedWindowsStillAlive = immediateAlive, SettleMilliseconds = 1000,
                AccessibilityIsolation = "Passed: canceled close remains accessible, destroyed control becomes unavailable, other live window remains accessible.",
                ClosedWindows = references.Count, ClosedWindowsStillAlive = alive, BaselineWindows = baselineWindows,
                FinalWindows = Application.Current.Windows.Count, Samples = samples
            }, new JsonSerializerOptions { WriteIndented = true }));
            if (alive != 0 || Application.Current.Windows.Count != baselineWindows)
                throw new InvalidOperationException("Overlay lifetime test retained closed windows; see overlay-memory.json.");
        } finally { if (running) timer.Start(); }
    }
}
