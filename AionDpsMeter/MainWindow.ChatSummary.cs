using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowChatSummary()
    {
        var fight = Current;
        var window = new Window { Title = global::AionDpsMeter.Local.Localization.T("Copy fight summary"), Owner = this, Width = 700, Height = 480,
            MinWidth = 520, MinHeight = 340, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var layout = new DockPanel { Margin = new(18) };
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        heading.Children.Add(UiSections.Heading("Copy fight summary", "Copy a line or the full summary, then paste it where you choose."));
        var controls = new WrapPanel(); heading.Children.Add(controls);
        var metric = new ComboBox { ItemsSource = new[] { "Damage", "Healing" }, SelectedIndex = 0, Width = 120, Margin = new(0, 0, 8, 8) };
        var scope = new ComboBox { ItemsSource = new[] { "Party", "Self", "Players", "All" }, SelectedIndex = 0, Width = 150, Margin = new(0, 0, 8, 8) };
        controls.Children.Add(UiSections.Field("Metric", metric, 130)); controls.Children.Add(UiSections.Field("Players", scope, 160));
        var lines = new ListBox { SelectionMode = SelectionMode.Single };
        var status = new TextBlock { Margin = new(0, 8, 0, 8), TextWrapping = TextWrapping.Wrap };
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        var buttons = new WrapPanel(); footer.Children.Add(buttons); footer.Children.Add(status);
        string[] text = [];
        void RefreshSummary() {
            text = FightChatSummary.Build(fight, metric.SelectedIndex == 1, scope.SelectedItem?.ToString() ?? global::AionDpsMeter.Local.Localization.T("Party"));
            lines.ItemsSource = text; lines.SelectedIndex = 0; status.Text = global::AionDpsMeter.Local.Localization.T("Snapshot of ") + fight.Label;
        }
        void Copy(string value) {
            try { Clipboard.SetText(value); status.Text = global::AionDpsMeter.Local.Localization.T("Copied to clipboard."); }
            catch (ExternalException) { status.Text = global::AionDpsMeter.Local.Localization.T("Clipboard is busy. Please try again."); }
        }
        buttons.Children.Add(Button(global::AionDpsMeter.Local.Localization.T("Copy selected line"), () => { if (lines.SelectedItem is string line) Copy(line); }));
        buttons.Children.Add(Button(global::AionDpsMeter.Local.Localization.T("Copy all"), () => Copy(string.Join(Environment.NewLine, text))));
        metric.SelectionChanged += (_, _) => RefreshSummary(); scope.SelectionChanged += (_, _) => RefreshSummary();
        layout.Children.Add(lines); window.Content = layout; RefreshSummary(); window.Show();
    }
}
