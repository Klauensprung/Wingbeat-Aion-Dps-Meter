using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
namespace AionDpsMeter.Local;

internal sealed class RadarOverlayWindow : Window
{
    internal event Action? TrackingRequested;
    readonly RadarCanvas radar = new();
    readonly TextBlock status = new() { Foreground = UiTheme.Brush("#B5C6D9"), FontSize = 10, TextWrapping = TextWrapping.Wrap };
    internal RadarOverlayWindow(Preferences settings, Action save)
    {
        Title = Localization.T("3D radar"); Width = double.IsFinite(settings.RadarWidth) ? Math.Clamp(settings.RadarWidth, 280, 1200) : 420; Height = double.IsFinite(settings.RadarHeight) ? Math.Clamp(settings.RadarHeight, 260, 1000) : 400;
        MinWidth = 280; MinHeight = 260; WindowStyle = WindowStyle.None; AllowsTransparency = true;
        ResizeMode = ResizeMode.CanResizeWithGrip; Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        UiTheme.Apply(this, settings); Background = Brushes.Transparent;
        var root = new DockPanel { Margin = new(12, 28, 12, 6) };
        var header = new DockPanel { Background = Brushes.Transparent, Margin = new(0, 0, 0, 6) };
        var title = new TextBlock { Text = Localization.T("3D radar"), FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
        var choices = new[] { new {Value=0,Label=Localization.T("Detected")}, new {Value=1000,Label="1000"}, new {Value=3000,Label="3000"}, new {Value=6000,Label="6000"}, new {Value=10000,Label="10000"} };
        var range = new ComboBox { Width = 92, ItemsSource = choices, DisplayMemberPath="Label", SelectedValuePath="Value", SelectedValue = settings.RadarRange };
        if (range.SelectedIndex < 0) range.SelectedIndex = 0;
        radar.Range = (int)range.SelectedValue;
        range.SelectionChanged += (_, _) => { if (range.SelectedValue is int value) { radar.Range = value; settings.RadarRange = value; save(); radar.InvalidateVisual(); } };
        range.ToolTip = Localization.T("Cube radius in world units"); DockPanel.SetDock(range, Dock.Right); header.Children.Add(range);
        var track = new Button { Content = Localization.T("Track"), Padding = new(8,4,8,4), Margin = new(0,0,6,0), FontSize = 11 };
        track.Click += (_,_) => TrackingRequested?.Invoke(); DockPanel.SetDock(track,Dock.Right); header.Children.Add(track); header.Children.Add(title);
        title.MouseLeftButtonDown += (_, e) => { DragMove(); e.Handled = true; };
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new DockPanel(); var grip = new ResizeGrip { Width = 16, Height = 16, Opacity = .65, VerticalAlignment = VerticalAlignment.Bottom };
        DockPanel.SetDock(grip, Dock.Right); footer.Children.Add(grip); footer.Children.Add(status); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(radar); Content = root;
        if (settings.RadarLeft is double x && settings.RadarTop is double y && double.IsFinite(x) && double.IsFinite(y)) {
            Left = Math.Clamp(x, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + Math.Max(0, SystemParameters.VirtualScreenWidth - Width));
            Top = Math.Clamp(y, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + Math.Max(0, SystemParameters.VirtualScreenHeight - Height));
        }
        var menu = UiTheme.Menu(this); var close = new MenuItem { Header = Localization.T("Close overlay") }; close.Click += (_, _) => Close(); menu.Items.Add(close); ContextMenu = menu;
        OverlayPlacement.Track(this, () => { settings.RadarLeft = Left; settings.RadarTop = Top; settings.RadarWidth = Width; settings.RadarHeight = Height; save(); });
        Closed += (_, _) => radar.Stop();
    }
    internal void Update(RadarFrame frame)
    {
        radar.Update(frame);
        status.Text = Localization.T(frame.Origin == null ? "Waiting for fresh player position" : "Red: enemies / Green: detected materials / Forward: movement") +
            (frame.Origin != null && !RadarSpace.Fresh(frame.Origin,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) ? "\n" + Localization.T("Delayed updates - showing last known scan") : "") +
            (frame.Estimated ? "\n"+Localization.T("Estimated position")+$" (~{frame.Accuracy:N0})" : "") +
            (!frame.HasAltitude ? "\n"+Localization.T("Altitude unknown - horizontal scan") : "") +
            (frame.Origin == null ? "" : $"\nX {frame.Origin.X:N0}  Y {frame.Origin.Y:N0}"+(frame.HasAltitude?$"  Z {frame.Origin.Z:N0}":""));
    }
}
internal sealed class RadarCanvas : FrameworkElement
{
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    RadarFrame frame = new(null, []);
    CapturedPosition? previous;
    readonly RadarMotion motion = new();
    double heading, targetHeading;
    long lastTick;
    internal double Range { get; set; }
    double detectedRange=1000;
    internal bool HasHeading {get;private set;}
    internal RadarCanvas()
    {
        ClipToBounds = true;
        timer.Tick += (_, _) => {
            double delta = Math.Atan2(Math.Sin(targetHeading - heading), Math.Cos(targetHeading - heading));
            double dt = Math.Clamp((Environment.TickCount64 - lastTick) / 1000d, 0, .1); lastTick = Environment.TickCount64;
            heading += delta * (1 - Math.Exp(-dt * 10)); InvalidateVisual();
            if (Math.Abs(delta) < .001 && (frame.Movement == null || motion.Desynced)) timer.Stop();
        };
        IsVisibleChanged += (_, _) => { if (!IsVisible) timer.Stop(); else Animate(); };
    }
    internal void Stop() => timer.Stop();
    void Animate() { if (IsVisible) { lastTick = Environment.TickCount64; timer.Start(); } }
    internal void Update(RadarFrame next)
    {
        var p = next.Origin;
        if (p == null) { motion.Clear(); previous = null; HasHeading = false; detectedRange=1000; frame = next; timer.Stop(); InvalidateVisual(); return; }
        if(previous?.MapId!=p.MapId || next.Movement?.Scene!=frame.Movement?.Scene || next.Movement?.Relocation!=frame.Movement?.Relocation) detectedRange=1000;
        if (previous == null || previous.MapId != p.MapId) { heading = targetHeading = 0; HasHeading = false; }
        else if (p.Timestamp > previous.Timestamp) {
            double dx = p.X - previous.X, dy = p.Y - previous.Y, distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance > 10000 || next.Movement?.Scene != frame.Movement?.Scene || next.Movement?.Relocation != frame.Movement?.Relocation) { heading = targetHeading = 0; HasHeading = false; }
            else if (distance > 30) { targetHeading = Math.Atan2(dx, -dy); HasHeading = true; }
        }
        previous = p; frame = next; Animate(); InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        var center = new Point(ActualWidth * .5, ActualHeight * .5); double scale = Math.Max(1, Math.Min(ActualWidth / 2.8, ActualHeight / 3.5));
        // Camera behind and above the player, looking along movement heading.
        // Forward stays centered and points up; perspective distinguishes near
        // and far planes without the previous diagonal/isometric side view.
        double tilt = Math.Atan2(.6, .8) - 15 * Math.PI / 180;
        double sinTilt = Math.Sin(tilt), cosTilt = Math.Cos(tilt);
        Point Project(double x, double y, double z) {
            double perspective = 5 / (5 + cosTilt * y - sinTilt * z);
            return new(center.X + scale * x * perspective,
                center.Y - scale * (sinTilt * y + cosTilt * z) * perspective);
        }
        void Text(string text, Point p, Brush brush, double size = 10) => dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), p);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var edge = new Pen(UiTheme.Brush("#704E768D"), 1); var grid = new Pen(UiTheme.Brush("#303B667B"), 1);
        foreach (double z in new[] { -1d, 0d, 1d }) {
            var plane = new StreamGeometry(); using (var path = plane.Open()) { path.BeginFigure(Project(-1,-1,z),true,true); path.LineTo(Project(1,-1,z),true,false); path.LineTo(Project(1,1,z),true,false); path.LineTo(Project(-1,1,z),true,false); }
            dc.DrawGeometry(UiTheme.Brush(z == 0 ? "#182C7B88" : "#080B1720"), edge, plane);
            for (double k = -.5; k <= .5; k += .5) { dc.DrawLine(grid, Project(k,-1,z),Project(k,1,z)); dc.DrawLine(grid,Project(-1,k,z),Project(1,k,z)); }
        }
        foreach (double x in new[] { -1d, 1d }) foreach (double y in new[] { -1d, 1d }) dc.DrawLine(edge, Project(x,y,-1), Project(x,y,1));
        var white = UiTheme.Brush("#D9F6FF");
        dc.DrawLine(new Pen(white,2),Project(0,0,0),Project(0,.38,0)); dc.DrawLine(new Pen(white,2),Project(0,.38,0),Project(-.07,.25,0));dc.DrawLine(new Pen(white,2),Project(0,.38,0),Project(.07,.25,0));
        dc.DrawEllipse(white,null,Project(0,0,0),4,4);
        Text("+Z",Project(-1,-1,1),white); Text("-Z",Project(-1,-1,-1),white);
        Text(Localization.T(HasHeading ? "Movement heading" : "North-up / waiting for movement"),new(4,2),white);
        if (motion.Present(frame,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).Origin is not {} origin) return;
        detectedRange=Math.Max(detectedRange,RadarSpace.DetectedRange(frame,origin));
        double range=Range>0?Range:detectedRange;
        var points = RadarSpace.Project(frame,origin,heading,range,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        foreach (var (point,pos,atEdge) in points) {
            var at = Project(pos.Right/range,pos.Forward/range,pos.Up/range); var floor = Project(pos.Right/range,pos.Forward/range,0);
            var brush = UiTheme.Brush(point.Enemy ? "#FF6375" : "#64E4A9");
            dc.DrawLine(new Pen(UiTheme.Brush(point.Enemy ? "#60FF6375" : "#6064E4A9"),1){DashStyle=DashStyles.Dot},floor,at);
            dc.DrawEllipse(null,new Pen(brush,.7),floor,2,1.5); dc.DrawEllipse(atEdge?null:brush,new Pen(atEdge?brush:UiTheme.Brush("#D0101823"),1),at,point.Enemy?4:3.5,point.Enemy?4:3.5);
        }
        // Label only the closest few to avoid an unreadable cloud of text.
        foreach (var (point,pos,atEdge) in points.OrderBy(p => p.Pos.Right*p.Pos.Right+p.Pos.Forward*p.Pos.Forward+p.Pos.Up*p.Pos.Up).Take(3)) {
            var at=Project(pos.Right/range,pos.Forward/range,pos.Up/range); string name=point.Name.Length>20?point.Name[..18]+"..":point.Name;
            Text((atEdge?"~ ":"")+name,new(at.X+7,at.Y-13),UiTheme.Brush(point.Enemy?"#FFADB6":"#91E8BD"));
        }
        Text($"{points.Count(p=>p.Point.Enemy)} {Localization.T("enemies")} / {points.Count(p=>!p.Point.Enemy)} {Localization.T("materials")}",new(4,ActualHeight-15),white);
    }
}
