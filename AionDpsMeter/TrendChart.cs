using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AionDpsMeter.Local;

internal sealed class TrendChart : Canvas
{
    TrendRow[] rows = [];
    bool healing;
    public event Action<string>? OpenFight;
    public TrendChart() { MinHeight = 120; MinWidth = 300; Margin = new(10); ClipToBounds = true; SizeChanged += (_, _) => Draw(); }
    public void Update(TrendRow[] value, bool showHealing) { if (rows.SequenceEqual(value) && healing == showHealing) return; rows = value; healing = showHealing; Draw(); }
    void Draw()
    {
        Children.Clear();
        var data = rows.TakeLast(100).ToArray();
        void Text(string text, double x, double y) { var label = new TextBlock { Text = text, Foreground = UiTheme.Brush("#AAB6C8"), FontSize = 11 }; SetLeft(label, x); SetTop(label, y); Children.Add(label); }
        if (data.Length == 0) { Text("Save or import fights, then select an encounter to see your DPS trend.", 12, 45); return; }
        double average = rows.Average(r => healing ? r.HPS : r.DPS);
        double width = Math.Max(200, ActualWidth - 90), height = Math.Max(40, ActualHeight - 62);
        double maximum = Math.Max(1, Math.Max(average, data.Max(r => healing ? r.HPS : r.DPS)) * 1.12);
        double averageY = 12 + height * (1 - average / maximum);
        var baseline = new Line { X1 = 55, X2 = 55 + width, Y1 = averageY, Y2 = averageY,
            Stroke = UiTheme.Brush("#E7CF7D"), StrokeThickness = 1.5, StrokeDashArray = new([5d, 4d]),
            ToolTip = $"Average of all {rows.Length:N0} attempts: {average:N1} {(healing ? "HPS" : "DPS")}\nEach attempt has equal weight." };
        Children.Add(baseline);
        Text("Average", 60, Math.Max(0, averageY - 17));
        var line = new Polyline { Stroke = UiTheme.Brush(healing ? "#65D6AF" : "#69CCF0"), StrokeThickness = 2 };
        Children.Add(line);
        for (int i = 0; i < data.Length; i++) {
            var row = data[i]; double value = healing ? row.HPS : row.DPS;
            double x = 55 + (data.Length == 1 ? width / 2 : width * i / (data.Length - 1)), y = 12 + height * (1 - value / maximum);
            line.Points.Add(new(x, y)); var dot = new Ellipse { Width = 7, Height = 7, Fill = line.Stroke,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = $"Attempt {rows.Length - data.Length + i + 1:N0} · {row.Date:g}\n{row.Encounter} · {row.Character}\n{value:N1} {(healing ? "HPS" : "DPS")} · {row.Seconds:N1}s\nDouble-click to open this fight" };
            dot.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { OpenFight?.Invoke(row.FightId); e.Handled = true; } };
            ToolTipService.SetInitialShowDelay(dot, 100);
            SetLeft(dot, x - 3.5); SetTop(dot, y - 3.5); Children.Add(dot);
        }
        Text(MeterPresentation.Number(maximum), 0, 5); Text("0", 24, height + 2);
        Text($"{data[0].Date:d} – {data[^1].Date:d} · {(rows.Length > data.Length ? $"latest {data.Length} of {rows.Length:N0}" : $"{data.Length}")} attempts · oldest → newest", 55, height + 27);
    }
}
