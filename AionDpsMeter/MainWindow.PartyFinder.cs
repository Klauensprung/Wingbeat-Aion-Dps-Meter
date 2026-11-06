using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowPartyFinder() => ShowPartyFinder(() => { lock (engine.Gate) return engine.PartyFinder.Snapshot(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), engine.Fight.Actors.GetValueOrDefault(engine.Fight.LocalPlayer)); });
    void ShowPartyFinder(Func<FinderGroup?> read)
    {
        var window = new Window { Title = global::AionDpsMeter.Local.Localization.T("Party finder"), Owner = this, Width = 920, Height = 500, MinWidth = 850, MinHeight = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences); var layout = new DockPanel { Margin = new(18) };
        var heading = UiSections.Heading("Party finder", "Group readiness", "Captured members of your current recruitment group. Readiness updates automatically while capture is running. A full group packet is required before individual joins can be shown. Once the formed party replaces this recruitment group, the finder view clears.");
        DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        var status = new TextBlock { Margin = new(0, 0, 0, 12) }; DockPanel.SetDock(status, Dock.Top); layout.Children.Add(status);
        var table = new LiveTable { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, EnableRowVirtualization = true, RowHeight = 36 };
        table.Columns.Add(new DataGridTextColumn { Header = "Player", Binding = new Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 160 });
        foreach (var (label, field, width) in new[] { ("Server", "Server", 75), ("Class", "Class", 110), ("Level", "Level", 65), ("Gear score", "GearScore", 95), ("CP", "CombatPower", 95), ("Ready", "Ready", 95), ("Role", "Role", 85) })
            table.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(field), Width = width });
        void Refresh() {
            var group = read();
            table.SetRows((group?.Members ?? []).Select(m => new { m.Name, Server = m.Server == 0 ? "Unknown" : m.Server.ToString(), m.Class, m.Level, m.GearScore, m.CombatPower,
                Ready = m.Ready ? "Ready" : "Not ready", Role = m.Id == group!.Leader ? "Leader" : "Member" }).ToArray());
            status.Text = group == null ? global::AionDpsMeter.Local.Localization.T("No active recruitment group captured.") : global::AionDpsMeter.Local.Localization.F($"{group.Members.Count(m => m.Ready)} / {group.Members.Length} members ready");
        }
        Refresh(); layout.Children.Add(table); window.Content = layout;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; timer.Tick += (_, _) => Refresh(); window.Closed += (_, _) => timer.Stop(); window.Show(); timer.Start();
    }
}
