using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowEquippedGear()
    {
        var fight = Current;
        var rows = fight.Actors.Values.Where(a => a.Gear != null).SelectMany(a => a.Gear!.Items.Select(i => new EquippedGearRow(
            a.Name, ItemNames.Default.Find(i.ItemId), i.Details.Slot, i.Details.Enchant, i.Details.Surpass, i.Details.ItemLevel,
            a.Gear.Complete ? "Full inventory" : "Partial capture", i))).ToArray();
        var window = new Window { Title = global::AionDpsMeter.Local.Localization.T("Equipped gear"), Owner = this, Width = 1100, Height = 760, MinWidth = 800, MinHeight = 600,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var layout = new DockPanel { Margin = new(18) }; var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        heading.Children.Add(UiSections.Heading("Equipped gear", fight.Label,
            "Captured equipped inventory items, separate from appearance skins. Select an item to inspect its raw stats, skills and socketed stones. Combined stats include soul stats once; main stats remain separate. Values are not converted into derived character bonuses."));
        heading.Children.Add(new TextBlock { Text = rows.Length == 0 ? global::AionDpsMeter.Local.Localization.T("No equipped items captured. Enter the game while capture is running.")
            : global::AionDpsMeter.Local.Localization.F($"{rows.Length:N0} equipped items captured"), Margin = new(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap });
        LiveTable Table() => new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, EnableRowVirtualization = true, RowHeight = 36 };
        var items = Table(); var details = Table();
        void Column(LiveTable table, string title, string property, int width) => table.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = width });
        Column(items, "Item", "Item", 250); Column(items, "Slot", "Slot", 60); Column(items, "Enchant", "Enchant", 80);
        Column(items, "Surpass", "Surpass", 80); Column(items, "Item level", "ItemLevel", 95); Column(items, "Capture", "Coverage", 130); Column(items, "Character", "Character", 160);
        Column(details, "Type", "Type", 170); Column(details, "ID / item", "Label", 360); Column(details, "Value / level", "Value", 150);
        var panels = new Grid(); panels.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star), MinHeight = 150 }); panels.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star), MinHeight = 150 });
        panels.Children.Add(items); details.Margin = new(0, 12, 0, 0); Grid.SetRow(details, 1); panels.Children.Add(details);
        items.SelectionChanged += (_, _) => {
            var values = new List<GearDetailRow>();
            if (items.SelectedItem is EquippedGearRow row) {
                var d = row.Source.Details;
                values.AddRange(d.MainStats.Select(s => new GearDetailRow("Main stat", s.Stat.ToString(), s.Value.ToString())));
                values.AddRange(d.Stats.Select(s => new GearDetailRow("Combined stat", s.Stat.ToString(), s.Value.ToString())));
                values.AddRange(d.Skills.Select(s => new GearDetailRow("Skill", s.Skill.ToString(), s.Level.ToString())));
                values.AddRange(d.Manastones.Select(s => new GearDetailRow("Manastone", $"{ItemNames.Default.Find(s.Item)} / stat {s.Stat}", $"Grade {s.Grade}")));
                values.AddRange(d.Godstones.Select(id => new GearDetailRow("Godstone", ItemNames.Default.Find(id), "")));
            }
            details.SetRows(values);
        };
        items.SetRows(rows); if (rows.Length > 0) items.SelectedIndex = 0;
        layout.Children.Add(panels); window.Content = layout; window.Show();
    }
    sealed record EquippedGearRow(string Character, string Item, byte Slot, byte Enchant, byte Surpass, int ItemLevel, string Coverage, EquippedItem Source);
    sealed record GearDetailRow(string Type, string Label, string Value);
}
