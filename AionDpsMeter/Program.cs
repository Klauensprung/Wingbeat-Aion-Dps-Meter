using System.IO;
using System.Windows;

namespace AionDpsMeter.Local;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        try {
            bool isolated = args.Any(a => a is "--self-test" or "--smoke-test" or "--benchmark-ui" or "--demo" or "--check-npcap" or "--check-game-adapter" or "--live-probe" or "--replay-pcap" or "--audit-fight");
            AppDataPaths.Initialize(isolated);
        } catch (Exception ex) {
            MessageBox.Show("Could not prepare Wingbeat user data. Original files have been retained.\n" + ex.Message, "Wingbeat storage error");
            return 1;
        }
        if (args.Contains("--self-test")) return SelfTest.Run();
        if (args.Contains("--check-npcap")) return SelfTest.CheckNpcap();
        if (args.Contains("--check-game-adapter")) return GameAdapterTests.CheckLive();
        if (args.Contains("--live-probe")) return LiveProbe.Run(args);
        if (args.Contains("--replay-pcap")) return PacketReplay.Run(args);
        if (args.Contains("--audit-fight")) return SkillAudit.Run(args);
        if (args.Contains("--benchmark-ui")) return UiPerformance.Run(args);
        var app = new System.Windows.Application();
        app.DispatcherUnhandledException += (_, e) => {
            LocalLog.Write("UI error: " + e.Exception);
            MessageBox.Show(e.Exception.Message, "Wingbeat Local", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        try {
            bool smoke = args.Contains("--smoke-test");
            var window = new MainWindow(smoke || args.Contains("--demo"));
            if (smoke) window.ContentRendered += (_, _) => window.Dispatcher.BeginInvoke(new Action(() => {
                try { window.RunVisualSmokeTest(); app.Shutdown(0); }
                catch (Exception ex) { File.WriteAllText("smoke-test-error.txt", ex.ToString()); app.Shutdown(1); }
            }));
            return app.Run(window);
        }
        catch (Exception ex) { LocalLog.Write("Startup failed: " + ex); MessageBox.Show(ex.ToString(), "Startup failed"); return 1; }
    }
}
