using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Threading;

namespace AionDpsMeter.Local;

// Disconnect only providers belonging to a destroyed window, outside its native message callback.
internal static class ClosedWindowAutomation
{
    [DllImport("UIAutomationCore.dll", ExactSpelling = true)]
    static extern int UiaDisconnectProvider([MarshalAs(UnmanagedType.Interface)] IRawElementProviderSimple provider);

    sealed class ProviderAccess(FrameworkElement owner) : FrameworkElementAutomationPeer(owner)
    {
        internal IRawElementProviderSimple Get(AutomationPeer peer) => ProviderFromPeer(peer);
    }

    internal static void Attach(Window window)
    {
        IRawElementProviderSimple[] pending = [];
        window.Closing += (_, _) => {
            var root = UIElementAutomationPeer.FromElement(window);
            if (root == null) return;
            var access = new ProviderAccess(window);
            var seen = new HashSet<AutomationPeer>(); var queue = new Stack<AutomationPeer>();
            var providers = new List<IRawElementProviderSimple>(); queue.Push(root);
            while (queue.TryPop(out var peer) && seen.Count < 4096) {
                if (!seen.Add(peer)) continue;
                try {
                    providers.Add(access.Get(peer));
                    foreach (var child in peer.GetChildren() ?? []) queue.Push(child);
                } catch (ElementNotAvailableException) { }
            }
            pending = providers.ToArray();
        };
        window.Closed += (_, _) => {
            var providers = pending; pending = [];
            if (providers.Length == 0) return;
            window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
                foreach (var provider in providers) {
                    int result = UiaDisconnectProvider(provider);
                    if (result < 0) LocalLog.Write($"Closed window accessibility cleanup failed: 0x{result:X8}.");
                }
            }));
        };
    }
}
