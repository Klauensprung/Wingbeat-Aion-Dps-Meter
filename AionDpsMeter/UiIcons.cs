using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AionDpsMeter.Local;

internal enum UiIcon { Play, Stop, NewFight, Meter, Cooldowns, More, Settings, Power, Capture, Palette, Folder, Refresh, Save, Close, Chart,
    Encounter, Party, Skills, Healing, Pvp, Rotation, Player, Opener, Boss, Catalogue, Report, Trophy, Cohort, Network }

// Small original vector icons: no font dependency or external image downloads.
internal static class UiIcons
{
    static string GeometryData(UiIcon icon) => icon switch {
            UiIcon.Play => "M6,3 L16,10 6,17 Z",
            UiIcon.Stop => "M5,5 L15,5 15,15 5,15 Z",
            UiIcon.NewFight => "M4,6 A7,7 0 1 1 3,13 M4,2 L4,6 8,6 M10,7 L10,13 M7,10 L13,10",
            UiIcon.Meter => "M3,16 L3,11 6,11 6,16 Z M8.5,16 L8.5,7 11.5,7 11.5,16 Z M14,16 L14,3 17,3 17,16 Z",
            UiIcon.Cooldowns => "M10,5 A6,6 0 1 1 9.99,5 M8,2 L12,2 M10,2 L10,5 M10,8 L10,11 13,11 M15,5 L17,3",
            UiIcon.More => "M3,10 A1,1 0 1 1 3,10.01 M10,10 A1,1 0 1 1 10,10.01 M17,10 A1,1 0 1 1 17,10.01",
            UiIcon.Settings => "M3,5 L8,5 M12,5 L17,5 M3,10 L12,10 M16,10 L17,10 M3,15 L4,15 M8,15 L17,15 M10,3 A2,2 0 1 1 9.99,3 M14,8 A2,2 0 1 1 13.99,8 M6,13 A2,2 0 1 1 5.99,13",
            UiIcon.Power => "M10,2 L10,9 M6,4 A7,7 0 1 0 14,4",
            UiIcon.Capture => "M3,3 L17,3 17,12 3,12 Z M10,12 L10,17 M5,17 L15,17 M6,7 L8,7 9,5 11,10 12,7 14,7",
            UiIcon.Palette => "M10,3 C1,3 1,17 9,17 C12,17 8,13 12,13 L14,13 C20,13 19,3 10,3 Z M6,6 L6,6.1 M11,5 L11,5.1 M15,8 L15,8.1 M5,11 L5,11.1",
            UiIcon.Folder => "M2,6 L2,4 8,4 10,6 18,6 17,16 3,16 Z M2,8 L17,8",
            UiIcon.Refresh => "M4,7 A6.5,6.5 0 1 1 4,14 M4,3 L4,7 8,7",
            UiIcon.Save => "M3,3 L14,3 17,6 17,17 3,17 Z M6,3 L6,8 13,8 13,3 M6,17 L6,12 14,12 14,17",
            UiIcon.Close => "M5,5 L15,15 M15,5 L5,15",
            UiIcon.Encounter => "M10,4 A6,6 0 1 1 9.99,4 M10,1 L10,5 M10,15 L10,19 M1,10 L5,10 M15,10 L19,10 M10,8 A2,2 0 1 1 9.99,8",
            UiIcon.Party => "M10,3 A2.5,2.5 0 1 1 9.99,3 M5,17 L5,14 C5,10 15,10 15,14 L15,17 M4,5 A2,2 0 1 0 4,9 M3,12 C1,12 1,15 1,16 M16,5 A2,2 0 1 1 16,9 M17,12 C19,12 19,15 19,16",
            UiIcon.Skills => "M11,2 L4,11 9,11 8,18 16,8 11,8 Z",
            UiIcon.Healing => "M7,2 L13,2 13,7 18,7 18,13 13,13 13,18 7,18 7,13 2,13 2,7 7,7 Z",
            UiIcon.Pvp => "M3,2 L7,3 16,12 12,16 3,7 Z M12,12 L18,18 M11,17 L17,11 M17,2 L13,3 10,6 M14,10 L17,7 Z M8,12 L2,18 M3,11 L9,17",
            UiIcon.Rotation => "M3,4 L17,4 M3,10 L17,10 M3,16 L17,16 M6,2 L6,6 M13,8 L13,12 M9,14 L9,18",
            UiIcon.Player => "M3,3 L17,3 17,17 3,17 Z M8,6 A2,2 0 1 1 7.99,6 M5,14 C5,10 11,10 11,14 M13,7 L15,7 M13,10 L15,10 M13,13 L15,13",
            UiIcon.Opener => "M4,18 L4,3 M4,3 C8,0 12,6 17,3 L17,11 C12,14 8,8 4,11",
            UiIcon.Boss => "M3,3 L6,6 10,4 14,6 17,3 16,12 13,16 7,16 4,12 Z M6,9 L8,10 M14,9 L12,10 M8,13 L12,13 M8,16 L8,18 M12,16 L12,18",
            UiIcon.Catalogue => "M10,5 C7,3 4,3 2,4 L2,17 C5,16 7,16 10,18 C13,16 15,16 18,17 L18,4 C15,3 13,3 10,5 Z M10,5 L10,18 M5,7 L7,7 M5,10 L7,10 M13,7 L15,7 M13,10 L15,10",
            UiIcon.Report => "M7,4 L4,4 4,18 16,18 16,4 13,4 M7,2 L13,2 13,6 7,6 Z M7,10 L13,10 M7,14 L11,14",
            UiIcon.Trophy => "M6,3 L14,3 14,8 C14,14 6,14 6,8 Z M6,5 L2,5 2,8 C2,11 4,11 6,11 M14,5 L18,5 18,8 C18,11 16,11 14,11 M10,13 L10,17 M6,18 L14,18",
            UiIcon.Cohort => "M2,18 L2,12 7,12 7,8 13,8 13,14 18,14 18,18 Z M7,12 L7,18 M13,14 L13,18 M10,1 L10.8,3.3 13,3.3 11.2,4.7 12,7 10,5.6 8,7 8.8,4.7 7,3.3 9.2,3.3 Z",
            UiIcon.Network => "M7,2 L13,2 13,7 7,7 Z M2,13 L7,13 7,18 2,18 Z M13,13 L18,13 18,18 13,18 Z M10,7 L10,10 M4.5,13 L4.5,10 15.5,10 15.5,13",
            _ => "M3,3 L3,17 17,17 M6,13 L10,8 13,11 17,5"
        };
    internal static FrameworkElement Create(UiIcon icon, double size = 18)
    {
        var path = new Path { Data = Geometry.Parse(GeometryData(icon)), StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        path.SetBinding(Shape.StrokeProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Control), 1) });
        var canvas = new Canvas { Width = 20, Height = 20 }; canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false, Tag = icon };
    }
    internal static UiIcon ForView(string title) => title switch {
        "Encounter" => UiIcon.Encounter, "Cooldowns" => UiIcon.Cooldowns, "Party" => UiIcon.Party, "Skills" => UiIcon.Skills,
        "Healing" => UiIcon.Healing, "PvP" => UiIcon.Pvp, "Rotations" => UiIcon.Rotation, "Player stats" => UiIcon.Player,
        "Saved fights" => UiIcon.Folder, "Trends" => UiIcon.Chart, "Openers" => UiIcon.Opener, "Boss analysis" => UiIcon.Boss,
        "Boss catalogue" => UiIcon.Catalogue, "Report card" => UiIcon.Report, "Top 5%" => UiIcon.Trophy,
        "Top cohort" => UiIcon.Cohort, "Network events" => UiIcon.Network, _ => UiIcon.Chart
    };
    internal static DataTemplate NavigationHeader(string title)
    {
        // Keep Header as a string: navigation, filtering and screen readers use
        // its name. The template only adds the themed vector beside that label.
        var row = new FrameworkElementFactory(typeof(DockPanel));
        var icon = new FrameworkElementFactory(typeof(Viewbox));
        icon.SetValue(FrameworkElement.WidthProperty, 16d); icon.SetValue(FrameworkElement.HeightProperty, 16d);
        icon.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0)); icon.SetValue(DockPanel.DockProperty, Dock.Left);
        icon.SetValue(UIElement.IsHitTestVisibleProperty, false);
        var canvas = new FrameworkElementFactory(typeof(Canvas)); canvas.SetValue(FrameworkElement.WidthProperty, 20d); canvas.SetValue(FrameworkElement.HeightProperty, 20d);
        var path = new FrameworkElementFactory(typeof(Path)); path.SetValue(Path.DataProperty, Geometry.Parse(GeometryData(ForView(title))));
        path.SetValue(Shape.StrokeThicknessProperty, 1.6); path.SetValue(Shape.StrokeStartLineCapProperty, PenLineCap.Round);
        path.SetValue(Shape.StrokeEndLineCapProperty, PenLineCap.Round); path.SetValue(Shape.StrokeLineJoinProperty, PenLineJoin.Round);
        path.SetBinding(Shape.StrokeProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(TabItem), 1) });
        canvas.AppendChild(path); icon.AppendChild(canvas); row.AppendChild(icon);
        var label = new FrameworkElementFactory(typeof(TextBlock)); label.SetBinding(TextBlock.TextProperty, new Binding());
        label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        row.AppendChild(label); return new DataTemplate { VisualTree = row };
    }
    internal static FrameworkElement Label(UiIcon icon, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Create(icon));
        row.Children.Add(new TextBlock { Text = text, Margin = new(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return row;
    }
    internal static void SetLabel(Button button, UiIcon icon, string label, string? tip = null)
    {
        button.Content = Label(icon, label); button.ToolTip = tip ?? label;
        AutomationProperties.SetName(button, label);
    }
}
