using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowSelectedOpponent()
    {
        if (grids["PvP"].SelectedItem is not OpponentRow opponent) {
            notice = global::AionDpsMeter.Local.Localization.T("Select a PvP opponent to inspect their exchange."); Refresh(); return;
        }
        var fight = Current;
        var window = new Window { Title = global::AionDpsMeter.Local.Localization.T("PvP exchange · ") + opponent.Opponent, Owner = this,
            Width = 980, Height = 710, MinWidth = 680, MinHeight = 550, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var layout = new DockPanel { Margin = new(18) };
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); layout.Children.Add(top);
        top.Children.Add(UiSections.Heading(opponent.Opponent, "Damage exchanged with this opponent.",
            "Both timelines share the exchange duration. Each chart uses its own peak for the vertical scale."));
        var party = new CheckBox { Content = global::AionDpsMeter.Local.Localization.T("Include my captured party"), IsChecked = true, Margin = new(0, 0, 0, 10) }; top.Children.Add(party);
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) }; top.Children.Add(summary);
        var to = new EncounterTimelinePlot { Height = 90, Margin = new(0, 5, 0, 12) };
        var from = new EncounterTimelinePlot { Height = 90, Margin = new(0, 5, 0, 12) };
        var charts = new Grid { Margin = new(0, 0, 0, 12) }; charts.ColumnDefinitions.Add(new()); charts.ColumnDefinitions.Add(new());
        var outgoing = UiSections.TableSection("Damage dealt", to); outgoing.Margin = new(0, 0, 6, 0);
        var incoming = UiSections.TableSection("Damage received", from); incoming.Margin = new(6, 0, 0, 0);
        charts.Children.Add(outgoing); Grid.SetColumn(incoming, 1); charts.Children.Add(incoming); top.Children.Add(charts);
        var table = new LiveTable { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
            EnableRowVirtualization = true, EnableColumnVirtualization = true, HeadersVisibility = DataGridHeadersVisibility.Column, RowHeight = 36 };
        void Column(string title, string path, double width, string? format = null) => table.Columns.Add(new DataGridTextColumn {
            Header = title, Binding = new Binding(path) { StringFormat = format }, Width = width });
        Column("Direction", "Direction", 125); Column("Character", "Character", 135); Column("Skill", "Skill", 220);
        Column("Hits", "Hits", 70, "N0"); Column("Damage", "Damage", 110, "N0"); Column("DPS", "DPS", 100, "N1"); Column("Crit %", "CritPercent", 85, "N1");
        void Update() {
            var data = PvpOpponentAnalysis.Build(fight, opponent.Id, party.IsChecked == true, skills.GetSkillName);
            to.Update(data.ToOpponent, data.ToOpponent.Damage.Select(h => h.ActorId));
            from.Update(data.FromOpponent, new[] { opponent.Id }); table.SetRows(data.Skills);
            summary.Text = data.Skills.Length == 0 ? global::AionDpsMeter.Local.Localization.T("No direct exchange with your character was captured.") :
                global::AionDpsMeter.Local.Localization.F($"To opponent: {data.DamageToOpponent:N0} · {data.DamageToOpponent / data.ToOpponent.Seconds:N1} DPS     From opponent: {data.DamageFromOpponent:N0} · {data.DamageFromOpponent / data.FromOpponent.Seconds:N1} DPS     {MeterPresentation.Duration(data.ToOpponent.Seconds)}");
        }
        party.Checked += (_, _) => Update(); party.Unchecked += (_, _) => Update();
        layout.Children.Add(table); window.Content = layout; Update(); window.Show();
    }
}
