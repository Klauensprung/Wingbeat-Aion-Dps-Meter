using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowScoreboard() => ShowScoreboard(() => { lock (engine.Gate) return engine.Scoreboard.Rows(engine.Fight.Actors.Values); });
    void ShowScoreboard(Func<BattleScoreRow[]> read)
    {
        var window = new Window { Title = "Battle scoreboard", Owner = this, Width = 1000, Height = 540, MinWidth = 940, MinHeight = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences); var layout = new DockPanel { Margin = new(18) };
        var heading = UiSections.Heading("Battle scoreboard", "Captured game scores for the current map", "Game-reported kills, deaths and assists update independently of the DPS meter. Player names require a matching captured character identity. Unknown players keep their full character ID. Map changes clear scores; resetting the fight does not.");
        DockPanel.SetDock(heading,Dock.Top); layout.Children.Add(heading);
        var status = new TextBlock { Margin = new(0,0,0,12) }; DockPanel.SetDock(status,Dock.Top); layout.Children.Add(status);
        var table = new LiveTable { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, EnableRowVirtualization = true, RowHeight = 36 };
        table.Columns.Add(new DataGridTextColumn { Header = "Player", Binding = new Binding("Name"), Width = new DataGridLength(1,DataGridLengthUnitType.Star), MinWidth = 150 });
        foreach (var (label,field,width) in new[] { ("Server","Server",90),("Class","Class",120),("Kills","Kills",70),("Deaths","Deaths",80),("Assists","Assists",80),("Character ID","CharacterId",220) })
            table.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(field), Width = width });
        void Refresh() { var rows = read(); table.SetRows(rows); status.Text = rows.Length == 0 ? "No battle scores captured for this map." : $"{rows.Length:N0} players · ranked by kills, assists, then fewest deaths"; }
        Refresh(); layout.Children.Add(table); window.Content = layout;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; timer.Tick += (_,_) => Refresh(); window.Closed += (_,_) => timer.Stop(); window.Show(); timer.Start();
    }
}
