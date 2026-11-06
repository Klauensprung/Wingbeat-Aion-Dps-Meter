using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    sealed record AppearancePresetRow(string Slot, string Label, int Bytes, string Data);
    void ShowAppearancePresets() => ShowAppearancePresets(() => { lock (engine.Gate) return engine.AppearancePresets.Snapshot(); });
    void ShowAppearancePresets(Func<CharacterPresets?> read)
    {
        var window = new Window { Title = global::AionDpsMeter.Local.Localization.T("Appearance presets"), Owner = this, Width = 820, Height = 510, MinWidth = 650, MinHeight = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var layout = new DockPanel { Margin = new(18) };
        var heading = UiSections.Heading("Appearance presets", "Captured character presets", "Open your in-game appearance presets while capture is running. Export saves a local Wingbeat JSON backup; it is not a game-import file. Select a preset to inspect its captured colors and settings locally.");
        DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        var status = new TextBlock { Margin = new(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap }; DockPanel.SetDock(status, Dock.Top); layout.Children.Add(status);
        var export = new Button { Content = global::AionDpsMeter.Local.Localization.T("Export presets"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 12), Padding = new(12, 6, 12, 6) };
        var inspect = new Button { Content = global::AionDpsMeter.Local.Localization.T("View colors and settings"), IsEnabled = false, Margin = new(0, 0, 10, 12), Padding = new(12, 6, 12, 6) };
        var actions = new WrapPanel(); actions.Children.Add(inspect); actions.Children.Add(export);
        DockPanel.SetDock(actions, Dock.Top); layout.Children.Add(actions);
        var table = new LiveTable { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, EnableRowVirtualization = true, RowHeight = 36 };
        table.Columns.Add(new DataGridTextColumn { Header = "Slot", Binding = new Binding("Slot"), Width = 75 });
        table.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding("Label"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 220 });
        table.Columns.Add(new DataGridTextColumn { Header = "Data bytes", Binding = new Binding("Bytes"), Width = 110 });
        CharacterPresets? current = null;
        void Refresh() {
            current = read(); export.IsEnabled = current != null;
            string? selectedSlot = (table.SelectedItem as AppearancePresetRow)?.Slot;
            var rows = current?.Presets.Select(p => new AppearancePresetRow((p.Slot + 1).ToString(), p.Label, p.Bytes, p.Data)).ToList() ?? [];
            if (current?.Current?.Data is string data) rows.Insert(0, new("Current", "Current appearance", new AppearancePreset(0, null, data).Bytes, data));
            table.SetRows(rows);
            table.SelectedItem = rows.FirstOrDefault(r => r.Slot == selectedSlot) ?? rows.FirstOrDefault();
            status.Text = current == null ? global::AionDpsMeter.Local.Localization.T("No presets captured for the current character.") : global::AionDpsMeter.Local.Localization.F($"{current.Name} / Server {current.Server} / {(current.PresetsCaptured ? current.Presets.Length + Localization.T(" presets") : Localization.T("Preset list not captured"))} / Captured {DateTimeOffset.FromUnixTimeMilliseconds(current.Timestamp).ToLocalTime():dd MMM HH:mm}");
            if (current?.Current is { } appearance) status.Text += global::AionDpsMeter.Local.Localization.F($"\n{appearance.Race ?? "Race unknown"} / {appearance.Gender ?? "Gender unknown"} / Wing {appearance.WingSkinId switch { > 0 => appearance.WingSkinId, _ => appearance.WingId }}");
            if (current?.Equipment is { } equipment) status.Text += global::AionDpsMeter.Local.Localization.F($" / {equipment.Length} shown items ({(current.EquipmentComplete ? Localization.T("complete") : Localization.T("partial"))} capture)");
        }
        table.SelectionChanged += (_, _) => inspect.IsEnabled = table.SelectedItem is AppearancePresetRow { Data.Length: > 0 };
        inspect.Click += (_, _) => { if (table.SelectedItem is AppearancePresetRow row) ShowAppearanceSettings(row.Label, row.Data, window, current?.Current?.Gender); };
        export.Click += (_, _) => {
            var snapshot = current; if (snapshot == null) return;
            var dialog = new SaveFileDialog { Title = global::AionDpsMeter.Local.Localization.T("Export Wingbeat appearance presets"), Filter = "Wingbeat JSON backup (*.json)|*.json", FileName = "appearance-presets.json" };
            if (dialog.ShowDialog(window) != true) return;
            try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(new { Format = "Wingbeat.AppearancePresets", Version = 1, Character = snapshot }, new JsonSerializerOptions { WriteIndented = true })); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { MessageBox.Show(window, "Could not export presets: " + ex.Message, global::AionDpsMeter.Local.Localization.T("Appearance presets")); }
        };
        Refresh(); layout.Children.Add(table); window.Content = layout;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; timer.Tick += (_, _) => Refresh();
        window.Closed += (_, _) => timer.Stop(); window.Show(); timer.Start();
    }
}
