using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace AionDpsMeter.Local;

internal static class ClosedWindowAutomationTests
{
    // UIA client calls run off the WPF thread while its dispatcher services provider requests.
    static T Client<T>(Func<T> read)
    {
        var task = Task.Run(read); var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        poll.Tick += (_, _) => { if (task.IsCompleted || DateTime.UtcNow >= deadline) frame.Continue = false; };
        poll.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { poll.Stop(); }
        if (!task.IsCompleted) throw new TimeoutException("Window accessibility client timed out.");
        return task.GetAwaiter().GetResult();
    }

    internal static void Run(Window owner)
    {
        Window Create(string name) {
            var button = new Button { Content = name };
            AutomationProperties.SetAutomationId(button, "test-button");
            var window = new Window { Owner = owner, Title = name, Content = button, Width = 220, Height = 120 };
            ClosedWindowAutomation.Attach(window); window.Show(); window.UpdateLayout(); return window;
        }
        var live = Create("Live accessibility test"); var closing = Create("Closing accessibility test");
        bool cancel = true; closing.Closing += (_, args) => args.Cancel = cancel;
        try {
            var liveHandle = new WindowInteropHelper(live).Handle;
            var closingHandle = new WindowInteropHelper(closing).Handle;
            AutomationElement Button(nint handle) => AutomationElement.FromHandle(handle).FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "test-button"));
            var oldButton = Client(() => Button(closingHandle));
            closing.Close();
            if (!closing.IsVisible || Client(() => oldButton.Current.Name) != "Closing accessibility test")
                throw new InvalidOperationException("Canceled close disconnected a live control.");
            cancel = false; closing.Close();
            var idle = new DispatcherFrame();
            owner.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => idle.Continue = false));
            Dispatcher.PushFrame(idle);
            bool unavailable = Client(() => {
                try { _ = oldButton.Current.Name; return false; }
                catch (ElementNotAvailableException) { return true; }
            });
            if (!unavailable || Client(() => Button(liveHandle).Current.Name) != "Live accessibility test")
                throw new InvalidOperationException("Closed-window accessibility cleanup affected the wrong providers.");
        } finally { cancel = false; closing.Close(); live.Close(); }
    }
}
