using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowPetGenus()
    {
        var savedFight = viewed;
        PetGenus[]? ReadGenera() {
            if (savedFight != null) return savedFight.Pets?.Genera;
            return engine.CurrentPets()?.Genera;
        }
        var genera = ReadGenera();
        GenusRow[] Rows() => (genera ?? []).SelectMany(g => g.Effects.Length == 0
            ? new[] { new GenusRow(GenusName(g.Type), g.Level, g.Panel, "", "", "", "", "", "") }
            : g.Effects.Select(e => new GenusRow(GenusName(g.Type), g.Level, g.Panel, e.Slot.ToString(), e.Grade.ToString(), $"{StatNames.Default.Find(e.Stat)} ({e.Stat})", StatNames.Default.Value(e.Stat, e.Value), e.Locked ? "Yes" : "No", e.Activated ? "Yes" : "No", $"Raw value: {e.Value}"))).ToArray();
        var window = new Window { Title = "Pet genus bonuses", Owner = this, Width = 1000, Height = 580, MinWidth = 780, MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        ClosedWindowAutomation.Attach(window);
        var layout = new DockPanel { Margin = new(18) }; var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        heading.Children.Add(UiSections.Heading("Pet genus bonuses", savedFight == null ? "Live collection" : "Saved fight: " + savedFight.Label,
            "Last captured genus levels and selected-panel slots from the pet roster. Named stats use captured catalogue units. Hover a value to inspect its raw packet value. Bonus totals are not combined. Activation and lock flags are separate observations."));
        string Status() => genera == null ? "No pet genus snapshot captured. Enter the game while capture is running."
            : $"{genera.Length:N0} genera captured; selected panels only";
        var status = new TextBlock { Text = Status(), TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) };
        heading.Children.Add(status);
        var table = new LiveTable { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, EnableRowVirtualization = true, RowHeight = 36 };
        var headers = new Style(typeof(DataGridColumnHeader), window.TryFindResource(typeof(DataGridColumnHeader)) as Style);
        headers.Setters.Add(new Setter(PaddingProperty, new Thickness(6, 8, 6, 8)));
        table.ColumnHeaderStyle = headers;
        void Column(string title, string property, int width) {
            var cells = new Style(typeof(DataGridCell), window.TryFindResource(typeof(DataGridCell)) as Style);
            cells.Setters.Add(new Setter(ToolTipProperty, new Binding(property == "Value" ? "RawValue" : property)));
            table.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = width, CellStyle = cells });
        }
        Column("Genus", "Genus", 80); Column("Level", "Level", 60); Column("Panel", "Panel", 72); Column("Slot", "Slot", 55); Column("Grade", "Grade", 72);
        Column("Stat", "Stat", 150); Column("Value", "Value", 80); Column("Locked", "Locked", 75); Column("Activated", "Activated", 85);
        table.SetRows(Rows()); layout.Children.Add(table); window.Content = layout;
        var refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        void Tick(object? sender, EventArgs args) {
            var next = ReadGenera();
            if (ReferenceEquals(next, genera)) return;
            genera = next; table.SetRows(Rows()); status.Text = Status();
        }
        refresh.Tick += Tick;
        window.Closed += (_, _) => { refresh.Stop(); refresh.Tick -= Tick; };
        if (savedFight == null) refresh.Start();
        window.Show();
    }
    static string GenusName(byte type) => type switch { 2 => "Intellect", 3 => "Feral", 4 => "Nature", 5 => "Trans", 6 => "Special", _ => $"Type {type}" };
    sealed record GenusRow(string Genus, int Level, byte Panel, string Slot, string Grade, string Stat, string Value, string Locked, string Activated, string RawValue = "");
}
