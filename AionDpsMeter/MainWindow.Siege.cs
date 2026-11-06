using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowSiege()
    {
        SiegeLayer[] layers; SiegeArtifact[] artifacts; ArtifactCaptorship[] captors;
        lock (engine.Gate) { layers = engine.Siege.Layers(); artifacts = engine.Siege.Artifacts(); captors = engine.Siege.Captors(); }
        ShowSiege(layers, artifacts, captors);
    }
    void ShowSiege(SiegeLayer[] layers, SiegeArtifact[] artifacts, ArtifactCaptorship[]? captors = null)
    {
        string Time(long? at) => at is >= 0 and <= 253402300799999 ? DateTimeOffset.FromUnixTimeMilliseconds(at.Value).ToString("dd MMM HH:mm") : "";
        string Owner(string? owner) => owner switch { "light" => "Elyos", "dark" => "Asmodians", _ => "Neutral / unknown" };
        var links = (captors ?? []).ToDictionary(c => c.Artifact);
        string OwnerLabel(SiegeArtifact artifact) => Owner(artifact.Owner) + (links.TryGetValue(artifact.Artifact, out var captor) ? " / " + captor.Name + (captor.Server is { } server ? " (" + server.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")" : "") : "");
        var rows = layers.Select(l => new { Kind = "Event", Id = l.Event, l.State, Owner = "", End = Time(l.EndsAt), Ended = Time(l.EndedAt), Next = Time(l.NextAt) })
            .Concat(artifacts.Select(a => new { Kind = "Artifact", Id = a.Artifact, State = "", Owner = OwnerLabel(a), End = "", Ended = "", Next = "" })).ToArray();
        var window = new Window { Title = global::AionDpsMeter.Local.Localization.T("Abyss siege"), Owner = this, Width = 1220, Height = 560, MinWidth = 1180, MinHeight = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences); var layout = new DockPanel { Margin = new(18) };
        var heading = UiSections.Heading("Abyss siege", "Last captured event status and ownership", "Event and artifact IDs come from the game. Empty updates retain earlier readings; unknown ownership never implies a faction. Times are the captured server UTC timestamps. Status is last observed, not inferred from the clock. Captor names are linked by a same-faction server announcement within five seconds, not proof of a local kill. Reopen to refresh.");
        DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        if (rows.Length == 0) { var empty = new TextBlock { Text = global::AionDpsMeter.Local.Localization.T("No siege information captured."), Margin = new(0,0,0,12) }; DockPanel.SetDock(empty,Dock.Top); layout.Children.Add(empty); }
        var table = new LiveTable { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, EnableRowVirtualization = true, RowHeight = 36 };
        foreach (var (label, field, width) in new[] { ("Type","Kind",90),("ID","Id",85),("State","State",105),("Owner / linked captor","Owner",300),("Ends (UTC)","End",150),("Ended (UTC)","Ended",150),("Next (UTC)","Next",150) })
            table.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(field), Width = width });
        table.SetRows(rows); layout.Children.Add(table); window.Content = layout; window.Show();
    }
}
