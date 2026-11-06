using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowEffects()
    {
        var fight = Current;
        var rows = EffectAnalysis.Build(fight, skills.GetSkillName);
        var shieldRows = ShieldAnalysis.Build(fight, skills.GetSkillName);
        var window = new Window { Title = global::AionDpsMeter.Local.Localization.T("Buffs and effects"), Owner = this, Width = 1040, Height = 620, MinWidth = 720, MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var layout = new DockPanel { Margin = new(18) };
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        heading.Children.Add(UiSections.Heading("Buffs and effects", fight.Label + " - " + MeterPresentation.Duration(fight),
            "Effect uptime covers this fight. Shields show observed capacity added and consumed; consumption is inferred from capacity decreases, not verified damage prevented. Removal does not count as consumption."));
        var search = new TextBox { Tag = "Search character, effect or skill...", Margin = new(0, 0, 0, 10), ToolTip = global::AionDpsMeter.Local.Localization.T("Search recipient, source, skill or effect ID") };
        heading.Children.Add(search);
        var status = new TextBlock { Margin = new(0, 0, 0, 8) }; heading.Children.Add(status);
        var table = new LiveTable { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
            EnableRowVirtualization = true, EnableColumnVirtualization = true, RowHeight = 36, HeadersVisibility = DataGridHeadersVisibility.Column };
        void Column(string title, string property, double width, string? format = null) => table.Columns.Add(new DataGridTextColumn {
            Header = title, Binding = new Binding(property) { StringFormat = format }, Width = width });
        Column("Recipient", "Recipient", 150); Column("Source", "Source", 150); Column("Effect", "EffectName", 190); Column("Type", "EffectKind", 90); Column("Effect ID", "EffectId", 100);
        Column("Source skill", "SourceSkill", 190); Column("Tracked seconds", "TrackedSeconds", 125, "N1");
        Column("Tracked uptime %", "TrackedUptime", 140, "N1"); Column("Segments", "TrackedSegments", 85);
        Column("Unknown duration", "HasUnknownDuration", 145);
        var shieldTable = new LiveTable { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false,
            CanUserDeleteRows = false, RowHeight = 36, EnableRowVirtualization = true };
        foreach (var c in new[] { ("Recipient", "Recipient", 130d), ("Added", "Added", 110d), ("Consumed", "Consumed", 120d),
            ("Source", "Source", 130d), ("Skill", "Skill", 180d), ("Updates", "Updates", 75d) })
            shieldTable.Columns.Add(new DataGridTextColumn { Header = c.Item1, Binding = new Binding(c.Item2), Width = c.Item3 });
        var tabs = new TabControl(); tabs.Items.Add(new TabItem { Header = "Effects", Content = table });
        tabs.Items.Add(new TabItem { Header = "Shields", Content = shieldTable });
        void Filter() {
            string text = search.Text.Trim();
            var filtered = rows.Where(r => r.Recipient.Contains(text, StringComparison.OrdinalIgnoreCase) || r.Source.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                r.SourceSkill.Contains(text, StringComparison.OrdinalIgnoreCase) || r.EffectName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                r.EffectKind.Contains(text, StringComparison.OrdinalIgnoreCase) || r.EffectId.ToString().Contains(text)).ToArray();
            table.SetRows(filtered); status.Text = rows.Length == 0 ? global::AionDpsMeter.Local.Localization.T("No effect intervals were captured within this fight.") : global::AionDpsMeter.Local.Localization.F($"{filtered.Length:N0} effect/source combinations · snapshot");
            shieldTable.SetRows(shieldRows.Where(r => r.Recipient.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                r.Source.Contains(text, StringComparison.OrdinalIgnoreCase) || r.Skill.Contains(text, StringComparison.OrdinalIgnoreCase)).ToArray());
            if (tabs.SelectedIndex == 1) status.Text = shieldRows.Length == 0 ? global::AionDpsMeter.Local.Localization.T("No shield capacity changes were captured within this fight.") : global::AionDpsMeter.Local.Localization.F($"{shieldRows.Length:N0} shield/source combinations");
        }
        tabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.Source, tabs)) Filter(); };
        search.TextChanged += (_, _) => Filter(); layout.Children.Add(tabs); window.Content = layout; Filter(); window.Show();
    }
}
