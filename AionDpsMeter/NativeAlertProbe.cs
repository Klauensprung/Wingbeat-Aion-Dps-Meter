using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace AionDpsMeter.Local;

internal static class NativeAlertProbe
{
    internal static int Run()
    {
        const string reportPath = "test-artifacts/native-alert-probe.json";
        Directory.CreateDirectory("test-artifacts");
        string sound = Path.GetFullPath("test-artifacts/native-alert-test.wav");
        // A quiet, brief generated tone; no external media or user settings.
        const int rate = 16000, samples = 4800;
        using (var writer = new BinaryWriter(File.Create(sound))) {
            writer.Write("RIFF"u8); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
            writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(samples * 2);
            for (int i = 0; i < samples; i++) {
                double envelope = Math.Min(1, Math.Min(i, samples - i - 1) / 320d);
                writer.Write((short)(Math.Sin(i * 2 * Math.PI * 660 / rate) * envelope * 6000));
            }
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var owner = new Window { Title = "Wingbeat notification test", ShowActivated = false, ShowInTaskbar = false, Width = 1, Height = 1 };
        new WindowInteropHelper(owner).EnsureHandle();
        var player = new MediaPlayer { Volume = .15 };
        bool displayed = false, audioStarted = false, opened = false, ended = false, failed = false, fromNotification = false;
        void Play(bool notification) {
            if (audioStarted) return;
            audioStarted = true; fromNotification = notification; player.Open(new Uri(sound)); player.Play();
        }
        player.MediaOpened += (_, _) => opened = true;
        player.MediaEnded += (_, _) => ended = true;
        player.MediaFailed += (_, _) => failed = true;
        using var notifications = new WindowsNotifications(owner, () => { displayed = true; Play(true); });
        bool accepted = notifications.Show("Wingbeat test notification", "Notification delivery check. This is a test, not an in-game event.");
        var elapsed = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) => {
            if (!audioStarted && elapsed.ElapsedMilliseconds >= 3000) Play(false);
            if (!(displayed && (ended || failed)) && elapsed.ElapsedMilliseconds < 10000) return;
            timer.Stop();
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new {
                NativeAccepted = accepted, DisplayCallbackObserved = displayed,
                AudioTriggeredByNotification = fromNotification, MediaOpened = opened, MediaEnded = ended, MediaFailed = failed,
                ElapsedMs = elapsed.ElapsedMilliseconds,
                Scope = "Real Shell_NotifyIconW and WPF MediaPlayer with a hidden owner window and a generated 300ms tone at volume 0.15. Display callback and media lifecycle are observable; physical speaker audibility and fullscreen visibility are not inferred."
            }, new JsonSerializerOptions { WriteIndented = true }));
            player.Close(); notifications.Dispose(); owner.Close(); app.Shutdown(accepted && displayed && ended && !failed ? 0 : 2);
        };
        timer.Start();
        return app.Run();
    }
}
