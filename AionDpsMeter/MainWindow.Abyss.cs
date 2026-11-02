using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowAbyss()
    {
        AbyssSeason? season; long timestamp; AbyssServer[] servers;
        lock (engine.Gate) { season = engine.Abyss.Season; timestamp = engine.Abyss.SeasonAt; servers = engine.Abyss.Servers(); }
        ShowAbyss(season, timestamp, servers);
    }
    void ShowAbyss(AbyssSeason? season,long timestamp,AbyssServer[] servers)
    {
        var window=new Window {Title="Abyss season",Owner=this,Width=880,Height=520,MinWidth=800,MinHeight=500,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        UiTheme.Apply(window,preferences);var layout=new DockPanel{Margin=new(18)};
        var heading=UiSections.Heading("Abyss season","Last captured season and matchup","Season points and statistics come from the game's ranking reply and are separate from this meter's combat records. Missing statistics remain unknown. Grade and server numbers are captured IDs. Reopen to refresh after the game sends new information.");
        DockPanel.SetDock(heading,Dock.Top);layout.Children.Add(heading);
        var summary=new TextBlock {Text=season==null?"No Abyss season reading captured.":$"{season.Name} · captured {DateTimeOffset.FromUnixTimeMilliseconds(timestamp).ToLocalTime():dd MMM HH:mm}",Margin=new(0,0,0,12)};
        DockPanel.SetDock(summary,Dock.Top);layout.Children.Add(summary);
        var seasonTable=new LiveTable {IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,RowHeight=36,Height=100};
        foreach(var(label,field,width) in new[]{("Grade","Grade",90),("Points","Points",150),("Kills","Kills",100),("Deaths","Deaths",100),("Assists","Assists",100),("Rank change","RankChange",130)})
            seasonTable.Columns.Add(new DataGridTextColumn{Header=label,Binding=new Binding(field),Width=width});
        seasonTable.SetRows(season==null?[]:new[]{new{season.Grade,season.Points,Kills=season.Kills?.ToString("N0")??"Unknown",Deaths=season.Deaths?.ToString("N0")??"Unknown",Assists=season.Assists?.ToString("N0")??"Unknown",season.RankChange}});
        DockPanel.SetDock(seasonTable,Dock.Top);layout.Children.Add(seasonTable);
        var matchupLabel=new TextBlock {Text=servers.Length==0?"No Abyss server matchup captured.":"Captured Abyss server matchup",Margin=new(0,14,0,10)};DockPanel.SetDock(matchupLabel,Dock.Top);layout.Children.Add(matchupLabel);
        var table=new LiveTable {IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,RowHeight=36};
        table.Columns.Add(new DataGridTextColumn{Header="Faction",Binding=new Binding("Faction"),Width=200});table.Columns.Add(new DataGridTextColumn{Header="Server",Binding=new Binding("Server"),Width=200});
        table.SetRows(servers.Select(s=>new{s.Server,Faction=s.Race=="light"?"Elyos":"Asmodians"}).ToArray());layout.Children.Add(table);window.Content=layout;window.Show();
    }
}
