using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AionDpsMeter.Local;

internal sealed class TrendChart : Canvas
{
    TrendRow[] rows = [];
    bool healing;
    public TrendChart() { Height = 190; MinWidth = 300; Margin = new(10); SizeChanged += (_, _) => Draw(); }
    public void Update(TrendRow[] value, bool showHealing) { if (rows.SequenceEqual(value) && healing == showHealing) return; rows = value; healing = showHealing; Draw(); }
    void Draw()
    {
        Children.Clear();
        var data = rows.TakeLast(100).ToArray();
        void Text(string text, double x, double y) { var label = new TextBlock { Text = text, Foreground = UiTheme.Brush("#AAB6C8"), FontSize = 11 }; SetLeft(label, x); SetTop(label, y); Children.Add(label); }
        if (data.Length == 0) { Text("Save or import fights to see this character’s performance over time.", 12, 70); return; }
        double width = Math.Max(240, ActualWidth - 80), height = 130, maximum = Math.Max(1, data.Max(r => healing ? r.HPS : r.DPS));
        var line = new Polyline { Stroke = UiTheme.Brush(healing ? "#65D6AF" : "#69CCF0"), StrokeThickness = 2 };
        Children.Add(line);
        for (int i = 0; i < data.Length; i++) {
            var row = data[i]; double value = healing ? row.HPS : row.DPS;
            double x = 55 + (data.Length == 1 ? width / 2 : width * i / (data.Length - 1)), y = 12 + height * (1 - value / maximum);
            line.Points.Add(new(x, y)); var dot = new Ellipse { Width = 7, Height = 7, Fill = line.Stroke,
                ToolTip = $"{row.Date:g} · {row.Encounter}\n{row.Character} · server {row.Server} · {row.CP:N0} CP\n{value:N1} {(healing ? "HPS" : "DPS")} · {row.Seconds:N1}s" };
            SetLeft(dot, x - 3); SetTop(dot, y - 3); Children.Add(dot);
        }
        Text(MeterPresentation.Number(maximum), 0, 5); Text("0", 24, 132);
        Text(data[0].Date.ToString("g"), 55, 153); Text($"{data.Length} fights · chronological · {(healing ? "HPS" : "DPS")}", Math.Max(210, width - 150), 153);
    }
}
