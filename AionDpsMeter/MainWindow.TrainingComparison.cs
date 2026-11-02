using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    sealed record TrainingChoice(Fight Summary)
    {
        public string Label => $"{Summary.Created.ToLocalTime():g} · {Summary.Label} · {Summary.Training?.Group} · {Summary.Seconds:0.#}s";
    }
    void ShowTrainingComparison()
    {
        FinishAutosave(wait: false);
        var choices = archive.Where(f => f.Training != null).OrderByDescending(f => f.Created).Select(f => new TrainingChoice(f)).ToArray();
        var window = new Window { Title = "Compare training attempts", Owner = this, Width = 1040, Height = 680,
            MinWidth = 760, MinHeight = 480, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var layout = new DockPanel { Margin = new(18) };
        var heading = new StackPanel();
        var filters = new ScrollViewer { Content = heading, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = window.Height * .55 };
        window.SizeChanged += (_, _) => filters.MaxHeight = Math.Max(170, window.ActualHeight * .55);
        DockPanel.SetDock(filters, Dock.Top); layout.Children.Add(filters);
        heading.Children.Add(UiSections.Heading("Training comparison", "Compare an attempt against your baseline. Rates include the full attempt duration."));
        var groupFilter = new TextBox { Tag = "Group or attempt name...", Margin = new(0, 4, 0, 10), ToolTip = "Filter attempts by group or name; leave blank to show all training attempts." };
        heading.Children.Add(new TextBlock { Text = "Find group or attempt" }); heading.Children.Add(groupFilter);
        var a = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Label", SelectedIndex = choices.Length > 1 ? 1 : -1, Margin = new(0, 4, 0, 10) };
        var b = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Label", SelectedIndex = choices.Length > 0 ? 0 : -1, Margin = new(0, 4, 0, 10) };
        var attempts = new Grid(); attempts.ColumnDefinitions.Add(new()); attempts.ColumnDefinitions.Add(new());
        var firstChoice = UiSections.Field("A - Baseline", a, 300); firstChoice.Width = double.NaN;
        var secondChoice = UiSections.Field("B - Comparison", b, 300); secondChoice.Width = double.NaN;
        attempts.Children.Add(firstChoice); Grid.SetColumn(secondChoice, 1); attempts.Children.Add(secondChoice); heading.Children.Add(attempts);
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
        var notes = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxHeight = 75, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(0, 0, 0, 10) };
        var table = new LiveTable { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false,
            CanUserDeleteRows = false, EnableRowVirtualization = true, EnableColumnVirtualization = true,
            HeadersVisibility = DataGridHeadersVisibility.Column, RowHeight = 36, ColumnWidth = 100 };
        void Column(string label, string property, string? format = null, double width = 100) => table.Columns.Add(new DataGridTextColumn {
            Header = label, Binding = new Binding(property) { StringFormat = format, TargetNullValue = "—" }, Width = width });
        Column("Skill", "Skill", width: 210); Column("A damage", "DamageA", "N0"); Column("B damage", "DamageB", "N0");
        Column("A DPS", "DpsA", "N1"); Column("B DPS", "DpsB", "N1"); Column("DPS change", "DpsChange", "+0.0;-0.0;0.0");
        Column("Change %", "ChangePercent", "+0.0;-0.0;0.0"); Column("A hits", "HitsA", "N0", 75); Column("B hits", "HitsB", "N0", 75);
        Column("A crit %", "CritA", "N1", 85); Column("B crit %", "CritB", "N1", 85);
        void Compare()
        {
            table.SetRows(Array.Empty<TrainingSkillDifference>()); notes.Text = "";
            if (a.SelectedItem is not TrainingChoice first || b.SelectedItem is not TrainingChoice second) {
                summary.Text = "Save two training attempts to compare them here."; return;
            }
            if (first.Summary.Id == second.Summary.Id) { summary.Text = "Select two different attempts."; return; }
            try {
                // Load only the selected pair. The view retains aggregates, not event histories.
                var result = TrainingComparison.Build(LoadArchiveDetails(first.Summary), LoadArchiveDetails(second.Summary), skills.GetSkillName);
                table.SetRows(result.Skills);
                summary.Text = $"A: {result.DamageA:N0} damage · {result.DpsA:N1} DPS · {result.SecondsA:0.#}s     B: {result.DamageB:N0} damage · {result.DpsB:N1} DPS · {result.SecondsB:0.#}s\nDPS change: {result.DpsB - result.DpsA:+0.0;-0.0;0.0}. A dash means no baseline damage for that skill.";
                if (result.CharacterA != result.CharacterB) summary.Text += "\nThese attempts belong to different character names.";
                notes.Text = $"A · {first.Summary.Training?.Group}: {first.Summary.Training?.Notes}\nB · {second.Summary.Training?.Group}: {second.Summary.Training?.Notes}";
                notes.ToolTip = notes.Text;
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException) {
                summary.Text = "Could not load these attempts: " + ex.Message;
            }
        }
        a.SelectionChanged += (_, _) => { table.SetRows(Array.Empty<TrainingSkillDifference>()); summary.Text = "Press Compare to load the selected attempts."; notes.Text = ""; };
        b.SelectionChanged += (_, _) => { table.SetRows(Array.Empty<TrainingSkillDifference>()); summary.Text = "Press Compare to load the selected attempts."; notes.Text = ""; };
        groupFilter.TextChanged += (_, _) => {
            string query = groupFilter.Text.Trim();
            var filtered = choices.Where(c => c.Summary.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Summary.Training!.Group.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            a.ItemsSource = filtered; b.ItemsSource = filtered;
            a.SelectedIndex = filtered.Length > 1 ? 1 : -1; b.SelectedIndex = filtered.Length > 0 ? 0 : -1;
        };
        var compare = Button("Compare attempts", Compare); compare.HorizontalAlignment = HorizontalAlignment.Left; heading.Children.Add(compare); heading.Children.Add(summary); heading.Children.Add(notes);
        layout.Children.Add(table); window.Content = layout; Compare(); window.Show();
    }
}
